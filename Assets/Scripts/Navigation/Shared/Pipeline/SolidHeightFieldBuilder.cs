using RTS.Utilities.Extensions;
using Navigation.Components;
using Navigation.Flags;
using Navigation.PipelineData;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Logger = Navigation.Utilities.Logger;

namespace Navigation.Pipeline
{

    public class SolidHeightFieldBuilder : INavMeshPipeline
    {
        private DataSet _data;

        #region 快捷访问
        private Tile _tile => _data.CurrentTile;
        private SolidHeightField _shf => _data.SolidHeightField;
        private float _cellSize => _data.NavMeshPreference.CellSize;
        private float _cellHeight => _data.NavMeshPreference.CellHeight;
        private float _agentMaxSlope => _data.NavMeshPreference.AgentMaxSlope;
        private float _agentMaxStepHeight => _data.NavMeshPreference.AgentMaxStepHeight;
        private float _agentHeight => _data.NavMeshPreference.AgentHeight;
        private Logger _logger => _data.Logger;
        #endregion

        private int _voxelXNum, _voxelYNum, _voxelZNum;
        private int _voxelNum;
        private float _minNormalY;
        private int _spanCount = 0; // 在MergeSpan环节记录Span的数量，用于在log中输出
        private Bounds _voxelBox;

        public void Process(DataSet data)
        {
            _data = data;
            _logger.Info("Start Build SolidHeightField");

            Initialize();
            VoxelizeInBox();

            // 过滤算法
            MarkLowHangingSpans();
            MarkLedgeSpans();
            MarkLowHeightSpans();

            _logger.Info($"Summary: SolidHeightSpan Count: {_spanCount}");
            _logger.Info("Build SolidHeightField Finished");
        }

        private void Initialize()
        {
            _voxelXNum = Mathf.CeilToInt(_tile.Width / _cellSize);
            _voxelYNum = Mathf.CeilToInt(_tile.Height / _cellHeight);
            _voxelZNum = Mathf.CeilToInt(_tile.Depth / _cellSize);
            _voxelNum = _voxelXNum * _voxelYNum * _voxelZNum;

            _minNormalY = Mathf.Cos(Mathf.Abs(_agentMaxSlope) / 180 * Mathf.PI);

            _logger.Info($"Voxels num: (width: {_voxelXNum}, height: {_voxelYNum}, depth: {_voxelZNum}), total: {_voxelNum}");

            _shf.Initialize(_tile, _voxelXNum, _voxelYNum, _voxelZNum, _voxelNum, _minNormalY);

            Vector3 size = new Vector3(_tile.Width, _tile.Height, _tile.Depth);
            Vector3 center = new Vector3(_tile.Min.x + size.x / 2, _tile.Min.y + size.y / 2, _tile.Min.z + size.z / 2);
            _voxelBox = new Bounds(center, size);
        }

        private void VoxelizeInBox()
        {
            // 获取场景内所有的gameObject，逐个判断是否在VoxelizationBox范围内。
            foreach (var ob in Object.FindObjectsOfType<StaticObstacle>())
            {
                var mf = ob.GetComponent<MeshFilter>();
                if (mf == null) continue; // 这里不会走到，因为StaticObstacle组件必须挂载在MeshRenderer上
                var mesh = mf.sharedMesh;

                var bounds = ob.GetComponent<MeshRenderer>().bounds;
                if (!_voxelBox.Intersects(bounds)) continue;

                // 物体和VoxelizationBox有交叉
                // 获取物体Mesh的全部三角面，逐个光栅化（标记其占用的体素）
                int[] triangles = (int[])mesh.triangles.Clone();
                Vector3[] vertices = (Vector3[])mesh.vertices.Clone();
                var goTrans = ob.transform;

                Matrix4x4 transMatrix = new Matrix4x4();
                transMatrix.SetTRS(goTrans.position, goTrans.rotation, goTrans.localScale);
                for (int i = 0; i < vertices.Length; i++)
                {
                    var vertex = vertices[i];
                    vertex = transMatrix.MultiplyPoint(vertex);
                    vertices[i] = vertex;
                }

                var position = ob.transform.position;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int j = i + 1, k = i + 2;
                    var flag = GetTriangleWalkableFlag(vertices[triangles[i]],
                                                        vertices[triangles[j]],
                                                        vertices[triangles[k]]);
                    if (flag == AreaMask.Walkable)
                        flag = ob.Area;

                    RasterizeTriangle(vertices[triangles[i]],
                                        vertices[triangles[j]],
                                        vertices[triangles[k]],
                                        flag);
                }
            }

            MergeSpan();
        }

        private void MergeSpan()
        {
            _spanCount = 0;
            foreach (var span in _shf.SpanDict.Values)
            {
                var current = span;
                while (current != null)
                {
                    var next = current.Next;
                    while (next != null && current.Top + 1 == next.Bottom) // Merge
                    {
                        current.Top = next.Top;
                        current.Area = next.Area;
                        current.Next = next.Next;
                        next = next.Next;
                    }

                    _spanCount++;
                    current = current.Next;
                }
            }
        }

        AreaMask GetTriangleWalkableFlag(Vector3 a, Vector3 b, Vector3 c)
        {
            var normal = Vector3.Cross(a - b, a - c).normalized;

            return (normal.y >= _minNormalY) ? AreaMask.Walkable : AreaMask.NotWalkable;
        }

        private void RasterizeTriangle(Vector3 a, Vector3 b, Vector3 c, AreaMask area)
        {
            Bounds triBound = new Bounds();
            triBound.max = a.ComponentMax(b).ComponentMax(c);
            triBound.min = a.ComponentMin(b).ComponentMin(c);

            if (!_voxelBox.Intersects(triBound))
                return;

            var z0 = Mathf.Clamp(
                            Mathf.FloorToInt((triBound.min.z - _voxelBox.min.z) / _cellSize),
                            0,
                            _voxelZNum - 1
                        );

            var z1 = Mathf.Clamp(
                            Mathf.FloorToInt((triBound.max.z - _voxelBox.min.z) / _cellSize),
                            0,
                            _voxelZNum - 1
                        );

            // 一个三角形被正方形切割得到的图形最多有七个顶点
            List<Vector3> NextRow = new List<Vector3>(7);
            List<Vector3> CurrentRow = new List<Vector3>(7);
            List<Vector3> NextGrid = new List<Vector3>(7);
            List<Vector3> CurrentGrid = new List<Vector3>(7);

            NextRow.Add(a);
            NextRow.Add(b);
            NextRow.Add(c);

            for (int z = z0; z <= z1; z++)
            {
                float zSecant = _voxelBox.min.z + (z + 1) * _cellSize;

                DividePolygon(NextRow, CurrentRow, zSecant, true);

                if (CurrentRow.Count < 3)
                    continue;

                float minX = CurrentRow[0].x, maxX = CurrentRow[0].x;
                for (int i = 1; i < CurrentRow.Count; i++)
                {
                    minX = Mathf.Min(minX, CurrentRow[i].x);
                    maxX = Mathf.Max(maxX, CurrentRow[i].x);
                }

                var x0 = Mathf.Clamp(
                                Mathf.FloorToInt((minX - _voxelBox.min.x) / _cellSize),
                                0,
                                _voxelXNum - 1
                            );

                var x1 = Mathf.Clamp(
                                Mathf.FloorToInt((maxX - _voxelBox.min.x) / _cellSize),
                                0,
                                _voxelXNum - 1
                            );

                for (int x = x0; x <= x1; x++)
                {
                    float xSecant = _voxelBox.min.x + (x + 1) * _cellSize;

                    DividePolygon(CurrentRow, CurrentGrid, xSecant, false);

                    if (CurrentGrid.Count < 3)
                        continue;

                    float minY = CurrentGrid[0].y, maxY = CurrentGrid[0].y;
                    for (int i = 0; i < CurrentGrid.Count; i++)
                    {
                        minY = Mathf.Min(minY, CurrentGrid[i].y);
                        maxY = Mathf.Max(maxY, CurrentGrid[i].y);
                    }

                    if (maxY <= _voxelBox.min.y || minY >= _voxelBox.max.y)
                        continue;

                    var y0 = Mathf.Clamp(
                                    Mathf.FloorToInt((minY - _voxelBox.min.y) / _cellHeight),
                                    0,
                                    _voxelYNum - 1
                                );

                    var y1 = Mathf.Clamp(
                                    Mathf.FloorToInt((maxY - _voxelBox.min.y) / _cellHeight),
                                    y0,
                                    _voxelYNum - 1
                                );

                    for (int y = y0; y <= y1; y++)
                    {
                        GenerateSpan(x, y, z, area);
                    }
                }
            }
        }

        private void DividePolygon(List<Vector3> divided, List<Vector3> result, float secant, bool zAxis)
        {
            List<Vector3> nextPart = new List<Vector3>(7);
            result.Clear();

            for (int i = 1; i <= divided.Count; i++)
            {
                Vector3 a = divided[i - 1], b = divided[i % divided.Count];

                // true -> nextPart, false -> result
                bool aBelongs = false, bBelongs = false;
                aBelongs = zAxis ? (a.z >= secant) : (a.x >= secant);
                bBelongs = zAxis ? (b.z >= secant) : (b.x >= secant);

                if (i == 1)
                {
                    if (aBelongs) nextPart.Add(a);
                    else result.Add(a);
                }

                if (aBelongs ^ bBelongs)
                {
                    float proportion, intersectX, intersectY, intersectZ;

                    if (zAxis)
                    {
                        proportion = (secant - a.z) / (b.z - a.z);
                        intersectX = a.x + (b.x - a.x) * proportion;
                        intersectZ = secant;
                    }
                    else
                    {
                        proportion = (secant - a.x) / (b.x - a.x);
                        intersectX = secant;
                        intersectZ = a.z + (b.z - a.z) * proportion;
                    }

                    intersectY = a.y + (b.y - a.y) * proportion;

                    var intersect = new Vector3(intersectX, intersectY, intersectZ);
                    nextPart.Add(intersect);
                    result.Add(intersect);
                }

                if (i != divided.Count)
                {
                    if (bBelongs) nextPart.Add(b);
                    else result.Add(b);
                }
            }

            divided.Clear();
            divided.AddRange(nextPart);
        }

        private void GenerateSpan(int x, int y, int z, AreaMask area)
        {
            int id = z * _voxelXNum + x;
            _shf.SpanDict.TryGetValue(id, out SolidHeightSpan span);

            if (span == null)
            {
                span = new SolidHeightSpan(y, y, null, area);
                _shf.SpanDict.Add(id, span);
            }
            else
            {
                if (y < span.Bottom)
                {
                    SolidHeightSpan newSpan = new SolidHeightSpan(y, y, span, area);
                    _shf.SpanDict[id] = newSpan;
                }
                else
                {
                    SolidHeightSpan prev = span;
                    while (prev != null)
                    {
                        if (span != null && span.Bottom <= y && span.Top >= y)
                        {
                            // Walkable优先级高于NotWalkable
                            if (span.Top == y && area > span.Area)
                                span.Area = area;
                            break;
                        }

                        if (y > prev.Top && (span == null || y < span.Bottom))
                        {
                            SolidHeightSpan newSpan = new SolidHeightSpan(y, y, span, area);
                            prev.Next = newSpan;
                            break;
                        }

                        prev = span;
                        span = span.Next;
                    }
                }
            }
        }

        private void MarkLedgeSpans()
        {
            // 暂时的理解是剔除掉单个不合理的Span
            int[] dirX = new int[] { 0, 0, 1, -1 };
            int[] dirZ = new int[] { 1, -1, 0, 0 };
            int maxTraversableStep = Mathf.CeilToInt(_agentMaxStepHeight / _cellHeight);
            int minTraversableHeight = Mathf.CeilToInt(_agentHeight / _cellHeight);

            foreach (var id in _shf.SpanDict.Keys)
            {
                var currentSpan = _shf.SpanDict[id];
                while (currentSpan != null)
                {
                    if (currentSpan.Area >= AreaMask.Walkable) // 仅考虑可以行走的面
                    {
                        int curZ = id / _voxelXNum;
                        int curX = id % _voxelXNum;

                        int curFloor = currentSpan.Top;
                        int curCeiling = (currentSpan.Next != null) ? currentSpan.Next.Bottom : int.MaxValue;
                        int minDropToNeighbor = int.MaxValue;

                        for (int i = 0; i < 4; i++)
                        {
                            int nextX = curX + dirX[i];
                            int nextZ = curZ + dirZ[i];

                            var nextSpan = TryGetSpanByCoordinate(nextX, nextZ);
                            if (nextSpan == null)
                                continue;

                            while (nextSpan != null)
                            {
                                if (nextSpan.Area >= AreaMask.Walkable)
                                {
                                    int nextFloor = nextSpan.Top;
                                    int nextCeiling = (nextSpan.Next != null) ? nextSpan.Next.Bottom : int.MaxValue;

                                    if (Mathf.Min(curCeiling, nextCeiling) - Mathf.Max(curFloor, nextFloor)
                                        >= minTraversableHeight)
                                    {
                                        minDropToNeighbor = Mathf.Min(minDropToNeighbor, Mathf.Abs(nextFloor - curFloor));
                                    }
                                }
                                nextSpan = nextSpan.Next;
                            }
                        }

                        if (minDropToNeighbor > maxTraversableStep)
                        {
                            currentSpan.Area = AreaMask.NotWalkable;
                        }
                    }

                    currentSpan = currentSpan.Next;
                }
            }
        }

        /*
        注解：
            过滤悬空的可走障碍物
            对应：rcFilterLowHangingWalkableObstacles()
            逻辑：
                上下两个span，下span可走，上span不可走，
                并且上下span的上表面相差不超过maxTraversableStep，则把上span也改为可走
            用途：
                做法的意图是想对地形不整齐的地方做修缮, 如果地形整齐，这个方法没有意义，事实上不整齐的情况也很少见。
                实际美术做场景时，一般会先弄个terrain,然后往上摆各种东西，有时候摆的就不是严丝合缝。
                如果摆的东西略高了一些，导致了和terrain占用了上下两个Span的情况，如果该物体在贴近地形的位置有
                大于agentMaxSlope的斜面时，那这一个体素就变成不可走的了。这个方法就是为了修复这种情况。
        */
        private void MarkLowHangingSpans()
        {
            int maxTraversableStep = Mathf.CeilToInt(_agentMaxStepHeight / _cellHeight);
            int minTraversableHeight = Mathf.CeilToInt(_agentHeight / _cellHeight);

            foreach (var id in _shf.SpanDict.Keys)
            {
                var currentSpan = _shf.SpanDict[id];
                while (currentSpan != null)
                {
                    if (currentSpan.Area >= AreaMask.Walkable) // 仅考虑可以行走的面
                    {
                        int curFloor = currentSpan.Top;

                        var nextSpan = currentSpan.Next;
                        if (nextSpan != null)
                        {
                            int nextFloor = nextSpan.Top;

                            if (nextSpan.Area < AreaMask.Walkable // 上面的span不可走
                                && Mathf.Abs(nextFloor - curFloor) <= maxTraversableStep)
                            {
                                nextSpan.Area = AreaMask.Walkable;
                            }
                        }
                    }

                    currentSpan = currentSpan.Next;
                }
            }
        }

        private void MarkLowHeightSpans()
        {
            foreach (var span in _shf.SpanDict.Values)
            {
                var current = span;
                while (current.Next != null)
                {
                    // 间隔小于Agent高度，标记为不可行走
                    if (_cellHeight * (current.Next.Bottom - current.Top) < _agentHeight)
                    {
                        current.Area = AreaMask.NotWalkable;
                    }
                    current = current.Next;
                }
            }
        }


        private SolidHeightSpan TryGetSpanByCoordinate(int x, int z)
        {
            _shf.SpanDict.TryGetValue(z * _voxelXNum + x, out SolidHeightSpan value);
            return value;
        }
    }
}