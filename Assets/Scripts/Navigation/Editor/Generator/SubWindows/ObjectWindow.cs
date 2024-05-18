using Navigation.Flags;
using Navigation.Components;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Sirenix.OdinInspector;
using System.Reflection;

namespace Navigation.Generator.Editor.SubWindows
{
    public class ObjectWindow : IGeneratorSubWindow
    {
        public string Title => "Object";

        private GameObject _currentSelectGO;

        private bool _isObstacle = false;
        private AreaMask _areaMask = AreaMask.NotWalkable;

        private object _currentHierarchy;
        private MethodInfo _setSearchFilterMethod;
        private object[] _methodParameters;

        public ObjectWindow()
        {
            _methodParameters = new object[2];
            _methodParameters[1] = SearchableEditorWindow.SearchModeHierarchyWindow.All;

            var assembly = Assembly.GetAssembly(typeof(EditorWindow));
            var hierarchyType = assembly.GetType("UnityEditor.SceneHierarchyWindow");
            var leastHierarchyProperty = hierarchyType.GetProperty("lastInteractedHierarchyWindow");
            EditorWindow sceneHierarchyWindow = (EditorWindow)leastHierarchyProperty.GetValue(null);

            if (sceneHierarchyWindow == null)
            {
                EditorUtility.DisplayDialog("Error >_<!", "Please ensure that hierarchy window is open, you can focus hierarchy window and then open this window!", "Yes");
                return;
            }

            PropertyInfo sceneHierarchyProperty = sceneHierarchyWindow.GetType().GetProperty("sceneHierarchy");
            _currentHierarchy = sceneHierarchyProperty.GetValue(sceneHierarchyWindow);
            _setSearchFilterMethod = _currentHierarchy.GetType().GetMethod("SetSearchFilter");
        }


        [HorizontalGroup("Buttons", Title = "Filter", MarginLeft = 5)]
        [Button(SdfIconType.Box, "All")]
        private void Filter_All()
        {
            _methodParameters[0] = "";
            _setSearchFilterMethod?.Invoke(_currentHierarchy, _methodParameters);
        }


        [HorizontalGroup("Buttons", MarginLeft = 5)]
        [Button(SdfIconType.Grid3x3, "MeshRenderer")]
        private void Filter_MeshRenderer()
        {
            _methodParameters[0] = "t:MeshRenderer";
            _setSearchFilterMethod?.Invoke(_currentHierarchy, _methodParameters);
        }


        [HorizontalGroup("Buttons", MarginLeft = 5)]
        [Button(SdfIconType.Stack, "Terrains")]
        private void Filter_Terrains()
        {
            _methodParameters[0] = "t:Terrains";
            _setSearchFilterMethod?.Invoke(_currentHierarchy, _methodParameters);
        }

        [HorizontalGroup("Buttons", MarginLeft = 5, MarginRight = 5)]
        [Button(SdfIconType.Alt, "StaticObstacle")]
        private void Filter_StaticObstacle()
        {
            _methodParameters[0] = "t:StaticObstacle";
            _setSearchFilterMethod?.Invoke(_currentHierarchy, _methodParameters);
        }

        [OnInspectorGUI]
        private void OnInspectorGUI()
        {
            GUILayout.Space(10);
            using (var scope = new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Object", EditorStyles.boldLabel);

                _currentSelectGO = Selection.activeGameObject;

                if (_currentSelectGO != null)
                {
                    GUI.enabled = false;
                    EditorGUILayout.ObjectField(_currentSelectGO.name, _currentSelectGO, typeof(GameObject), true);
                    GUI.enabled = true;

                    // 确保 GameObject 有 MeshRenderer， 则可编辑定为 Obstacle，
                    var mr = _currentSelectGO.GetComponent<MeshRenderer>();
                    if (mr != null)
                    {
                        var so = _currentSelectGO.GetComponent<StaticObstacle>();
                        if (so == null)
                        {
                            _isObstacle = EditorGUILayout.Toggle("Is Obstacle", _isObstacle);
                            if (_isObstacle == true)
                            {
                                _areaMask = (AreaMask)EditorGUILayout.EnumFlagsField("Area", _areaMask);

                                // 对于一个为添加 Static Obstacle 的 GameObject，
                                // 选择 isObstacle 后，会为其添加该组件
                                var newSO = _currentSelectGO.AddComponent<StaticObstacle>();
                                newSO.Area = _areaMask;
                            }
                        }
                        else
                        {
                            _isObstacle = true;
                            _isObstacle = EditorGUILayout.Toggle("Is Obstacle", _isObstacle);
                            _areaMask = (AreaMask)EditorGUILayout.EnumFlagsField("Area", so.Area);
                            so.Area = _areaMask;
                            if (_isObstacle == false)
                            {
                                GameObject.DestroyImmediate(so);
                            }
                        }

                        if ((((int)_areaMask) & 1) != 0 && (((int)_areaMask) & ~1) != 0)
                        {
                            EditorGUILayout.HelpBox("An area can't be 'NotWalkable' and others at the same time!", MessageType.Error);
                        }
                    }
                    else
                    {
                        EditorGUILayout.LabelField("Selected GameObject doesn't have a MeshRenderer!");
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("Please Select a GameObject from Hierarchy Window!");
                }
            }
        }
    }
}
