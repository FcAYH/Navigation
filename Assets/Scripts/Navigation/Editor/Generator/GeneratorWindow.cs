using Navigation.PreferenceData;
using Navigation.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities;
using Sirenix.Utilities.Editor;

namespace Navigation.Generator.Editor.SubWindows
{
    public class GeneratorWindow : OdinMenuEditorWindow
    {
        private static GeneratorWindow _currentWindow;
        public static GeneratorWindow CurrentWindow
        {
            get
            {
                if (_currentWindow == null)
                    _currentWindow = GetWindow<GeneratorWindow>();

                _currentWindow.minSize = new Vector2(800, 600);

                return _currentWindow;
            }
        }

        private OverviewWindow _overviewWindow;
        private AreasWindow _areasWindow;
        private BuildWindow _buildWindow;
        private ObjectWindow _objectWindow;
        private NewAgentWindow _newAgentWindow;

        private List<AgentDetailWindow> _agentWindowList;
        private Agents _agents;
        private Areas _areas;
        private BuildInfo _buildInfo;
        private BuildInfo _buildInfo_default;



        [MenuItem("Navigation/Generator", priority = 0)]
        private static void OpenWindow()
        {
            CurrentWindow.Show();

            //window.position = GUIHelper.GetEditorWindowRect().AlignCenter(800, 600);
        }

        protected override void Initialize()
        {
            base.Initialize();
            _agents = AssetDatabase.LoadAssetAtPath<Agents>(Path.AgentsAssetPath);
            _areas = AssetDatabase.LoadAssetAtPath<Areas>(Path.AreasAssetPath);
            _buildInfo = AssetDatabase.LoadAssetAtPath<BuildInfo>(Path.BuildInfoAssetPath);
            _buildInfo_default = AssetDatabase.LoadAssetAtPath<BuildInfo>(Path.DefaultBuildInfoAssetPath);

            _overviewWindow = new OverviewWindow();
            _areasWindow = new AreasWindow(_areas);
            _buildWindow = new BuildWindow(_agents, _buildInfo, _buildInfo_default);
            _objectWindow = new ObjectWindow();
            _newAgentWindow = new NewAgentWindow();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _newAgentWindow.AddNewAgent += OnAddNewAgent;
            _buildWindow.OnStartBuild += OnStartBuild;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            _newAgentWindow.AddNewAgent -= OnAddNewAgent;
            _buildWindow.OnStartBuild += OnStartBuild;
        }

        protected override void OnDestroy()
        {
            // Save 
            SaveDataChanges();

            base.OnDestroy();
        }

        private void OnStartBuild()
        {
            SaveChanges();
        }

        private void SaveDataChanges()
        {
            SaveAgentChanges();
            _areasWindow.SaveAreasChanges();
            _buildWindow.SaveBuildInfoChanges();
            _buildWindow.SaveSettingDataChanges();

            AssetDatabase.SaveAssets();
        }

        protected override OdinMenuTree BuildMenuTree()
        {
            var tree = new OdinMenuTree();
            tree.Selection.SupportsMultiSelect = false;
            tree.Config.DrawSearchToolbar = true;

            tree.Add(_objectWindow.Title, _objectWindow);
            tree.Add(_buildWindow.Title, _buildWindow);
            tree.Add(_areasWindow.Title, _areasWindow);
            tree.Add(_overviewWindow.Title, _overviewWindow);

            tree.SortMenuItemsByName();

            var overviewWindowItem = tree.GetMenuItem(_overviewWindow.Title);
            _agentWindowList = new List<AgentDetailWindow>(_agents.AgentList.Count);
            foreach (var agent in _agents.AgentList)
            {
                var agentWindow = new AgentDetailWindow(agent, OnDeleteAgent);
                _agentWindowList.Add(agentWindow);
                var agentWindowItem = new OdinMenuItem(tree, agentWindow.Title, agentWindow);
                overviewWindowItem.ChildMenuItems.Insert(_agentWindowList.Count - 1, agentWindowItem);
            }

            tree.Add(_newAgentWindow.Title, _newAgentWindow);

            return tree;
        }

        private void OnAddNewAgent(Agent newAgent)
        {
            var tree = this.MenuTree;
            var agentsItem = tree.GetMenuItem(_overviewWindow.Title);
            var agentWindow = new AgentDetailWindow(newAgent, OnDeleteAgent);

            var newAgentItem = new OdinMenuItem(tree, agentWindow.Title, agentWindow);
            _agentWindowList.Add(agentWindow);

            agentsItem.ChildMenuItems.Insert(_agentWindowList.Count - 1, newAgentItem);
            SaveAgentChanges();
        }

        private bool OnDeleteAgent(string AgentName)
        {
            var tree = this.MenuTree;
            var agentsItem = tree.GetMenuItem(_overviewWindow.Title);
            if (agentsItem.ChildMenuItems.Count <= 2)
            {
                EditorUtility.DisplayDialog("Error! >_<", "You need Keep at least ONE Agent!", "Ok");
                return false;
            }
            if (!EditorUtility.DisplayDialog("Delete Current Agent? ",
                                                "This operation is not recoverable!",
                                                "Yes", "Cancel"))
                return false;

            int index = -1;
            for (int i = 0; i < _agentWindowList.Count; i++)
            {
                if (_agentWindowList[i].Title == AgentName)
                {
                    index = i;
                    break;
                }
            }

            _agentWindowList.RemoveAt(index);
            agentsItem.ChildMenuItems.RemoveAt(index);
            SaveAgentChanges();
            return true;
        }

        protected override void OnBeginDrawEditors()
        {

        }

        private void SaveAgentChanges()
        {
            // Agents
            List<Agent> agentList = new List<Agent>(_agentWindowList.Count);
            foreach (var window in _agentWindowList)
            {
                agentList.Add(window.CurrentAgent);
            }

            _agents.AgentList = agentList;
            EditorUtility.SetDirty(_agents);
        }
    }
}