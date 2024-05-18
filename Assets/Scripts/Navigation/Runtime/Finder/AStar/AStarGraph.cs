using Navigation.Flags;
using Navigation.PipelineData;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Navigation.Finder.PathFinding
{
    public class AStarGraph
    {
        public int Count => _graph.Count;
        private Dictionary<Vector3, HashSet<Vector3>> _graph;

        public HashSet<Vector3> this[Vector3 key]
        {
            get
            {
                return (_graph.ContainsKey(key))
                        ? _graph[key]
                        : null;
            }
            set
            {
                if (_graph.ContainsKey(key))
                {
                    _graph[key] = value;
                }
                else
                {
                    _graph.Add(key, value);
                }
            }
        }

        private PolyMeshField _pmf;
        private Polygon[] _polygons;

        /// <summary>
        /// TODO：这是一个临时的LoadData代码，我还不清楚具体打包之后的路径是什么样的
        /// 
        /// LoadData做三部分工作：
        /// 1. 根据 AgentType 读取 PolyMeshField的数据
        /// 2. 生成 Polygon
        /// 3. 生成 Graph
        /// 4. 修改传入的 AStarOption 中的起点和终点数据，使其能够被 AStar 使用
        /// </summary>
        /// <returns>返回Graph数据是否成功生成了</returns>
        public bool LoadData(AStarOption option)
        {
            var activeScene = SceneManager.GetActiveScene();
            string scenePath = System.IO.Path.Combine(Application.dataPath,
                                    activeScene.path.Substring(7));
            FileInfo sceneFile = new FileInfo(scenePath);
            string dataFolderPath = System.IO.Path.Combine(sceneFile.DirectoryName, activeScene.name + "_Navigation");
            string agentFolderPath = System.IO.Path.Combine(dataFolderPath, option.Agent.ToString());
            string displayDataFolderPath = System.IO.Path.Combine(agentFolderPath, "PolyMeshField");
            if (Directory.Exists(displayDataFolderPath))
            {
                DirectoryInfo dir = new DirectoryInfo(displayDataFolderPath);
                var json = dir.GetFiles($"tile0.json").First();

                _pmf = PolyMeshField.LoadFromJson(json.FullName);
            }

            if (_pmf == null)
                return false;

            GeneratorPolygons();
            GeneratorGraph();

            // 找到起点，终点所在的 Polygon
            Vector3 start = option.Start - _pmf.CurrentTile.Min;
            start.x = Mathf.Floor(start.x / 0.2f);
            start.y = Mathf.Floor(start.y / 0.3f);
            start.z = Mathf.Floor(start.z / 0.2f);

            Vector3 destination = option.Destination - _pmf.CurrentTile.Min;
            destination.x = Mathf.Floor(destination.x / 0.2f);
            destination.y = Mathf.Floor(destination.y / 0.3f);
            destination.z = Mathf.Floor(destination.z / 0.2f);

            option.Start = start;
            option.Destination = destination;

            Polygon startPolygon = null;
            Polygon destinationPolygon = null;

            // 找距离最近的
            float minDistance = float.MaxValue;
            for (int i = 0; i < _polygons.Length; i++)
            {
                // < 0 表示点在多边形外
                float distance = PointToPolygonDistance(start, _polygons[i]);
                if (distance >= 0 && distance < minDistance)
                {
                    minDistance = distance;
                    startPolygon = _polygons[i];
                }
            }

            minDistance = float.MaxValue;
            for (int i = 0; i < _polygons.Length; i++)
            {
                float distance = PointToPolygonDistance(destination, _polygons[i]);
                if (distance >= 0 && distance < minDistance)
                {
                    minDistance = distance;
                    destinationPolygon = _polygons[i];
                }
            }

            // TODO：这里可以改一下，当终点不在多边形内时，可以找到最近的多边形的顶点，然后以这个顶点为终点
            if (startPolygon == null || destinationPolygon == null)
                return false;

            // 建立起点、终点和其所在Polygon顶点的连边
            // 仅需构建起点到起点Polygon的边和终点Polygon到终点的边
            // 无需构建双向这里，因为没必要
            if (!_graph.ContainsKey(start))
            {
                _graph.Add(start, new HashSet<Vector3>());
            }

            for (int i = 0; i < startPolygon.Vertices.Length; i++)
            {
                _graph[start].Add(startPolygon.Vertices[i]);
            }

            if (!_graph.ContainsKey(destination))
            {
                _graph.Add(destination, new HashSet<Vector3>());
            }

            for (int i = 0; i < destinationPolygon.Vertices.Length; i++)
            {
                _graph[destinationPolygon.Vertices[i]].Add(destination);
            }

            return true;
        }

        private void GeneratorPolygons()
        {
            _polygons = new Polygon[_pmf.RegionIndices.Length];
            for (int i = 0; i < _pmf.RegionIndices.Length; i++)
            {
                int index = i * 6 * 2; // 注意，我直接默认 VerticesPerPoly = 6了

                int vertexCount = 0;
                while (vertexCount < 6 && _pmf.Polygons[index + vertexCount] != -1)
                {
                    vertexCount++;
                }

                int neighborCount = 0;
                while (neighborCount < 6 && _pmf.Polygons[index + 6 + neighborCount] != -1)
                {
                    neighborCount++;
                }

                _polygons[i] = MakePolygon(index, vertexCount, neighborCount);
            }

            // 为每个多边形设置邻居
            // 好像没啥特别好的办法，只能遍历所有的 Polygon
            for (int i = 0; i < _polygons.Length; i++)
            {
                _polygons[i].Neighbors = new List<Polygon>(6);
                for (int j = 0; j < _polygons.Length; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    for (int k = 0; k < _polygons[i].Vertices.Length; k++)
                    {
                        if (_polygons[j].Vertices.Contains(_polygons[i].Vertices[k]))
                        {
                            _polygons[i].Neighbors.Add(_polygons[i]);
                            break;
                        }
                    }
                }
            }

            // 后面可能是需要调整一下 PolyMesh 的数据？或者PolyMesh要多做一些处理
            // 不然像现在这样，Neighbor其实没起到作用，只能用暴力的方式去遍历所有的 Polygon
            // 有点蠢
        }

        private Polygon MakePolygon(int index, int vertexCount, int neighborCount)
        {
            Polygon p = new Polygon();

            p.Vertices = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                int vertex = _pmf.Polygons[index + i];

                p.Vertices[i] = new Vector3(_pmf.Vertices[vertex * 3],
                                                _pmf.Vertices[vertex * 3 + 1],
                                                _pmf.Vertices[vertex * 3 + 2]);
            }

            // 目前是以 Polygon 顶点进行寻路的，后面会改成使用边中点
            // p.Vertices = new Vector3[vertexCount]; 
            // for (int i = 0, j = vertexCount - 1; i < vertexCount; j = i++)
            // {
            //     p.Vertices[i] = (p.Vertices[i] + p.Vertices[j]) / 2;
            // }

            return p;
        }

        private void GeneratorGraph()
        {
            _graph = new Dictionary<Vector3, HashSet<Vector3>>();

            for (int i = 0; i < _polygons.Length; i++)
            {
                for (int j = 0; j < _polygons[i].Vertices.Length; j++)
                {
                    if (!_graph.ContainsKey(_polygons[i].Vertices[j]))
                    {
                        _graph[_polygons[i].Vertices[j]] = new HashSet<Vector3>();
                    }

                    for (int k = 0; k < _polygons[i].Vertices.Length; k++)
                    {
                        if (j == k)
                        {
                            continue;
                        }

                        _graph[_polygons[i].Vertices[j]].Add(_polygons[i].Vertices[k]);
                    }
                }
            }

            /*
                这里看上去我们只对每个 Polygon 内部顶点之间相互连了边
                但是其实相邻 Polygon 之间的顶点也是相互连的
                因为例如 PolyA 中包含 A 点，则 A 点首先会和 PolyA 中的剩余点构建链接关系
                然后如果 PolyA 的邻居 PolyB 中也包含 A 点，则 A 点会和 PolyB 中的剩余点构建链接关系
                即其实相邻的 Polygon 之间的顶点是共享的
            */
        }

        private float PointToPolygonDistance(Vector3 point, Polygon polygon)
        {
            bool isInside = false;

            // 采用射线法去判断点是否在多边形内
            for (int i = 0, j = polygon.Vertices.Length - 1; i < polygon.Vertices.Length; j = i++)
            {
                // 以 A - B 为边
                Vector3 A = polygon.Vertices[j];
                Vector3 B = polygon.Vertices[i];

                // 从 (x, z) 向 x 正方向发出射线，检测是否和 AB 相交
                if (((B.z > point.z) != (A.z > point.z)) // a, b的 z 坐标同时大于或小于 z 则说明肯定不相交
                        && (point.x < (A.x - B.x) * (point.z - B.z) / (A.z - B.z) + B.x))
                {
                    // 奇数次交点，说明在多边形内，偶数次交点，说明在多边形外
                    isInside = !isInside;
                }
            }

            // 粗略计算顶点和多边形面的距离，主要是为了区分在 y 方向上的多个多边形
            // 使用 point.y 和多边形顶点 y 的均值的差值作为距离
            float average = 0f;
            for (int i = 0; i < polygon.Vertices.Length; i++)
            {
                average += polygon.Vertices[i].y;
            }
            average /= polygon.Vertices.Length;


            return isInside ? Mathf.Abs(average - point.y) : -1f;
        }

        public void PathToWorldPosition(List<Vector3> path)
        {
            for (int i = 0; i < path.Count; i++)
            {
                var world = new Vector3
                {
                    x = path[i].x * 0.2f + _pmf.CurrentTile.Min.x,
                    y = path[i].y * 0.3f + _pmf.CurrentTile.Min.y,
                    z = path[i].z * 0.2f + _pmf.CurrentTile.Min.z
                };

                path[i] = world;
            }

        }
    }

    internal class Polygon
    {
        /// <summary>
        /// Polygon 所有的顶点，体素坐标
        /// </summary>
        public Vector3[] Vertices;


        /// <summary>
        /// Polygon 的所有相邻的 Polygon
        /// </summary>
        public List<Polygon> Neighbors;
    }
}