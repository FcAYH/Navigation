using Newtonsoft.Json;

namespace Navigation.PipelineData
{
    public class TriangleMesh : IPipelineData
    {
        public Tile CurrentTile { get; set; }
        public float[] Vertices { get; set; }
        public int[] Indices { get; set; }
        public int[] Regions { get; set; }


        public void Initialize(Tile tile)
        {
            CurrentTile = tile;
        }

        public static TriangleMesh LoadFromJson(string path)
        {
            if (!System.IO.File.Exists(path)) return null;

            var json = System.IO.File.ReadAllText(path);
            var tm = JsonConvert.DeserializeObject<TriangleMesh>(json);

            return tm;
        }

        public void PersistData(string path)
        {
            var fileName = "tile" + CurrentTile.Id + ".json";
            var filePath = System.IO.Path.Combine(path, fileName);

            var json = JsonConvert.SerializeObject(this);
            System.IO.File.WriteAllText(filePath, json);
        }
    }
}