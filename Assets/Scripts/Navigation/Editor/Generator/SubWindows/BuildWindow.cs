using Navigation.Display;
using Navigation.PreferenceData;
using Navigation.Utilities;
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Sirenix.OdinInspector;

namespace Navigation.Generator.Editor.SubWindows
{
    public class BuildWindow : IGeneratorSubWindow
    {
        public event Action OnStartBuild;

        public string Title => "Build";

        private Agents _agents;
        private BuildInfo _buildInfo;
        private BuildInfo _buildInfo_default;
        private SettingData _settingData;

        // 是否初始化过，记录这个窗口是否被打开过，被打开过才要触发保存的逻辑
        private bool _initialized = false;

        [SerializeField]
        [ListDrawerSettings(DraggableItems = false, Expanded = true, ShowIndexLabels = false, ShowItemCount = false, IsReadOnly = true)]
        [LabelText("Agents Waiting Build")]
        [DisplayAsString]
        private string[] _names;

        [SerializeField]
        [Space(10)]
        [BoxGroup("Build Preference")]
        [HideLabel]
        private BuildPreference _buildPreference;

        [HorizontalGroup("Buttons", marginLeft: 5, marginRight: 5)]
        [Button(SdfIconType.ArrowCounterclockwise, "Set Default Value", ButtonHeight = 22)]
        private void SetDefaultValue()
        {
            _buildPreference.CellSize = _buildInfo_default.CellSize;
            _buildPreference.CellHeight = _buildInfo_default.CellHeight;
            _buildPreference.TileSize = _buildInfo_default.TileSize;
            _buildPreference.BlurDistanceThreshold = _buildInfo_default.BlurDistanceThreshold;
            _buildPreference.UseOnlyNullSpans = _buildInfo_default.UseOnlyNullSpans;
            _buildPreference.MinRegionSize = _buildInfo_default.MinRegionSize;
            _buildPreference.MergeRegionSize = _buildInfo_default.MergeRegionSize;
            _buildPreference.DeviationThreshold = _buildInfo_default.DeviationThreshold;
            _buildPreference.MaxEdgeLength = _buildInfo_default.MaxEdgeLength;
            _buildPreference.MaxEdgeError = _buildInfo_default.MaxEdgeError;
            _buildPreference.VerticesPerPoly = _buildInfo_default.VerticesPerPoly;
            _buildPreference.SampleDistance = _buildInfo_default.SampleDistance;
            _buildPreference.MaxSampleError = _buildInfo_default.MaxSampleError;
            SaveBuildInfoChanges();
        }

        [HorizontalGroup("Buttons", marginRight: 5)]
        [Button(SdfIconType.Play, "Build", ButtonHeight = 22)]
        private void Build()
        {
            StringBuilder sb = new StringBuilder();
            foreach (var name in _names)
                sb.AppendLine(name);

            if (EditorUtility.DisplayDialog("Confirm", "We will build NavMesh for following Agents:\n" + sb.ToString(), "Yes", "Cancel"))
            {
                GizmosDrawer.Instance.Enabled = false; // Build前强制关停 Gizmos
                OnStartBuild?.Invoke(); // Build 开始前，由 GeneratorWindow 触发各个数据的保存工作

                NavMeshGenerator.Instance.Build();
            }
        }

        public BuildWindow(Agents agents, BuildInfo buildInfo, BuildInfo defaultBuildInfo)
        {
            _agents = agents;
            _buildInfo = buildInfo;
            _buildInfo_default = defaultBuildInfo;

            _names = _agents.AgentList.Select(i => i.Name).ToArray<string>();
            _settingData = AssetDatabase.LoadAssetAtPath<SettingData>(Path.SettingDataAssetPath);
        }

        [OnInspectorInit]
        private void OnInspectorInit()
        {
            _buildPreference.CellSize = _buildInfo.CellSize;
            _buildPreference.CellHeight = _buildInfo.CellHeight;
            _buildPreference.TileSize = _buildInfo.TileSize;
            _buildPreference.BlurDistanceThreshold = _buildInfo.BlurDistanceThreshold;
            _buildPreference.UseOnlyNullSpans = _buildInfo.UseOnlyNullSpans;
            _buildPreference.MinRegionSize = _buildInfo.MinRegionSize;
            _buildPreference.MergeRegionSize = _buildInfo.MergeRegionSize;
            _buildPreference.DeviationThreshold = _buildInfo.DeviationThreshold;
            _buildPreference.MaxEdgeLength = _buildInfo.MaxEdgeLength;
            _buildPreference.MaxEdgeError = _buildInfo.MaxEdgeError;
            _buildPreference.VerticesPerPoly = _buildInfo.VerticesPerPoly;
            _buildPreference.SampleDistance = _buildInfo.SampleDistance;
            _buildPreference.MaxSampleError = _buildInfo.MaxSampleError;

            _names = _agents.AgentList.Select(i => i.Name).ToArray<string>();
            _initialized = true;
        }

        [OnInspectorGUI]
        private void OnInspectorGUI()
        {
            var e = Event.current;
            if (e.isKey)
            {
                if (e.control && e.keyCode == KeyCode.S)
                {
                    SaveBuildInfoChanges();
                }
            }
        }

        [OnInspectorGUI]
        private void DrawSettingData()
        {
            if (_settingData == null)
                return;

            using (var scope = new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Logger settings", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Logger Level", GUILayout.Width(100));
                _settingData.Level = (LogLevel)EditorGUILayout.EnumPopup(_settingData.Level);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Use Unity Debug", GUILayout.Width(100));
                _settingData.UseUnityDebug = EditorGUILayout.Toggle(_settingData.UseUnityDebug);
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Show Date Time", GUILayout.Width(100));
                _settingData.ShowDateTime = EditorGUILayout.Toggle(_settingData.ShowDateTime);
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Show File Info", GUILayout.Width(90));
                _settingData.ShowFileInfo = EditorGUILayout.Toggle(_settingData.ShowFileInfo);
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Show Level", GUILayout.Width(80));
                _settingData.ShowLevel = EditorGUILayout.Toggle(_settingData.ShowLevel);
                EditorGUILayout.EndHorizontal();
            }

            using (var scope = new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Output Data", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Voxels", GUILayout.Width(45));
                _settingData.SaveSolidHeightField = EditorGUILayout.Toggle(_settingData.SaveSolidHeightField);
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Regions", GUILayout.Width(50));
                _settingData.SaveCompactHeightField = EditorGUILayout.Toggle(_settingData.SaveCompactHeightField);
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Contours", GUILayout.Width(60));
                _settingData.SaveContours = EditorGUILayout.Toggle(_settingData.SaveContours);
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("NavMesh", GUILayout.Width(60));
                GUI.enabled = false;
                _settingData.SaveNavMeshData = EditorGUILayout.Toggle(_settingData.SaveNavMeshData);
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
            }
        }

        public void SaveBuildInfoChanges()
        {
            if (!_initialized)
                return;

            _buildInfo.CellSize = _buildPreference.CellSize;
            _buildInfo.CellHeight = _buildPreference.CellHeight;
            _buildInfo.TileSize = _buildPreference.TileSize;
            _buildInfo.BlurDistanceThreshold = _buildPreference.BlurDistanceThreshold;
            _buildInfo.UseOnlyNullSpans = _buildPreference.UseOnlyNullSpans;
            _buildInfo.MinRegionSize = _buildPreference.MinRegionSize;
            _buildInfo.MergeRegionSize = _buildPreference.MergeRegionSize;
            _buildInfo.DeviationThreshold = _buildPreference.DeviationThreshold;
            _buildInfo.MaxEdgeLength = _buildPreference.MaxEdgeLength;
            _buildInfo.MaxEdgeError = _buildPreference.MaxEdgeError;
            _buildInfo.VerticesPerPoly = _buildPreference.VerticesPerPoly;
            _buildInfo.SampleDistance = _buildPreference.SampleDistance;
            _buildInfo.MaxSampleError = _buildPreference.MaxSampleError;

            EditorUtility.SetDirty(_buildInfo);
        }

        public void SaveSettingDataChanges()
        {
            if (_settingData == null)
                return;

            EditorUtility.SetDirty(_settingData);
        }

        [Serializable]
        internal struct BuildPreference
        {

            public float CellSize;
            public float CellHeight;
            public int TileSize;
            public int BlurDistanceThreshold;
            public bool UseOnlyNullSpans;
            public int MinRegionSize;
            public int MergeRegionSize;
            public float DeviationThreshold;
            [MinValue(5)]
            public int MaxEdgeLength;
            public int MaxEdgeError;
            [Range(3, 10)]
            public int VerticesPerPoly;
            public int SampleDistance;
            public int MaxSampleError;
        }
    }
}