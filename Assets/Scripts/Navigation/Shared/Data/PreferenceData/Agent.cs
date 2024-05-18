using System;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Navigation.PreferenceData
{
    [Serializable]
    public struct Agent
    {
        public string Name;

        [PropertyRange(0.2f, 10f)]
        public float Height;

        [PropertyRange(0.1f, 10f)]
        public float Radius;

        [PropertyRange(0f, 60f)]
        public float MaxSlope;

        [PropertyRange(0f, 10f)]
        public float MaxStepHeight;
    }
}