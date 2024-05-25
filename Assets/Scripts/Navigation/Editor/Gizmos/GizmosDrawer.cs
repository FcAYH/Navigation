using Navigation.PipelineData;
using Navigation.PreferenceData;
using Navigation.Utilities;
using Navigation.Flags;
using Navigation.Finder.Editor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using Path = System.IO.Path;

namespace Navigation.Display
{
    public class GizmosDrawer : Singleton<GizmosDrawer>
    {
        public bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;

                if (_enabled)
                {
                    SceneView.RepaintAll();

                    var go = GameObject.Find("__NavMeshGizmos__");
                    if (go == null)
                    {
                        GameObject empty = new GameObject("__NavMeshGizmos__");
                        _gameObjectToShowGizmos = empty.AddComponent<GameObjectToShowGizmos>();
                    }
                    else
                    {
                        var component = go.GetComponent<GameObjectToShowGizmos>();
                        if (component == null)
                            component = go.AddComponent<GameObjectToShowGizmos>();

                        _gameObjectToShowGizmos = component;
                    }

                    _selectedAgentIndex = 0;
                    _selectedTileIndex = 0;
                    _displayData = null;
                    LoadData();
                }
                else
                {
                    var go = GameObject.Find("__NavMeshGizmos__");
                    while (go != null)
                    {
                        GameObject.DestroyImmediate(go);
                        go = GameObject.Find("__NavMeshGizmos__");
                    }

                    _gameObjectToShowGizmos = null;
                }

                _type = GizmosType.NavMesh;
            }
        }

        private bool _enabled = false;
        private GameObjectToShowGizmos _gameObjectToShowGizmos;
        private int _selectedTileIndex = 0;
        private int _selectedAgentIndex = 0;

        IPipelineData _displayData;
        private BuildInfo _buildInfo;
        private Agents _agents;

        private GizmosType _type;
        private bool _selectedTileIndexChanged = false;
        private bool _selectedAgentIndexChanged = false;

        [InitializeOnLoadMethod]
        public static void InitializeOnLoadMethod()
        {
            SceneView.duringSceneGui += Instance.OnSceneGUI;
        }

        [MenuItem("Navigation/Show Gizmos", priority = 1)]
        private static void ShowGizmos()
        {
            Instance.Enabled = true;
        }

        private void LoadData()
        {
            Instance._buildInfo = AssetDatabase.LoadAssetAtPath<BuildInfo>(Utilities.Path.BuildInfoAssetPath);
            Instance._agents = AssetDatabase.LoadAssetAtPath<Agents>(Utilities.Path.AgentsAssetPath);
        }


        protected override void OnCreate()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _selectedAgentIndex = 0;
            _selectedTileIndex = 0;
            _displayData = null;
        }


        private void OnSceneUnloaded(Scene scene)
        {
            var go = GameObject.Find("__NavMeshGizmos__");
            while (go != null)
            {
                GameObject.DestroyImmediate(go);
                go = GameObject.Find("__NavMeshGizmos__");
            }

            _gameObjectToShowGizmos = null;
        }

        public static void DrawGizmos()
        {
            Instance.OnDrawGizmos();
        }

        public void OnDrawGizmos()
        {
            if (!Enabled) return;

            LoadNavMeshData();

            if (_displayData != null)
            {
                switch (_type)
                {
                    case GizmosType.SolidHeightField:
                        DrawSolidHeightField();
                        break;
                    case GizmosType.CompactHeightField:
                        DrawCompactHeightField();
                        break;
                    case GizmosType.DistanceField:
                        DrawDistanceField();
                        break;
                    case GizmosType.Regions:
                        DrawRegions();
                        break;
                    case GizmosType.RawContours:
                        DrawContours(false);
                        break;
                    case GizmosType.SimplifiedContours:
                        DrawContours(true);
                        break;
                    case GizmosType.PolyMeshField:
                        DrawPolyMeshField();
                        break;
                    case GizmosType.TriangleMesh:
                        DrawTriangleMesh();
                        break;
                    case GizmosType.NavMesh:
                        DrawNavMesh();
                        break;
                    default:
                        break;
                }
            }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!Enabled) return;

            Handles.BeginGUI();

            var rect = new Rect(sceneView.position.width - 155, sceneView.position.height - 120, 150, 90);
            GUILayout.BeginArea(rect, GUI.skin.button);

            EditorGUILayout.BeginHorizontal();

            GUILayout.Label("Gizmos Type", EditorStyles.boldLabel, GUILayout.Width(100));
            GUILayout.Space(25);

            if (GUILayout.Button("×", EditorStyles.label, GUILayout.Width(20)))
            {
                Enabled = false;
            }

            EditorGUILayout.EndHorizontal();

            GUILayout.Space(5);

            var activeScene = SceneManager.GetActiveScene();
            string scenePath = Path.Combine(Application.dataPath,
                                    activeScene.path.Substring(7));
            FileInfo sceneFile = new FileInfo(scenePath);
            string dataFolderPath = Path.Combine(sceneFile.DirectoryName, activeScene.name + "_Navigation");

            if (!Directory.Exists(dataFolderPath))
                EditorGUILayout.LabelField("There is no data to show.");
            else
            {
                string[] agentNames = _agents.AgentList.Select(a => a.Name).ToArray();
                int agentIndex = EditorGUILayout.Popup(_selectedAgentIndex, agentNames);
                if (agentIndex != _selectedAgentIndex)
                    _selectedAgentIndexChanged = true;
                _selectedAgentIndex = agentIndex;

                string agentFolder = Path.Combine(dataFolderPath, agentNames[_selectedAgentIndex]);
                if (Directory.Exists(agentFolder))
                {
                    _type = (GizmosType)EditorGUILayout.EnumPopup(_type);

                    string tilesFolder = Path.Combine(agentFolder, _type.ToString());
                    // TODO: 这里有点丑写的，后面再改
                    if (_type == GizmosType.DistanceField || _type == GizmosType.Regions)
                        tilesFolder = Path.Combine(agentFolder, "CompactHeightField");

                    if (_type == GizmosType.RawContours || _type == GizmosType.SimplifiedContours)
                        tilesFolder = Path.Combine(agentFolder, "ContourSet");

                    if (Directory.Exists(tilesFolder))
                    {
                        DirectoryInfo dir = new DirectoryInfo(tilesFolder);
                        string[] tileNames = new string[dir.GetFiles("*.json").Length];
                        for (int i = 0; i < tileNames.Length; i++)
                        {
                            tileNames[i] = "Tile " + i;
                        }

                        int tileIndex = EditorGUILayout.Popup(_selectedTileIndex, tileNames);
                        if (tileIndex != _selectedTileIndex)
                            _selectedTileIndexChanged = true;

                        _selectedTileIndex = tileIndex;
                    }
                    else
                    {
                        EditorGUILayout.LabelField("There is no data to show.");
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("There is no data to show.");
                }
            }

            GUILayout.EndArea();
            Handles.EndGUI();
        }

        private void LoadNavMeshData()
        {
            Type type = _type switch
            {
                GizmosType.SolidHeightField => typeof(SolidHeightField),
                GizmosType.CompactHeightField => typeof(CompactHeightField),
                GizmosType.DistanceField => typeof(CompactHeightField),
                GizmosType.Regions => typeof(CompactHeightField),
                GizmosType.RawContours => typeof(ContourSet),
                GizmosType.SimplifiedContours => typeof(ContourSet),
                GizmosType.PolyMeshField => typeof(PolyMeshField),
                GizmosType.TriangleMesh => typeof(TriangleMesh),
                GizmosType.NavMesh => typeof(NavMesh),
                _ => null,
            };


            if (_displayData == null || _displayData.GetType() != type
                    || _selectedTileIndexChanged || _selectedAgentIndexChanged)
            {
                EditorUtility.DisplayProgressBar("Loading Data", $"Type of {type.Name}", 0.5f);

                // 获取数据
                var activeScene = SceneManager.GetActiveScene();
                string scenePath = Path.Combine(Application.dataPath,
                                        activeScene.path.Substring(7));
                FileInfo sceneFile = new FileInfo(scenePath);
                string dataFolderPath = Path.Combine(sceneFile.DirectoryName, activeScene.name + "_Navigation");
                string agentFolderPath = Path.Combine(dataFolderPath, _agents.AgentList[_selectedAgentIndex].Name);
                string displayDataFolderPath = Path.Combine(agentFolderPath, type.Name);
                if (Directory.Exists(displayDataFolderPath))
                {
                    DirectoryInfo dir = new DirectoryInfo(displayDataFolderPath);
                    var json = dir.GetFiles($"tile{_selectedTileIndex}.json").First();

                    MethodInfo loadFromJsonMethod = type.GetMethod("LoadFromJson");
                    _displayData = null;
                    _displayData = (IPipelineData)loadFromJsonMethod?.Invoke(null, new object[] { json.FullName });

                    _selectedTileIndexChanged = false;
                    _selectedAgentIndexChanged = false;
                }
                else
                {
                    _displayData = null;
                }

                EditorUtility.ClearProgressBar();
            }
        }

        private void DrawSolidHeightField()
        {
            var shf = _displayData as SolidHeightField;

            // draw Tile
            Gizmos.DrawWireCube(shf.CurrentTile.Min + (shf.CurrentTile.Max - shf.CurrentTile.Min) / 2,
                                    (shf.CurrentTile.Max - shf.CurrentTile.Min));

            // 绘制
            foreach (var id in shf.SpanDict.Keys)
            {
                int curX = id % shf.VoxelNumX;
                int curZ = id / shf.VoxelNumX;

                var currentSpan = shf.SpanDict[id];
                while (currentSpan != null)
                {
                    // 绘制当前span
                    Vector3 drawBoxPosition = new Vector3
                    {
                        x = shf.CurrentTile.Min.x + _buildInfo.CellSize * curX + _buildInfo.CellSize / 2,
                        y = shf.CurrentTile.Min.y + _buildInfo.CellHeight * currentSpan.Top + _buildInfo.CellHeight / 2,
                        z = shf.CurrentTile.Min.z + _buildInfo.CellSize * curZ + _buildInfo.CellSize / 2
                    };

                    Vector3 drawBoxSize = new Vector3
                    {
                        x = _buildInfo.CellSize,
                        y = _buildInfo.CellHeight,
                        z = _buildInfo.CellSize
                    };

                    if (currentSpan.Area == AreaMask.NotWalkable)
                        Gizmos.color = Color.red;
                    else
                        Gizmos.color = Color.green;
                    Gizmos.DrawWireCube(drawBoxPosition, drawBoxSize);

                    currentSpan = currentSpan.Next;
                }
            }
        }


        private void DrawCompactHeightField()
        {
            var chf = _displayData as CompactHeightField;

            // draw Tile
            Gizmos.DrawWireCube(chf.CurrentTile.Min + (chf.CurrentTile.Max - chf.CurrentTile.Min) / 2,
                                    (chf.CurrentTile.Max - chf.CurrentTile.Min));

            // 绘制
            for (int id = 0; id < chf.Cells.Length; id++)
            {
                var curX = id % chf.VoxelNumX;
                var curZ = id / chf.VoxelNumX;

                var cell = chf.Cells[id];
                if (cell.Count == 0) continue;

                for (int i = 0; i < cell.Count; i++)
                {
                    var span = chf.Spans[cell.FirstSpan + i];

                    // 绘制当前span
                    Vector3 drawBoxPosition = new Vector3
                    {
                        x = chf.CurrentTile.Min.x + _buildInfo.CellSize * curX + _buildInfo.CellSize / 2,
                        y = chf.CurrentTile.Min.y + _buildInfo.CellHeight * span.Floor + _buildInfo.CellHeight / 2,
                        z = chf.CurrentTile.Min.z + _buildInfo.CellSize * curZ + _buildInfo.CellSize / 2
                    };

                    Vector3 drawBoxSize = new Vector3
                    {
                        x = _buildInfo.CellSize,
                        y = _buildInfo.CellHeight,
                        z = _buildInfo.CellSize
                    };

                    if (span.Area == AreaMask.NotWalkable)
                        Gizmos.color = Color.red;
                    else
                        Gizmos.color = Color.green;
                    Gizmos.DrawWireCube(drawBoxPosition, drawBoxSize);
                }
            }
        }

        private float _maxDistanceToBorder = -1f;
        private void DrawDistanceField()
        {
            var chf = _displayData as CompactHeightField;
            if (_maxDistanceToBorder < 0f)
            {
                foreach (var span in chf.Spans)
                    _maxDistanceToBorder = Mathf.Max(_maxDistanceToBorder, span.DistanceToBorder);
            }

            // draw Tile
            Gizmos.DrawWireCube(chf.CurrentTile.Min + (chf.CurrentTile.Max - chf.CurrentTile.Min) / 2,
                                    (chf.CurrentTile.Max - chf.CurrentTile.Min));

            // 绘制
            for (int id = 0; id < chf.Cells.Length; id++)
            {
                var curX = id % chf.VoxelNumX;
                var curZ = id / chf.VoxelNumX;

                var cell = chf.Cells[id];
                if (cell.Count == 0) continue;

                for (int i = 0; i < cell.Count; i++)
                {
                    var span = chf.Spans[cell.FirstSpan + i];

                    // 绘制当前span
                    Vector3 drawBoxPosition = new Vector3
                    {
                        x = chf.CurrentTile.Min.x + _buildInfo.CellSize * curX + _buildInfo.CellSize / 2,
                        y = chf.CurrentTile.Min.y + _buildInfo.CellHeight * span.Floor + _buildInfo.CellHeight / 2,
                        z = chf.CurrentTile.Min.z + _buildInfo.CellSize * curZ + _buildInfo.CellSize / 2
                    };

                    Vector3 drawBoxSize = new Vector3
                    {
                        x = _buildInfo.CellSize,
                        y = _buildInfo.CellHeight,
                        z = _buildInfo.CellSize
                    };

                    // float ratio = (float)span.DistanceToBorder / (float)_maxDistanceToBorder;
                    // Color drawColor = new Color(1 - (ratio - 1) * (ratio - 1), 0, 0);

                    Gizmos.color = drawColor[(span.DistanceToBorder / 2) % 6];
                    Gizmos.DrawWireCube(drawBoxPosition, drawBoxSize);
                }
            }
        }

        private Color[] drawColor = new Color[] { Color.red, Color.blue, Color.cyan, Color.green, Color.magenta, Color.yellow };
        private void DrawRegions()
        {
            var chf = _displayData as CompactHeightField;

            // draw Tile
            Gizmos.DrawWireCube(chf.CurrentTile.Min + (chf.CurrentTile.Max - chf.CurrentTile.Min) / 2,
                                    (chf.CurrentTile.Max - chf.CurrentTile.Min));

            // 绘制
            for (int id = 0; id < chf.Cells.Length; id++)
            {
                var curX = id % chf.VoxelNumX;
                var curZ = id / chf.VoxelNumX;

                var cell = chf.Cells[id];
                if (cell.Count == 0) continue;

                for (int i = 0; i < cell.Count; i++)
                {
                    var span = chf.Spans[cell.FirstSpan + i];

                    // 绘制当前span
                    Vector3 drawBoxPosition = new Vector3
                    {
                        x = chf.CurrentTile.Min.x + _buildInfo.CellSize * curX + _buildInfo.CellSize / 2,
                        y = chf.CurrentTile.Min.y + _buildInfo.CellHeight * span.Floor + _buildInfo.CellHeight / 2,
                        z = chf.CurrentTile.Min.z + _buildInfo.CellSize * curZ + _buildInfo.CellSize / 2
                    };

                    Vector3 drawBoxSize = new Vector3
                    {
                        x = _buildInfo.CellSize,
                        y = _buildInfo.CellHeight,
                        z = _buildInfo.CellSize
                    };

                    if (span.RegionId == 0)
                        Gizmos.color = Color.black;
                    else
                        Gizmos.color = drawColor[span.RegionId % drawColor.Length];
                    Gizmos.DrawWireCube(drawBoxPosition, drawBoxSize);
                }
            }
        }

        private void DrawContours(bool isSimplified)
        {
            var cs = _displayData as ContourSet;

            // draw Tile
            Gizmos.DrawWireCube(cs.CurrentTile.Min + (cs.CurrentTile.Max - cs.CurrentTile.Min) / 2,
                                    (cs.CurrentTile.Max - cs.CurrentTile.Min));

            // 绘制
            for (int i = 0; i < cs.Count; i++)
            {
                var contour = cs[i];
                int[] vertices = isSimplified ? contour.Vertices : contour.RawVertices;
                for (int j = 0; j < vertices.Length; j += 4)
                {
                    Vector3 drawLineStart = new Vector3
                    {
                        x = cs.CurrentTile.Min.x + vertices[j] * _buildInfo.CellSize,
                        y = cs.CurrentTile.Min.y + vertices[j + 1] * _buildInfo.CellHeight + _buildInfo.CellHeight,
                        z = cs.CurrentTile.Min.z + vertices[j + 2] * _buildInfo.CellSize
                    };

                    var k = (j + 4) % vertices.Length;
                    Vector3 drawLineEnd = new Vector3
                    {
                        x = cs.CurrentTile.Min.x + vertices[k] * _buildInfo.CellSize,
                        y = cs.CurrentTile.Min.y + vertices[k + 1] * _buildInfo.CellHeight + _buildInfo.CellHeight,
                        z = cs.CurrentTile.Min.z + vertices[k + 2] * _buildInfo.CellSize
                    };

                    // Debug.Log(drawLineStart + " " + drawLineEnd);

                    if (isSimplified)
                    {
                        Gizmos.color = drawColor[(j / 4) % drawColor.Length];
                        Gizmos.DrawSphere(drawLineStart, 0.1f);
                    }

                    Gizmos.color = drawColor[contour.RegionId % drawColor.Length];
                    Gizmos.DrawLine(drawLineStart, drawLineEnd);
                }
            }
        }

        private void DrawPolyMeshField()
        {
            var pmf = _displayData as PolyMeshField;

            // draw Tile
            Gizmos.DrawWireCube(pmf.CurrentTile.Min + (pmf.CurrentTile.Max - pmf.CurrentTile.Min) / 2,
                                    (pmf.CurrentTile.Max - pmf.CurrentTile.Min));

            // 绘制
            for (int i = 0; i < pmf.RegionIndices.Length; i++)
            {
                int poly = i * _buildInfo.VerticesPerPoly * 2;
                for (int j = 0; j < _buildInfo.VerticesPerPoly; j++)
                {
                    int cur = poly + j;
                    if (pmf.Polygons[cur] == -1)
                        break;

                    int next = (j + 1 >= _buildInfo.VerticesPerPoly || pmf.Polygons[cur + 1] == -1)
                                ? poly : cur + 1;

                    int indexA = pmf.Polygons[cur] * 3;
                    int indexB = pmf.Polygons[next] * 3;
                    int[] vertexA = pmf.Vertices[indexA..(indexA + 3)];
                    int[] vertexB = pmf.Vertices[indexB..(indexB + 3)];

                    Vector3 drawLineStart = new Vector3
                    {
                        x = pmf.CurrentTile.Min.x + vertexA[0] * _buildInfo.CellSize,
                        y = pmf.CurrentTile.Min.y + vertexA[1] * _buildInfo.CellHeight + _buildInfo.CellHeight,
                        z = pmf.CurrentTile.Min.z + vertexA[2] * _buildInfo.CellSize
                    };

                    Vector3 drawLineEnd = new Vector3
                    {
                        x = pmf.CurrentTile.Min.x + vertexB[0] * _buildInfo.CellSize,
                        y = pmf.CurrentTile.Min.y + vertexB[1] * _buildInfo.CellHeight + _buildInfo.CellHeight,
                        z = pmf.CurrentTile.Min.z + vertexB[2] * _buildInfo.CellSize
                    };

                    // Debug.Log(drawLineStart + " " + drawLineEnd);

                    Gizmos.color = drawColor[pmf.RegionIndices[i] % drawColor.Length];
                    Gizmos.DrawLine(drawLineStart, drawLineEnd);
                }
            }
        }

        private void DrawTriangleMesh()
        {
            var tm = _displayData as TriangleMesh;

            // draw Tile
            Gizmos.DrawWireCube(tm.CurrentTile.Min + (tm.CurrentTile.Max - tm.CurrentTile.Min) / 2,
                                    (tm.CurrentTile.Max - tm.CurrentTile.Min));

            // 绘制

            for (int i = 0; i < tm.Indices.Length; i += 3)
            {
                int vertexA = tm.Indices[i];
                int vertexB = tm.Indices[i + 1];
                int vertexC = tm.Indices[i + 2];

                Vector3 drawA = new Vector3
                {
                    x = tm.Vertices[vertexA * 3],
                    y = tm.Vertices[vertexA * 3 + 1],
                    z = tm.Vertices[vertexA * 3 + 2]
                };

                Vector3 drawB = new Vector3
                {
                    x = tm.Vertices[vertexB * 3],
                    y = tm.Vertices[vertexB * 3 + 1],
                    z = tm.Vertices[vertexB * 3 + 2]
                };

                Vector3 drawC = new Vector3
                {
                    x = tm.Vertices[vertexC * 3],
                    y = tm.Vertices[vertexC * 3 + 1],
                    z = tm.Vertices[vertexC * 3 + 2]
                };

                Gizmos.color = drawColor[tm.Regions[i / 3] % drawColor.Length];

                Gizmos.DrawLine(drawA, drawB);
                Gizmos.DrawLine(drawB, drawC);
                Gizmos.DrawLine(drawC, drawA);
            }

        }

        private void DrawNavMesh()
        {

        }

        internal enum GizmosType
        {
            SolidHeightField,
            CompactHeightField,
            DistanceField,
            Regions,
            RawContours,
            SimplifiedContours,
            PolyMeshField,
            TriangleMesh,
            NavMesh
        }
    }
}
