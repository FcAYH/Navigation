using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Navigation.PipelineData
{
    public class ContourSet : IPipelineData
    {
        public Tile CurrentTile { get; private set; }
        private List<Contour> _contourList;

        public int Count => _contourList.Count;

        public void Initialize(Tile tile, int size)
        {
            CurrentTile = tile;
            _contourList = new List<Contour>(size);
        }

        public void Add(Contour c) => _contourList.Add(c);

        public Contour this[int index]
        {
            get
            {
                if (index < 0 || index >= _contourList.Count)
                    return null;

                return _contourList[index];
            }
        }

        public Contour Get(int index)
        {
            if (index < 0 || index >= _contourList.Count)
                return null;

            return _contourList[index];
        }

        private void TransformFromJsonObject(ContourSet_JsonObject jsonObject)
        {
            CurrentTile = jsonObject.CurrentTile;
            _contourList = jsonObject.ContourList;
        }

        private ContourSet_JsonObject TransformToJsonObject()
        {
            var jsonObj = new ContourSet_JsonObject();
            jsonObj.CurrentTile = CurrentTile;
            jsonObj.ContourList = _contourList;
            return jsonObj;
        }

        public void PersistData(string path)
        {
            var fileName = "tile" + CurrentTile.Id + ".json";
            var filePath = System.IO.Path.Combine(path, fileName);

            var jsonObj = TransformToJsonObject();
            var json = JsonConvert.SerializeObject(jsonObj);

            System.IO.File.WriteAllText(filePath, json);
        }

        public static ContourSet LoadFromJson(string path)
        {
            if (!System.IO.File.Exists(path)) return null;

            var json = System.IO.File.ReadAllText(path);
            var jsonObj = JsonConvert.DeserializeObject<ContourSet_JsonObject>(json);

            var contourSet = new ContourSet();
            contourSet.TransformFromJsonObject(jsonObj);
            return contourSet;
        }

        internal class ContourSet_JsonObject
        {
            public Tile CurrentTile { get; set; }
            public List<Contour> ContourList { get; set; }
        }
    }

    public class Contour
    {
        public int RegionId { get; set; }
        public int[] RawVertices { get; set; }
        public int RawVerticesCount => RawVertices.Length / 4;

        // 存储格式： (x, y, z, regionId) 数组四位存储一个点

        public int[] Vertices { get; set; }
        public int VerticesCount => Vertices.Length / 4;

        public Contour() { } // 需要一个无参的构造函数，不然不好反序列化

        public Contour(int regionId, List<int> rawList, List<int> verticesList)
        {
            if (rawList == null || verticesList == null)
            {
                return;
            }

            RegionId = regionId;
            RawVertices = new int[rawList.Count];
            for (int i = 0; i < RawVertices.Length; i++)
            {
                RawVertices[i] = rawList[i];
            }

            Vertices = new int[verticesList.Count];
            for (int i = 0; i < Vertices.Length; i++)
            {
                Vertices[i] = verticesList[i];
            }
        }
    }

    public class ContourHole
    {
        public Contour Contour { get; set; }
        public int MinX, MinZ, LeftMost;
    }

    public class ContourRegion
    {
        public Contour Outline { get; set; }
        public ContourHole[] Holes { get; set; }
        public int holesCount { get; set; }
    }

    public struct PotentialDiagonal
    {
        public int vertex;
        public int distance;
    }
}