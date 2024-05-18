using Navigation.Flags;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Navigation.PipelineData
{
    public class PolyMeshField : IPipelineData
    {
        public Tile CurrentTile { get; set; }
        public int[] Vertices { get; set; }
        public int[] Polygons { get; set; }
        public int[] RegionIndices { get; set; }
        public AreaMask[] AreaMasks { get; set; }

        public static PolyMeshField LoadFromJson(string path)
        {
            if (!System.IO.File.Exists(path)) return null;

            var json = System.IO.File.ReadAllText(path);
            var pmf = JsonConvert.DeserializeObject<PolyMeshField>(json);

            return pmf;
        }

        public void PersistData(string path)
        {
            var fileName = "tile" + CurrentTile.Id + ".json";
            var filePath = System.IO.Path.Combine(path, fileName);

            var json = JsonConvert.SerializeObject(this);
            System.IO.File.WriteAllText(filePath, json);
        }

        public void Initialize(Tile tile)
        {
            CurrentTile = tile;
        }
    }
}