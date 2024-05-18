using Navigation.Components;
using Navigation.Display;
using Navigation.Pipeline;
using Navigation.PipelineData;
using Navigation.PreferenceData;
using Navigation.Utilities;
using RTS.Utilities.Extensions;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using Path = System.IO.Path;
using Logger = Navigation.Utilities.Logger;

namespace Navigation.Generator.Editor
{
    public class NavMeshGenerator : Singleton<NavMeshGenerator>
    {
        public PipelineProgress Progress { get; private set; }
        public DataSet Data => _data;

        private DataSet _data;
        private TileSet _tileSet;

        // INavMeshPipeline 本来是想做成用户可自定义处理程序，仅需继承这个接口即可
        // 但感觉有些过度设计了，其实没必要
        private SolidHeightFieldBuilder _solidHeightFieldBuilder;
        private CompactHeightFieldBuilder _compactHeightFieldBuilder;
        private RegionsBuilder _regionsBuilder;
        private ContoursBuilder _contoursBuilder;
        private PolyMeshFieldBuilder _polyMeshFieldBuilder;
        private TriangleMeshBuilder _triangleMeshBuilder;

        private Agents _agents;
        private Areas _areas;
        private BuildInfo _buildInfo;
        private SettingData _settingData;


        public NavMeshGenerator()
        {
            _solidHeightFieldBuilder = new SolidHeightFieldBuilder();
            _compactHeightFieldBuilder = new CompactHeightFieldBuilder();
            _regionsBuilder = new RegionsBuilder();
            _contoursBuilder = new ContoursBuilder();
            _polyMeshFieldBuilder = new PolyMeshFieldBuilder();
            _triangleMeshBuilder = new TriangleMeshBuilder();
            _tileSet = new TileSet();

            _agents = AssetDatabase.LoadAssetAtPath<Agents>(Utilities.Path.AgentsAssetPath);
            _areas = AssetDatabase.LoadAssetAtPath<Areas>(Utilities.Path.AreasAssetPath);
            _buildInfo = AssetDatabase.LoadAssetAtPath<BuildInfo>(Utilities.Path.BuildInfoAssetPath);
            _settingData = AssetDatabase.LoadAssetAtPath<SettingData>(Utilities.Path.SettingDataAssetPath);
        }

        public void Build()
        {
            EditorUtility.DisplayProgressBar("Generating", "Preparing", 0.2f);
            Progress = PipelineProgress.Initialize;

            // Logger 初始化
            string logPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs/Navigation/Generator");
            string logName = "generator";
            var logger = new Logger(logPath, logName);
            logger.ShowFileInfo = false;
            logger.Info("Start Build NavMesh!!!");

            bool isBuildSuccessful = true;
            try
            {
                long startTime = System.DateTime.Now.Ticks;

                logger.Info("Preparing working directory...");

                // 在当前Scene目录下创建同名目录
                var activeScene = SceneManager.GetActiveScene();
                string scenePath = Path.Combine(Application.dataPath,
                                        activeScene.path.Substring(7));
                FileInfo sceneFile = new FileInfo(scenePath);
                string dataFolderPath = Path.Combine(sceneFile.DirectoryName, activeScene.name + "_Navigation");

                if (Directory.Exists(dataFolderPath))
                    Directory.Delete(dataFolderPath, true);
                Directory.CreateDirectory(dataFolderPath);

                logger.Info("Data will be saved in " + dataFolderPath);

                EditorUtility.DisplayProgressBar("Generating - Data preprocessing", "Boxing Build Info", 0.1f);
                _data = new DataSet(dataFolderPath, logger);
                _data.NavMeshPreference.Load(_buildInfo);

                Vector3 min = new Vector3(int.MaxValue, int.MaxValue, int.MaxValue);
                Vector3 max = new Vector3(int.MinValue, int.MinValue, int.MinValue);

                // 地图分块
                EditorUtility.DisplayProgressBar("Generating - Data preprocessing", "Slicing Scene", 0.1f);
                logger.Info("Start Slicing Scene.");

                StaticObstacle[] obstacles = Transform.FindObjectsOfType<StaticObstacle>();
                foreach (var ob in obstacles)
                {
                    MeshRenderer mr = ob.GetComponent<MeshRenderer>();
                    if (mr != null)
                    {
                        min = min.ComponentMin(mr.bounds.min);
                        max = max.ComponentMax(mr.bounds.max);
                    }
                }

                min.y -= 10f;
                max.y += 10f;

                float tileSize = _data.NavMeshPreference.TileSize;

                int tileNumX = Mathf.CeilToInt((max.x - min.x) / tileSize);
                int tileNumZ = Mathf.CeilToInt((max.z - min.z) / tileSize);
                int tileCount = Mathf.CeilToInt(tileNumX * tileNumZ);

                _tileSet.Tiles = new Tile[tileCount];
                int count = 0;
                for (float x = min.x; x < max.x; x += tileSize)
                {
                    for (float z = min.z; z < max.z; z += tileSize)
                    {
                        _tileSet.Tiles[count] = new Tile
                                                (
                                                    count,
                                                    new Vector3(x, min.y, z),
                                                    new Vector3(x + tileSize, max.y, z + tileSize)
                                                );
                        count++;
                    }
                }

                logger.Info("Finished Slicing Scene into tiles");

                // 为每个Agent生成NavMesh
                foreach (var agent in _agents.AgentList)
                {
                    long agentStart = System.DateTime.Now.Ticks;
                    logger.Info($"Start Generating NavMesh for agent {agent.Name}");

                    // 创建数据目录
                    var agentNavMeshDataFolderPath = Path.Combine(dataFolderPath, agent.Name);
                    if (Directory.Exists(agentNavMeshDataFolderPath))
                        Directory.Delete(agentNavMeshDataFolderPath, true);
                    Directory.CreateDirectory(agentNavMeshDataFolderPath);

                    // 收集数据
                    EditorUtility.DisplayProgressBar($"Generating(agent: {agent.Name}) - Data preprocessing", "Boxing Agent Preference", 0.3f);
                    _data.NavMeshPreference.Load(agent);

                    for (int i = 0; i < _tileSet.Tiles.Length; i++)
                    {
                        var tile = _tileSet.Tiles[i];
                        logger.Info($"Agent({agent.Name}) - tile {i} start generating NavMesh");
                        logger.Info($"Tile {i}: ({tile.Min.x}, {tile.Min.y}, {tile.Min.z}) - ({tile.Max.x}, {tile.Max.y}, {tile.Max.z})");
                        _data.ClearRuntimeData();
                        _data.CurrentTile = tile;

                        var start = System.DateTime.Now.Ticks;
                        Progress = PipelineProgress.SolidHeightField;
                        EditorUtility.DisplayProgressBar($"Generating(agent: {agent.Name}) - Pipeline(tile: {i})", "Generate Solid Height Field", 0.4f);
                        _solidHeightFieldBuilder.Process(_data);
                        var end = System.DateTime.Now.Ticks;
                        logger.Info($"Agent({agent.Name}) - tile {i} generate solid height field cost: {(end - start) / 10000} ms");
                        if (_settingData.SaveSolidHeightField)
                        {
                            _data.PersistData(DataSet.DataType.SolidHeightField);
                        }

                        start = System.DateTime.Now.Ticks;
                        Progress = PipelineProgress.CompactHeightField;
                        EditorUtility.DisplayProgressBar($"Generating(agent: {agent.Name}) - Pipeline(tile: {i})", "Generate Compact Height Field", 0.5f);
                        _compactHeightFieldBuilder.Process(_data);
                        end = System.DateTime.Now.Ticks;
                        logger.Info($"Agent({agent.Name}) - tile {i} generate compact height field cost: {(end - start) / 10000} ms");

                        start = System.DateTime.Now.Ticks;
                        Progress = PipelineProgress.Regions;
                        EditorUtility.DisplayProgressBar($"Generating(agent: {agent.Name}) - Pipeline(tile: {i})", "Generate Regions", 0.6f);
                        _regionsBuilder.Process(_data);
                        end = System.DateTime.Now.Ticks;
                        logger.Info($"Agent({agent.Name}) - tile {i} generate regions cost: {(end - start) / 10000} ms");
                        if (_settingData.SaveCompactHeightField)
                        {
                            _data.PersistData(DataSet.DataType.CompactHeightField);
                        }

                        start = System.DateTime.Now.Ticks;
                        Progress = PipelineProgress.Contours;
                        EditorUtility.DisplayProgressBar($"Generating(agent: {agent.Name}) - Pipeline(tile: {i})", "Generate Contours", 0.7f);
                        _contoursBuilder.Process(_data);
                        end = System.DateTime.Now.Ticks;
                        logger.Info($"Agent({agent.Name}) - tile {i} generate contours cost: {(end - start) / 10000} ms");
                        if (_settingData.SaveContours)
                        {
                            _data.PersistData(DataSet.DataType.ContourSet);
                        }

                        start = System.DateTime.Now.Ticks;
                        Progress = PipelineProgress.PolyMesh;
                        EditorUtility.DisplayProgressBar($"Generating(agent: {agent.Name}) - Pipeline(tile: {i})", "Generate PolyMesh", 0.8f);
                        _polyMeshFieldBuilder.Process(_data);
                        end = System.DateTime.Now.Ticks;
                        logger.Info($"Agent({agent.Name}) - tile {i} generate poly mesh cost: {(end - start) / 10000} ms");
                        _data.PersistData(DataSet.DataType.PolyMeshField);

                        start = System.DateTime.Now.Ticks;
                        Progress = PipelineProgress.TriangleMesh;
                        EditorUtility.DisplayProgressBar("Generating - Pipeline", "Generate High level of detail", 0.9f);
                        _triangleMeshBuilder.Process(_data);
                        end = System.DateTime.Now.Ticks;
                        logger.Info($"Agent({agent.Name}) - tile {i} generate triangle mesh cost: {(end - start) / 10000} ms");
                        _data.PersistData(DataSet.DataType.TriangleMesh);

                    }

                    Progress = PipelineProgress.IntegrateResults;
                    // TODO: IntegrateResults
                    EditorUtility.DisplayProgressBar("Generating", "Integrate Results", 0.95f);

                    // TODO: 保存完整数据

                    logger.Info($"Agent({agent.Name}) total time cost: " + ((System.DateTime.Now.Ticks - agentStart) / 10000) + " ms");
                }

                logger.Info("NavMesh generation finished! Total time cost: " + ((System.DateTime.Now.Ticks - startTime) / 10000) + " ms");
                logger.Close();
                GizmosDrawer.Instance.Enabled = true;
            }
            catch (Exception e)
            {
                isBuildSuccessful = false;
                EditorUtility.ClearProgressBar();

                string message = e.Message + "\n" + e.Source + "\n" + e.StackTrace;
                logger.Info("Build NavMesh Failed!!!");
                logger.Fatal(message);
                logger.Close();
                if (message.Length > 500)
                    message = message.Substring(0, 500) + "... \n";
                if (EditorUtility.DisplayDialog("Exception! >_<", message + "详情见log文件", "打开log文件夹", "取消"))
                {
                    System.Diagnostics.Process.Start(logger.LogFileDirectory);
                }
            }

            EditorUtility.ClearProgressBar();
            if (isBuildSuccessful)
            {
                EditorUtility.DisplayDialog("Success! ^_^", "Build NavMesh Finished!", "Yes");
            }
        }
    }

    public enum PipelineProgress
    {
        Initialize,
        SolidHeightField,
        CompactHeightField,
        Regions,
        Contours,
        PolyMesh,
        TriangleMesh,
        IntegrateResults
    }
}
