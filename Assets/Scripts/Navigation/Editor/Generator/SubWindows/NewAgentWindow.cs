using Navigation.PreferenceData;
using Navigation.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Sirenix.OdinInspector;

namespace Navigation.Generator.Editor.SubWindows
{
    public class NewAgentWindow : IGeneratorSubWindow
    {
        public string Title => "Agents/[Add to Create New]";

        [BoxGroup("Create New Agent")]
        [HideLabel]
        public Agent NewAgent;

        private Rect _windowSize => GeneratorWindow.CurrentWindow.position;
        public event Action<Agent> AddNewAgent;

        public NewAgentWindow()
        {
            NewAgent = new Agent();
            NewAgent.Name = "Human";
            NewAgent.Height = 2.0f;
            NewAgent.Radius = 0.4f;
            NewAgent.MaxSlope = 45;
            NewAgent.MaxStepHeight = 0.7f;
        }

        [Button(SdfIconType.BoxArrowInDown, "Create")]
        public void CreateNewAgent()
        {
            if (string.IsNullOrEmpty(NewAgent.Name))
            {
                EditorUtility.DisplayDialog("Error >_<!", "Agent name cannot be Null!", "Ok");
                return;
            }

            var _agentAsset = AssetDatabase.LoadAssetAtPath<Agents>(Path.AgentsAssetPath);

            // 不重名
            bool isNameDuplicate = false;
            foreach (var agent in _agentAsset.AgentList)
            {
                if (string.Equals(agent.Name, NewAgent.Name))
                {
                    isNameDuplicate = true;
                    break;
                }
            }

            if (isNameDuplicate)
            {
                EditorUtility.DisplayDialog("Error! >_<", "Agent Name cannot be Duplicated!", "Ok");
            }
            else
            {
                AddNewAgent?.Invoke(NewAgent);
            }
        }

        [OnInspectorGUI]
        private void OnInspectorGUI()
        {
            using (var scope = new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var texture2d = new Texture2D(1, 1);
                // Handles.DrawLine(startPos, endPos);

                EditorGUILayout.LabelField("Display:", EditorStyles.boldLabel);

                float leftUpperX = 20f;
                float leftUpperY = 180f;
                float width = _windowSize.width - 240;
                float height = _windowSize.height - 250;

                float areaWidth = width / 3;

                Rect rect = new Rect(leftUpperX, leftUpperY, 25, 5);
                EditorGUI.DrawRect(rect, ColorCard.LightPhthaleinBlue);
                rect = new Rect(leftUpperX + 30, leftUpperY - 8, 100, 20);
                EditorGUI.LabelField(rect, "Step Height");

                rect = new Rect(leftUpperX + areaWidth, leftUpperY, 25, 5);
                EditorGUI.DrawRect(rect, ColorCard.OrangeYellow);
                rect = new Rect(leftUpperX + areaWidth + 30, leftUpperY - 8, 50, 20);
                EditorGUI.LabelField(rect, "Agent");

                rect = new Rect(leftUpperX + 2 * areaWidth, leftUpperY, 25, 5);
                EditorGUI.DrawRect(rect, ColorCard.PeaGreen);
                rect = new Rect(leftUpperX + 2 * areaWidth + 30, leftUpperY - 8, 50, 20);
                EditorGUI.LabelField(rect, "Slope");

                EditorGUILayout.LabelField("", GUILayout.Height(height + 70));
                // draw step 
                var stepHeight = NewAgent.MaxStepHeight / 10f * height; // TODO: 10: 最大值，如果未来修改了最大值，这里也要改，所以最好把这种东西做成一个配置文件去读取
                var stepHorizontalStart = new Vector3(leftUpperX, leftUpperY + height - stepHeight, 0);
                var stepHorizontalEnd = new Vector3(leftUpperX + areaWidth, stepHorizontalStart.y, 0);
                Handles.DrawBezier(stepHorizontalStart, stepHorizontalEnd, stepHorizontalStart, stepHorizontalEnd, ColorCard.LightPhthaleinBlue, texture2d, 2);

                if (stepHeight > 0.0001f)
                {
                    var stepVerticalStart = stepHorizontalEnd;
                    var stepVerticalEnd = new Vector3(stepVerticalStart.x, leftUpperY + height, 0);
                    Handles.DrawBezier(stepVerticalStart, stepVerticalEnd, stepVerticalStart, stepVerticalEnd, ColorCard.LightPhthaleinBlue, texture2d, 2);
                }

                // draw agent
                var agentHeight = NewAgent.Height / 10f * height;
                var agentRadius = NewAgent.Radius / 22f * areaWidth; // 22: 为了不让边重合 
                var middleX = leftUpperX + areaWidth + areaWidth / 2;

                var agentHeadStart = new Vector3(middleX - agentRadius, leftUpperY + height - agentHeight, 0);
                var agentHeadEnd = new Vector3(middleX + agentRadius, agentHeadStart.y, 0);
                Handles.DrawBezier(agentHeadStart, agentHeadEnd, agentHeadStart, agentHeadEnd, ColorCard.OrangeYellow, texture2d, 2);

                var agentStartVerticalStart = new Vector3(agentHeadStart.x, leftUpperY + height, 0);
                var agentStartVerticalEnd = agentHeadStart;
                Handles.DrawBezier(agentStartVerticalStart, agentStartVerticalEnd, agentStartVerticalStart, agentStartVerticalEnd, ColorCard.OrangeYellow, texture2d, 2);

                var agentEndVerticalStart = new Vector3(agentHeadEnd.x, leftUpperY + height, 0);
                var agentEndVerticalEnd = agentHeadEnd;
                Handles.DrawBezier(agentEndVerticalStart, agentEndVerticalEnd, agentEndVerticalStart, agentEndVerticalEnd, ColorCard.OrangeYellow, texture2d, 2);



                // draw ground
                var groundStart = new Vector3(leftUpperX + areaWidth, leftUpperY + height, 0);
                var groundEnd = new Vector3(leftUpperX + areaWidth * 2, leftUpperY + height, 0);
                Handles.DrawBezier(groundStart, groundEnd, groundStart, groundEnd, Color.black, texture2d, 2);

                // draw slope
                var slopeStart = new Vector3(leftUpperX + areaWidth * 2, leftUpperY + height, 0);
                var rate = Mathf.Max(Mathf.Tan(NewAgent.MaxSlope / 180f * Mathf.PI), 0.0000001f);

                var slopeEnd = new Vector3();
                slopeEnd.x = leftUpperX + width;
                var yOffset = areaWidth * Mathf.Tan(NewAgent.MaxSlope / 180f * Mathf.PI);
                slopeEnd.y = leftUpperY + height - yOffset;

                if (slopeEnd.y < leftUpperY)
                {
                    slopeEnd.y = leftUpperY;
                    var xOffset = height / Mathf.Tan(NewAgent.MaxSlope / 180f * Mathf.PI);
                    slopeEnd.x = slopeStart.x + xOffset;
                }
                Handles.DrawBezier(slopeStart, slopeEnd, slopeStart, slopeEnd, ColorCard.PeaGreen, texture2d, 2);
            }
        }
    }
}