using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Navigation.PreferenceData
{
    [CreateAssetMenu(menuName = "Navigation/Generator/BuildInfo")]
    public class BuildInfo : ScriptableObject
    {
        [ReadOnly]
        public float CellSize = 0.2f;
        [ReadOnly]
        public float CellHeight = 0.3f;
        [ReadOnly]
        public int TileSize = 128;
        [ReadOnly]
        public int BlurDistanceThreshold = 2;
        [ReadOnly]
        public bool UseOnlyNullSpans = true;
        [ReadOnly]
        public int MinRegionSize = 8;
        [ReadOnly]
        public int MergeRegionSize = 20;
        [ReadOnly]
        public float DeviationThreshold = 1.5f;
        [ReadOnly]
        public int MaxEdgeLength = 12;
        [ReadOnly]
        public int MaxEdgeError = 12;
        [ReadOnly]
        public int VerticesPerPoly = 6;
        [ReadOnly]
        public int SampleDistance = 12;
        [ReadOnly]
        public int MaxSampleError = 12;
    }
}