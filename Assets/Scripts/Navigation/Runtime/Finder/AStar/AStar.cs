using Navigation.Utilities;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Navigation.Finder.PathFinding
{
    /// <summary>
    /// 目前只是一个简陋的版本，只能使用 Tile0的 PolyMesh 数据进行寻路
    /// </summary>
    public sealed class AStar
    {
        private static AStarGraph _graph = new AStarGraph();
        private static List<Vector3> _path = new List<Vector3>();

        public static List<Vector3> FindPath(AStarOption option)
        {
            _path.Clear();

            // 因为后面会修改 option.Destination，使其变成基于 Tile.Min的体素坐标
            // 再转回来时无法和原数值相同，所以先保存一份
            var dest = option.Destination;

            if (_graph.LoadData(option)) // LoadData内部会做判断，避免反复读数据
            {
                AStarAlgorithm(option.Start, option.Destination);
                _graph.PathToWorldPosition(_path);
            }

            // clone 一份 path 返回，不然所有 Agent 共用一份 path了
            List<Vector3> path = new List<Vector3>(_path.Count);
            path.Add(dest);
            for (int i = 1; i < _path.Count - 1; i++)
            {
                path.Add(_path[i] + Vector3.up); // TODO：待修改，这里不应该该path的坐标，这只是为了演示效果而添加的
            }
            // 直接舍弃掉 Start即可

            return path;
        }

        private static bool AStarAlgorithm(Vector3 start, Vector3 destination)
        {
            // G: 起点到当前点的距离， H: 当前点到目的地的距离， F: G + H
            // F 值越小，说明离目的地越近

            Dictionary<Vector3, float> gScore = new Dictionary<Vector3, float>();
            Dictionary<Vector3, float> fScore = new Dictionary<Vector3, float>();

            // 用于记录路径
            Dictionary<Vector3, Vector3> cameFrom = new Dictionary<Vector3, Vector3>();

            // 用于记录已经访问过的点
            HashSet<Vector3> closedSet = new HashSet<Vector3>();

            // 用于记录待访问的点
            HashSet<Vector3> openSet = new HashSet<Vector3>();

            // 初始化
            gScore[start] = 0;
            fScore[start] = Vector3.Distance(start, destination);
            openSet.Add(start);

            while (openSet.Count > 0)
            {
                // 从 openSet 中找到 F 值最小的点
                Vector3 current = openSet.First();
                foreach (Vector3 v in openSet)
                {
                    if (fScore[v] < fScore[current])
                    {
                        current = v;
                    }
                }

                // 如果当前点就是目的地，那么就找到了路径
                if (current == destination)
                {
                    ReconstructPath(cameFrom, current);
                    return true;
                }

                // 从 openSet 中移除当前点
                openSet.Remove(current);

                // 将当前点加入 closedSet
                closedSet.Add(current);

                // 遍历当前点的邻居
                foreach (Vector3 neighbor in _graph[current])
                {
                    // 如果邻居已经在 closedSet 中，那么跳过
                    if (closedSet.Contains(neighbor))
                    {
                        continue;
                    }

                    // 计算从起点到当前点的距离
                    float tentativeGScore = gScore[current] + Vector3.Distance(current, neighbor);

                    // 如果邻居不在 openSet 中，那么加入 openSet
                    if (!openSet.Contains(neighbor))
                    {
                        openSet.Add(neighbor);
                    }
                    // 如果从起点到当前点的距离比从起点到邻居的距离要长，那么跳过
                    else if (tentativeGScore >= gScore[neighbor])
                    {
                        continue;
                    }

                    // 记录当前点是从哪个点过来的
                    cameFrom[neighbor] = current;

                    // 更新 G 值
                    gScore[neighbor] = tentativeGScore;

                    // 更新 F 值
                    fScore[neighbor] = gScore[neighbor] + Vector3.Distance(neighbor, destination);
                }
            }

            return false;
        }

        private static void ReconstructPath(Dictionary<Vector3, Vector3> cameFrom, Vector3 current)
        {
            _path.Clear();
            _path.Add(current);
            while (cameFrom.ContainsKey(current))
            {
                current = cameFrom[current];
                _path.Add(current);
            }
        }
    }
}
