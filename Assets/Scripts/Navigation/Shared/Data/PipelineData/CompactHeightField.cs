using Navigation.Flags;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Navigation.PipelineData
{

    public class CompactHeightField : IPipelineData
    {
        public Tile CurrentTile { get; private set; }
        public int VoxelNumX { get; private set; }
        public int VoxelNumY { get; private set; }
        public int VoxelNumZ { get; private set; }
        public int VoxelNum { get; private set; }

        public CompactHeightCell[] Cells { get; set; }
        public CompactHeightSpan[] Spans { get; set; }

        public int Count => Spans.Length;

        public void Initialize(Tile currentTile, int voxelNumX, int voxelNumY, int voxelNumZ, int voxelNum)
        {
            CurrentTile = currentTile;
            VoxelNumX = voxelNumX;
            VoxelNumY = voxelNumY;
            VoxelNumZ = voxelNumZ;
            VoxelNum = voxelNum;

            Cells = new CompactHeightCell[VoxelNumX * VoxelNumZ];
            for (int i = 0; i < Cells.Length; i++)
            {
                Cells[i] = new CompactHeightCell();
            }

            // 确保即便没有span，Spans也不会是null，因为后面很多处用到Spans.Length
            Spans = new CompactHeightSpan[0];
        }

        public void PersistData(string path)
        {
            var fileName = "tile" + CurrentTile.Id + ".json";
            var filePath = System.IO.Path.Combine(path, fileName);

            var json = JsonConvert.SerializeObject(this);
            System.IO.File.WriteAllText(filePath, json);
        }

        public static CompactHeightField LoadFromJson(string path)
        {
            if (!System.IO.File.Exists(path)) return null;

            var json = System.IO.File.ReadAllText(path);
            var data = JsonConvert.DeserializeObject<CompactHeightField_JsonObject>(json);

            var chf = new CompactHeightField();
            chf.TransformFromJsonObject(data);
            return chf;
        }

        private void TransformFromJsonObject(CompactHeightField_JsonObject jsonObj)
        {
            CurrentTile = jsonObj.CurrentTile;
            VoxelNumX = jsonObj.VoxelNumX;
            VoxelNumY = jsonObj.VoxelNumY;
            VoxelNumZ = jsonObj.VoxelNumZ;
            VoxelNum = jsonObj.VoxelNum;
            Cells = jsonObj.Cells;
            Spans = jsonObj.Spans;
        }

        protected class CompactHeightField_JsonObject
        {
            public Tile CurrentTile;
            public int VoxelNumX;
            public int VoxelNumY;
            public int VoxelNumZ;
            public int VoxelNum;
            public CompactHeightCell[] Cells;
            public CompactHeightSpan[] Spans;
        }
    }

    public class CompactHeightSpan
    {
        public int Floor { get; set; }
        public int Ceiling { get; set; }
        public int Height => Ceiling - Floor + 1;
        public int RegionId { get; set; } = 0;

        public AreaMask Area { get; set; }
        public uint Next { get; set; }
        public uint[] Neighbors { get; set; }

        public int DistanceToBorder { get; set; }
        public int DistanceToCore { get; set; }

        public uint Left => Neighbors[0];
        public uint Forward => Neighbors[1];
        public uint Right => Neighbors[2];
        public uint Back => Neighbors[3];

        public CompactHeightSpan(int floor, int ceiling, uint next, AreaMask area)
        {
            Floor = floor;
            Ceiling = ceiling;
            Next = next;
            Area = area;

            Neighbors = new uint[4] { uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue };
        }
    }

    public class CompactHeightCell
    {
        public uint FirstSpan { get; set; } = uint.MaxValue;
        public uint Count { get; set; }
    }
}
