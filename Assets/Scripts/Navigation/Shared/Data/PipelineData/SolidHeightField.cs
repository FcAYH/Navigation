using Navigation.Flags;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

namespace Navigation.PipelineData
{
    public class SolidHeightField : IPipelineData
    {
        public Tile CurrentTile { get; private set; }
        public int VoxelNumX { get; private set; }
        public int VoxelNumY { get; private set; }
        public int VoxelNumZ { get; private set; }
        public int VoxelNum { get; private set; }
        public float MinNormalY { get; private set; }
        public Dictionary<int, SolidHeightSpan> SpanDict { get; private set; }

        public void Initialize(Tile currentTile, int voxelNumX, int voxelNumY, int voxelNumZ, int voxelNum, float minNormalY)
        {
            CurrentTile = currentTile;
            VoxelNumX = voxelNumX;
            VoxelNumY = voxelNumY;
            VoxelNumZ = voxelNumZ;
            VoxelNum = voxelNum;
            MinNormalY = minNormalY;

            SpanDict = new Dictionary<int, SolidHeightSpan>(VoxelNum >> 2);
        }

        private void TransformFromJsonObject(SolidHeightField_JsonObject jsonObj)
        {
            CurrentTile = jsonObj.CurrentTile;
            VoxelNumX = jsonObj.VoxelNumX;
            VoxelNumY = jsonObj.VoxelNumY;
            VoxelNumZ = jsonObj.VoxelNumZ;
            VoxelNum = jsonObj.VoxelNum;
            MinNormalY = jsonObj.MinNormalY;
            SpanDict = jsonObj.SpanDict;
        }

        public void PersistData(string path)
        {
            var fileName = "tile" + CurrentTile.Id + ".json";
            var filePath = System.IO.Path.Combine(path, fileName);

            var json = JsonConvert.SerializeObject(this);
            System.IO.File.WriteAllText(filePath, json);
        }

        public static SolidHeightField LoadFromJson(string path)
        {
            if (!System.IO.File.Exists(path)) return null;

            var json = System.IO.File.ReadAllText(path);
            var data = JsonConvert.DeserializeObject<SolidHeightField_JsonObject>(json);

            var shf = new SolidHeightField();
            shf.TransformFromJsonObject(data);
            return shf;
        }

        protected class SolidHeightField_JsonObject
        {
            public Tile CurrentTile;
            public int VoxelNumX;
            public int VoxelNumY;
            public int VoxelNumZ;
            public int VoxelNum;
            public float MinNormalY;
            public Dictionary<int, SolidHeightSpan> SpanDict;
        }
    }

    public class SolidHeightSpan
    {
        public int Bottom { get; set; }
        public int Top { get; set; }
        public SolidHeightSpan Next { get; set; }
        public AreaMask Area { get; set; }

        public SolidHeightSpan(int bottom, int top, SolidHeightSpan next, AreaMask area)
        {
            Bottom = bottom;
            Top = top;
            Next = next;
            Area = area;
        }
    }
}