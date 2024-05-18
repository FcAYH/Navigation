using Navigation.Utilities;
using Navigation.PipelineData;
using Navigation.PreferenceData;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using System.IO;

namespace Navigation.Finder.Editor
{
    /// <summary>
    /// 这个PathFinder用于在Editor界面下测试
    /// 因为仅做测试使用，所以是一个半成品，使用当前场景 第一个Agent（默认为"Human"） 的 NavMesh
    /// 且仅使用Tile0的数据
    /// </summary>
    public class PathFinder : Singleton<PathFinder>
    {
        public Vector3 Start;
        public Vector3 Destination;
        public List<Vector3> Path = new List<Vector3>();

        private PolyMeshField _pmf;
        private Agents _agents;
        private Polygon[] _polygons;

        // 用 Polygon 直接 A* 好像很蠢，还是再做个 Vector3的图吧
        // 每个 Vector3 对应一个 HashSet 表示这个点的邻居
        private Dictionary<Vector3, HashSet<Vector3>> _graph;


        private Polygon _startPolygon;
        private Polygon _destinationPolygon;

        protected override void OnCreate()
        {
            _agents = AssetDatabase.LoadAssetAtPath<Agents>(Utilities.Path.AgentsAssetPath);
        }

        private bool LoadData()
        {
            var activeScene = SceneManager.GetActiveScene();
            string scenePath = System.IO.Path.Combine(Application.dataPath,
                                    activeScene.path.Substring(7));
            FileInfo sceneFile = new FileInfo(scenePath);
            string dataFolderPath = System.IO.Path.Combine(sceneFile.DirectoryName, activeScene.name + "_Navigation");
            string agentFolderPath = System.IO.Path.Combine(dataFolderPath, _agents.AgentList[0].Name);
            string displayDataFolderPath = System.IO.Path.Combine(agentFolderPath, "PolyMeshField");
            if (Directory.Exists(displayDataFolderPath))
            {
                DirectoryInfo dir = new DirectoryInfo(displayDataFolderPath);
                var json = dir.GetFiles($"tile0.json").First();

                _pmf = PolyMeshField.LoadFromJson(json.FullName);
            }

            return _pmf != null;
        }

        private bool LoadStartAndDestination()
        {
            var startPoint = GameObject.Find("startPoint");
            var endPoint = GameObject.Find("endPoint");

            if (startPoint == null || endPoint == null)
            {
                return false;
            }

            Start = startPoint.transform.position;
            Destination = endPoint.transform.position;

            return true;
        }

        public bool FindPath()
        {
            if (!LoadStartAndDestination())
            {
                Debug.LogError("Start or Destination Not Found!");
                return false;
            }
            if (_pmf == null)
            {
                if (!LoadData())
                {
                    Debug.LogError("Load NavMesh Data Failed!");
                    return false;
                }
            }

            // 1. 构建地图
            GeneratorPolygons();
            GeneratorGraph();

            // 2. 找到起点，终点所在的 Polygon
            Vector3 start = Start - _pmf.CurrentTile.Min;
            start.x = Mathf.Floor(start.x / 0.2f);
            start.y = Mathf.Floor(start.y / 0.3f);
            start.z = Mathf.Floor(start.z / 0.2f);

            // 5 -> 5 / 0.2 = 25

            Vector3 destination = Destination - _pmf.CurrentTile.Min;
            destination.x = Mathf.Floor(destination.x / 0.2f);
            destination.y = Mathf.Floor(destination.y / 0.3f);
            destination.z = Mathf.Floor(destination.z / 0.2f);

            Polygon startPolygon = null;
            Polygon destinationPolygon = null;

            // 找距离最近的
            float minDistance = float.MaxValue;
            for (int i = 0; i < _polygons.Length; i++)
            {
                // < 0 表示点在多边形外
                float distance = PointToPolygonDistance(start, _polygons[i]);
                if (distance >= 0 && distance < minDistance)
                {
                    minDistance = distance;
                    startPolygon = _polygons[i];
                }
            }

            minDistance = float.MaxValue;
            for (int i = 0; i < _polygons.Length; i++)
            {
                float distance = PointToPolygonDistance(destination, _polygons[i]);
                if (distance >= 0 && distance < minDistance)
                {
                    minDistance = distance;
                    destinationPolygon = _polygons[i];
                }
            }

            Debug.Log(startPolygon == null);
            Debug.Log(destinationPolygon == null);
            if (startPolygon == null || destinationPolygon == null)
            {
                Debug.LogError("Start or Destination not in NavMesh!");
                return false;
            }

            _startPolygon = startPolygon;
            _destinationPolygon = destinationPolygon;

            // 补充起点，终点的边
            // 仅需构建起点到起点Polygon的边和终点Polygon到终点的边
            // 无需构建双向这里，因为没必要
            if (!_graph.ContainsKey(start))
            {
                _graph.Add(start, new HashSet<Vector3>());
            }

            for (int i = 0; i < startPolygon.Vertices.Length; i++)
            {
                _graph[start].Add(startPolygon.Vertices[i]);
            }

            if (!_graph.ContainsKey(destination))
            {
                _graph.Add(destination, new HashSet<Vector3>());
            }

            for (int i = 0; i < destinationPolygon.Vertices.Length; i++)
            {
                _graph[destinationPolygon.Vertices[i]].Add(destination);
            }

            // 3. 使用 A* 算法寻路
            bool hasPath = AStar(start, startPolygon, destination, destinationPolygon);

            if (hasPath)
            {
                float length = 0;
                for (int i = 0; i < Path.Count - 1; i++)
                {
                    Vector3 p1 = new Vector3
                    {
                        x = _pmf.CurrentTile.Min.x + Path[i].x * 0.2f,
                        y = _pmf.CurrentTile.Min.y + Path[i].y * 0.3f + 0.3f,
                        z = _pmf.CurrentTile.Min.z + Path[i].z * 0.2f
                    };
                    Vector3 p2 = new Vector3
                    {
                        x = _pmf.CurrentTile.Min.x + Path[i + 1].x * 0.2f,
                        y = _pmf.CurrentTile.Min.y + Path[i + 1].y * 0.3f + 0.3f,
                        z = _pmf.CurrentTile.Min.z + Path[i + 1].z * 0.2f
                    };
                    length += Vector3.Distance(p1, p2);
                }

                Debug.Log("Find path successfully! Length: " + length);
            }
            else
            {
                Debug.Log("Find path failed!");
            }

            return hasPath;
        }

        private void GeneratorPolygons()
        {
            _polygons = new Polygon[_pmf.RegionIndices.Length];
            for (int i = 0; i < _pmf.RegionIndices.Length; i++)
            {
                int index = i * 6 * 2; // 注意，我直接默认 VerticesPerPoly = 6了

                int vertexCount = 0;
                while (vertexCount < 6 && _pmf.Polygons[index + vertexCount] != -1)
                {
                    vertexCount++;
                }

                int neighborCount = 0;
                while (neighborCount < 6 && _pmf.Polygons[index + 6 + neighborCount] != -1)
                {
                    neighborCount++;
                }

                _polygons[i] = MakePolygon(index, vertexCount, neighborCount);
            }

            // 为每个多边形设置邻居
            // 好像没啥特别好的办法，只能遍历所有的 Polygon
            for (int i = 0; i < _polygons.Length; i++)
            {
                _polygons[i].Neighbors = new List<Polygon>(6);
                for (int j = 0; j < _polygons.Length; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    for (int k = 0; k < _polygons[i].Vertices.Length; k++)
                    {
                        if (_polygons[j].Vertices.Contains(_polygons[i].Vertices[k]))
                        {
                            _polygons[i].Neighbors.Add(_polygons[i]);
                            break;
                        }
                    }
                }
            }

            // 后面可能是需要调整一下 PolyMesh 的数据？或者PolyMesh要多做一些处理
            // 不然像现在这样，Neighbor其实没起到作用，只能用暴力的方式去遍历所有的 Polygon
            // 有点蠢
        }

        private Polygon MakePolygon(int index, int vertexCount, int neighborCount)
        {
            Polygon p = new Polygon();

            p.Vertices = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                int vertex = _pmf.Polygons[index + i];

                p.Vertices[i] = new Vector3(_pmf.Vertices[vertex * 3],
                                                _pmf.Vertices[vertex * 3 + 1],
                                                _pmf.Vertices[vertex * 3 + 2]);
            }

            // 目前是以 Polygon 顶点进行寻路的，后面会改成使用边中点
            // p.Vertices = new Vector3[vertexCount]; 
            // for (int i = 0, j = vertexCount - 1; i < vertexCount; j = i++)
            // {
            //     p.Vertices[i] = (p.Vertices[i] + p.Vertices[j]) / 2;
            // }

            return p;
        }

        private void GeneratorGraph()
        {
            _graph = new Dictionary<Vector3, HashSet<Vector3>>();

            for (int i = 0; i < _polygons.Length; i++)
            {
                for (int j = 0; j < _polygons[i].Vertices.Length; j++)
                {
                    if (!_graph.ContainsKey(_polygons[i].Vertices[j]))
                    {
                        _graph[_polygons[i].Vertices[j]] = new HashSet<Vector3>();
                    }

                    for (int k = 0; k < _polygons[i].Vertices.Length; k++)
                    {
                        if (j == k)
                        {
                            continue;
                        }

                        _graph[_polygons[i].Vertices[j]].Add(_polygons[i].Vertices[k]);
                    }
                }
            }

            /*
                这里看上去我们只对每个 Polygon 内部顶点之间相互连了边
                但是其实相邻 Polygon 之间的顶点也是相互连的
                因为例如 PolyA 中包含 A 点，则 A 点首先会和 PolyA 中的剩余点构建链接关系
                然后如果 PolyA 的邻居 PolyB 中也包含 A 点，则 A 点会和 PolyB 中的剩余点构建链接关系
                即其实相邻的 Polygon 之间的顶点是共享的
            */
        }

        private float PointToPolygonDistance(Vector3 point, Polygon polygon)
        {
            bool isInside = false;

            // 采用射线法去判断点是否在多边形内
            for (int i = 0, j = polygon.Vertices.Length - 1; i < polygon.Vertices.Length; j = i++)
            {
                // 以 A - B 为边
                Vector3 A = polygon.Vertices[j];
                Vector3 B = polygon.Vertices[i];

                // 从 (x, z) 向 x 正方向发出射线，检测是否和 AB 相交
                if (((B.z > point.z) != (A.z > point.z)) // a, b的 z 坐标同时大于或小于 z 则说明肯定不相交
                        && (point.x < (A.x - B.x) * (point.z - B.z) / (A.z - B.z) + B.x))
                {
                    // 奇数次交点，说明在多边形内，偶数次交点，说明在多边形外
                    isInside = !isInside;
                }
            }

            // 粗略计算顶点和多边形面的距离，主要是为了区分在 y 方向上的多个多边形
            // 使用 point.y 和多边形顶点 y 的均值的差值作为距离
            float average = 0f;
            for (int i = 0; i < polygon.Vertices.Length; i++)
            {
                average += polygon.Vertices[i].y;
            }
            average /= polygon.Vertices.Length;


            return isInside ? Mathf.Abs(average - point.y) : -1f;
        }

        private bool AStar(Vector3 start, Polygon startPolygon, Vector3 destination, Polygon destinationPolygon)
        {
            // G: 起点到当前点的距离， H: 当前点到目的地的距离， F: G + H
            // F 值越小，说明离目的地越近

            Dictionary<Vector3, float> gScore = new Dictionary<Vector3, float>();
            Dictionary<Vector3, float> fScore = new Dictionary<Vector3, float>();

            // 用于记录路径
            Dictionary<Vector3, Vector3> cameFrom = new Dictionary<Vector3, Vector3>();

            // 用于记录已经访问过的点
            HashSet<Vector3> closedSet = new HashSet<Vector3>();

            // 用于记录待访问的点
            HashSet<Vector3> openSet = new HashSet<Vector3>();

            // 初始化
            gScore[start] = 0;
            fScore[start] = Vector3.Distance(start, destination);
            openSet.Add(start);

            while (openSet.Count > 0)
            {
                // 从 openSet 中找到 F 值最小的点
                Vector3 current = openSet.First();
                foreach (Vector3 v in openSet)
                {
                    if (fScore[v] < fScore[current])
                    {
                        current = v;
                    }
                }

                // 如果当前点就是目的地，那么就找到了路径
                if (current == destination)
                {
                    ReconstructPath(cameFrom, current);
                    return true;
                }

                // 从 openSet 中移除当前点
                openSet.Remove(current);

                // 将当前点加入 closedSet
                closedSet.Add(current);

                // 遍历当前点的邻居
                foreach (Vector3 neighbor in _graph[current])
                {
                    // 如果邻居已经在 closedSet 中，那么跳过
                    if (closedSet.Contains(neighbor))
                    {
                        continue;
                    }

                    // 计算从起点到当前点的距离
                    float tentativeGScore = gScore[current] + Vector3.Distance(current, neighbor);

                    // 如果邻居不在 openSet 中，那么加入 openSet
                    if (!openSet.Contains(neighbor))
                    {
                        openSet.Add(neighbor);
                    }
                    // 如果从起点到当前点的距离比从起点到邻居的距离要长，那么跳过
                    else if (tentativeGScore >= gScore[neighbor])
                    {
                        continue;
                    }

                    // 记录当前点是从哪个点过来的
                    cameFrom[neighbor] = current;

                    // 更新 G 值
                    gScore[neighbor] = tentativeGScore;

                    // 更新 F 值
                    fScore[neighbor] = gScore[neighbor] + Vector3.Distance(neighbor, destination);
                }
            }

            return false;
        }

        private void ReconstructPath(Dictionary<Vector3, Vector3> cameFrom, Vector3 current)
        {
            Path.Clear();
            Path.Add(current);
            while (cameFrom.ContainsKey(current))
            {
                current = cameFrom[current];
                Path.Add(current);
            }
            Path.Reverse();
        }

        // 依赖 GizmosDrawer
        public void OnDrawGizmos()
        {
            if (LoadStartAndDestination())
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(Start, 0.5f);
                Gizmos.color = Color.green;
                Gizmos.DrawSphere(Destination, 0.5f);

                for (int i = 0; i < Path.Count - 1; i++)
                {
                    Gizmos.color = Color.blue;
                    Gizmos.DrawSphere(Path[i], 0.5f);

                    Vector3 start = new Vector3
                    {
                        x = _pmf.CurrentTile.Min.x + Path[i].x * 0.2f,
                        y = _pmf.CurrentTile.Min.y + Path[i].y * 0.3f + 0.5f,
                        z = _pmf.CurrentTile.Min.z + Path[i].z * 0.2f
                    };
                    Vector3 end = new Vector3
                    {
                        x = _pmf.CurrentTile.Min.x + Path[i + 1].x * 0.2f,
                        y = _pmf.CurrentTile.Min.y + Path[i + 1].y * 0.3f + 0.5f,
                        z = _pmf.CurrentTile.Min.z + Path[i + 1].z * 0.2f
                    };

                    Gizmos.color = Color.magenta;
                    Gizmos.DrawLine(start, end);
                }

                if (_startPolygon != null)
                {
                    foreach (var vertex in _startPolygon.Vertices)
                    {
                        Gizmos.color = Color.red;
                        Vector3 center = new Vector3
                        {
                            x = _pmf.CurrentTile.Min.x + vertex.x * 0.2f,
                            y = _pmf.CurrentTile.Min.y + vertex.y * 0.3f + 0.3f,
                            z = _pmf.CurrentTile.Min.z + vertex.z * 0.2f
                        };
                        Gizmos.DrawSphere(center, 0.2f);
                    }
                }

                if (_destinationPolygon != null)
                {
                    foreach (var vertex in _destinationPolygon.Vertices)
                    {
                        Gizmos.color = Color.green;
                        Vector3 center = new Vector3
                        {
                            x = _pmf.CurrentTile.Min.x + vertex.x * 0.2f,
                            y = _pmf.CurrentTile.Min.y + vertex.y * 0.3f + 0.3f,
                            z = _pmf.CurrentTile.Min.z + vertex.z * 0.2f
                        };
                        Gizmos.DrawSphere(center, 0.2f);
                    }
                }
            }
        }

        internal class Polygon
        {
            /// <summary>
            /// Polygon 所有的顶点，体素坐标
            /// </summary>
            public Vector3[] Vertices;


            /// <summary>
            /// Polygon 的所有相邻的 Polygon
            /// </summary>
            public List<Polygon> Neighbors;
        }
    }
}
