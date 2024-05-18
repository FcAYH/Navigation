using Navigation.PreferenceData;
using System.Collections.Generic;
using System.Reflection;


namespace Navigation.PipelineData
{
    public class NavMeshPreference
    {
        // Agent 参数
        public string AgentName;
        public float AgentHeight;
        public float AgentRadius;
        public float AgentMaxSlope;
        public float AgentMaxStepHeight;

        // Build 参数
        public float CellSize;
        public float CellHeight;
        public int TileSize;
        public int BlurDistanceThreshold; // DistanceField
        public bool UseOnlyNullSpans; // Regions 暂时用不到
        public int MinRegionSize; // Regions
        public int MergeRegionSize; // Regions
        public float DeviationThreshold; // Contours
        public int MaxEdgeLength; // Contours
        public int MaxEdgeError;
        public int VerticesPerPoly; // PolyMesh
        public int SampleDistance;  // TriangleMesh
        public int MaxSampleError; // TriangleMesh

        public void Load(Agent agent)
        {
            AgentName = agent.Name;
            AgentHeight = agent.Height;
            AgentRadius = agent.Radius;
            AgentMaxSlope = agent.MaxSlope;
            AgentMaxStepHeight = agent.MaxStepHeight;
        }

        public void Load(BuildInfo buildInfo)
        {
            FieldInfo[] fields = typeof(BuildInfo).GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var field in fields)
            {
                FieldInfo fieldInfo = typeof(NavMeshPreference).GetField(field.Name);
                if (fieldInfo != null)
                {
                    fieldInfo.SetValue(this, field.GetValue(buildInfo));
                }
            }
        }

        public void Load(Agent agent, BuildInfo buildInfo)
        {
            AgentName = agent.Name;
            AgentHeight = agent.Height;
            AgentRadius = agent.Radius;
            AgentMaxSlope = agent.MaxSlope;
            AgentMaxStepHeight = agent.MaxStepHeight;

            FieldInfo[] fields = typeof(BuildInfo).GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var field in fields)
            {
                FieldInfo fieldInfo = typeof(NavMeshPreference).GetField(field.Name);
                if (fieldInfo != null)
                {
                    fieldInfo.SetValue(this, field.GetValue(buildInfo));
                }
            }
        }
    }
}
// TODO: BuildInfo一旦需要新增内容，连带着需要修改好几处代码，容易产生疏漏，需要优化
// 1. NavMeshPreference 目前已经用反射完成了该部分内容
//    但是相关信息目前被定义到了三个地方，本文件，BuildInfo，BuildWindow中的Preference，这里还需要优化