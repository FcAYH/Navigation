using Navigation.PreferenceData;
using Navigation.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using Sirenix.OdinInspector;

namespace Navigation.Generator.Editor.SubWindows
{
    public class AreasWindow : IGeneratorSubWindow
    {
        public string Title => "Areas";

        private DisplayInfo[] _displayInfos;
        private Areas _areas;

        [Serializable]
        internal struct DisplayInfo
        {
            public Color TagColor;
            public string AreaIndex;
            public string AreaName;
            public int AreaPriority;
        }

        public AreasWindow(Areas areas)
        {
            _displayInfos = new DisplayInfo[areas.AreaSet.Length];

            Type colorCardType = typeof(ColorCard);
            FieldInfo[] fields = colorCardType.GetFields();


            for (int i = 0; i < areas.AreaSet.Length; i++)
            {
                _displayInfos[i].TagColor = (Color)fields[i].GetValue(null);
                _displayInfos[i].AreaIndex = "Area " + i;
                _displayInfos[i].AreaName = areas.AreaSet[i].Name;
                _displayInfos[i].AreaPriority = areas.AreaSet[i].Priority;
            }

            _areas = areas;
        }

        private Vector2 _scrollPosition;
        [OnInspectorGUI]
        private void OnInspectorGUI()
        {
            EditorGUILayout.Space(10);
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            EditorGUILayout.BeginVertical();
            for (int i = 0; i < _displayInfos.Length; i++)
            {
                var info = _displayInfos[i];
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.Space(20);
                EditorGUILayout.LabelField(info.AreaIndex, GUILayout.Width(50));
                if (i == 0 || i == 1)
                {
                    Rect rect = new Rect(10, 25 * i, 10, 20);
                    EditorGUI.DrawRect(rect, info.TagColor);
                    GUI.enabled = false;
                    EditorGUILayout.TextField(info.AreaName);
                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Priority", GUILayout.Width(50));
                    EditorGUILayout.IntField(info.AreaPriority);
                    GUI.enabled = true;
                }
                else
                {
                    Rect rect = new Rect(10, 25 * i, 10, 20);
                    EditorGUI.DrawRect(rect, info.TagColor);
                    info.AreaName = EditorGUILayout.TextField(info.AreaName);
                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Priority", GUILayout.Width(50));
                    info.AreaPriority = EditorGUILayout.IntField(info.AreaPriority);
                }
                EditorGUILayout.Space(10);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(5);
                _displayInfos[i] = info;
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();


            Event e = Event.current;
            if (e.isKey)
            {
                if (e.keyCode == KeyCode.S && e.control)
                    SaveAreasChanges();
            }
        }

        public void SaveAreasChanges()
        {
            for (int i = 0; i < _displayInfos.Length; i++)
            {
                _areas.AreaSet[i].Name = _displayInfos[i].AreaName;
                _areas.AreaSet[i].Priority = _displayInfos[i].AreaPriority;
            }

            // 生成AreaMask.cs文件
            _areas.GenerateAreaMaskFile();
            EditorUtility.SetDirty(_areas);
            AssetDatabase.Refresh();
        }
    }
}
