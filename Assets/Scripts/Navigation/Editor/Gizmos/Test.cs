using Navigation.PipelineData;
using Navigation.Finder.Editor;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;

namespace Navigation.Display
{
    public class MyEditorWindow
    {

        [MenuItem("Navigation/Test", priority = 2)]
        public static void ToggleButton()
        {
            PathFinder.Instance.FindPath();
        }

        // [MenuItem("Navigation/Show Gizmos")]
        // public static void Test()
        // {
        //     var data = ContourSet.LoadFromJson(@"C:\Users\F_CIL\Desktop\tile0.json");

        //     Debug.Log(data.Count);
        //     Debug.Log(data[0].RegionId);
        //     Debug.Log(data[0].RawVerticesCount);
        // }

        internal class Data
        {
            public int Value { get; set; }
            public Data(int value)
            {
                Value = value;
            }
        }

    }
}