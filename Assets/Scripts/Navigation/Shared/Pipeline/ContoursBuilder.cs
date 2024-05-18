using Navigation.PipelineData;
using Navigation.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Logger = Navigation.Utilities.Logger;

namespace Navigation.Pipeline
{
    public class ContoursBuilder : INavMeshPipeline
    {
        private DataSet _data;

        #region 快捷访问
        private Tile _tile => _data.CurrentTile;
        private ContourSet _contourSet => _data.ContourSet;
        private CompactHeightField _chf => _data.CompactHeightField;
        private float _cellSize => _data.NavMeshPreference.CellSize;
        private float _cellHeight => _data.NavMeshPreference.CellHeight;
        private int _regionCount => _data.RegionCount;
        private float _deviationThreshold => _data.NavMeshPreference.DeviationThreshold;
        private float _maxEdgeLength => _data.NavMeshPreference.MaxEdgeLength;
        private Logger _logger => _data.Logger;
        #endregion

        private int _voxelXNum, _voxelYNum, _voxelZNum;
        private ushort[] _flags;

        public void Process(DataSet data)
        {
            _data = data;
            _logger.Info("Start Build Contours");
            Initialize();

            if (_regionCount <= 0)
                return;

            BuildContour();

            // Debug 输出一下Counters
            // for (int i = 0; i < _contourSet.Count; i++)
            // {
            //     var contour = _contourSet[i];
            //     for (int j = 0; j < contour.VerticesCount; j++)
            //     {
            //         Debug.Log($"Contour {i} Vertex {j} : {contour.Vertices[j * 4]}, {contour.Vertices[(j * 4) + 1]}, {contour.Vertices[(j * 4) + 2]}, {contour.Vertices[(j * 4) + 3]}");
            //     }
            // }

            _logger.Info($"Summary: Counter Count: {_contourSet.Count}");
            _logger.Info("Build Counters Finished");
        }

        private void Initialize()
        {
            _voxelXNum = Mathf.CeilToInt(_tile.Width / _cellSize);
            _voxelYNum = Mathf.CeilToInt(_tile.Height / _cellHeight);
            _voxelZNum = Mathf.CeilToInt(_tile.Depth / _cellSize);

            _contourSet.Initialize(_tile, _regionCount);
            _flags = new ushort[_chf.Spans.Length];
        }

        private void BuildContour()
        {
            int discardedContours = 0;
            for (int i = 0; i < _chf.Spans.Length; i++)
            {
                var span = _chf.Spans[i];
                _flags[i] = 0;

                if (span.RegionId != 0)
                {
                    for (int dir = 0; dir < 4; dir++)
                    {
                        int nRegionId = 0;
                        var nSpan = span.Neighbors[dir] != uint.MaxValue
                                        ? _chf.Spans[span.Neighbors[dir]]
                                        : null;
                        if (nSpan != null)
                            nRegionId = nSpan.RegionId;

                        if (nRegionId == span.RegionId)
                            _flags[i] |= (ushort)(1 << dir);
                    }

                    _flags[i] ^= 0xf;
                    if (_flags[i] == 0xf)
                    {
                        _flags[i] = 0;
                        discardedContours++;
                    }
                }
            }

            List<int> workingRawVertices = new List<int>(256);
            List<int> workingSimplifiedVertices = new List<int>(64);

            for (int id = 0; id < _chf.Cells.Length; id++)
            {
                var cell = _chf.Cells[id];
                if (cell.FirstSpan == uint.MaxValue)
                    continue;

                int curX = id % _voxelXNum;
                int curZ = id / _voxelXNum;

                for (uint i = 0; i < cell.Count; i++)
                {
                    var spanIndex = cell.FirstSpan + i;
                    var span = _chf.Spans[spanIndex];
                    if (span.RegionId > 0 && _flags[spanIndex] != 0)
                    {
                        workingRawVertices.Clear();
                        workingSimplifiedVertices.Clear();

                        int startDirection = 0;
                        while (IsTheSameRegion(spanIndex, startDirection))
                        {
                            startDirection++;
                        }

                        BuildRawContours(spanIndex, curX, curZ,
                            startDirection, workingRawVertices);
                        GenerateSimplifiedContour(span.RegionId,
                            workingRawVertices, workingSimplifiedVertices);
                        RemoveDegenerateContour(workingSimplifiedVertices);

                        if (workingSimplifiedVertices.Count < 12) // 3 * 4 = 12 -> 表示不到一个三角形
                        {
                            discardedContours++;
                        }
                        else
                        {
                            _contourSet.Add(new Contour(span.RegionId, workingRawVertices, workingSimplifiedVertices));
                        }
                    }
                }
            }

            // 理论上不会出现 discardedContours > 0 的情况
            if (discardedContours > 0)
            {
                _logger.Error($"Simplified contour failed, discardedContours = {discardedContours}");
            }

            // Merge holes 
            if (_contourSet.Count > 0)
            {
                short[] winding = new short[_contourSet.Count];
                int holeCount = 0;

                for (int i = 0; i < _contourSet.Count; i++)
                {
                    var contour = _contourSet[i];

                    winding[i] = CalcAreaOfPolygon2D(contour.Vertices) < 0 ? (short)-1 : (short)1;
                    if (winding[i] < 0)
                        holeCount++;
                }


                if (holeCount > 0)
                {
                    int regionCount = _regionCount + 1;
                    ContourRegion[] regions = new ContourRegion[regionCount];
                    for (int i = 0; i < regions.Length; i++)
                        regions[i] = new ContourRegion();

                    for (int i = 0; i < _contourSet.Count; i++)
                    {
                        Contour contour = _contourSet[i];
                        if (winding[i] > 0)
                        {
                            regions[contour.RegionId].Outline = contour;
                        }
                        else
                        {
                            regions[contour.RegionId].holesCount++;
                        }
                    }

                    for (int i = 0; i < regions.Length; i++)
                    {
                        if (regions[i].holesCount > 0)
                        {
                            regions[i].Holes = new ContourHole[regions[i].holesCount];
                            for (int j = 0; j < regions[i].Holes.Length; j++)
                                regions[i].Holes[j] = new ContourHole();
                            regions[i].holesCount = 0;
                        }
                    }

                    for (int i = 0; i < _contourSet.Count; i++)
                    {
                        Contour contour = _contourSet[i];
                        ContourRegion region = regions[contour.RegionId];
                        if (winding[i] < 0)
                        {
                            region.Holes[region.holesCount++].Contour = contour;
                        }
                    }

                    for (int i = 0; i < regionCount; i++)
                    {
                        ContourRegion region = regions[i];

                        // TODO： 验证一下 region的Outline何时会为null ？
                        if (region.holesCount > 0 && region.Outline != null)
                        {
                            MergeRegionHoles(region);
                        }
                    }
                }
            }
        }

        private bool IsTheSameRegion(uint index, int direction)
        {
            return index == uint.MaxValue
                    ? false
                    : (_flags[index] & (1 << direction)) == 0;
        }

        private void BuildRawContours(uint startIndex, int startX, int startZ, int startDirection, List<int> outContourVertices)
        {
            var index = startIndex;
            var startSpan = _chf.Spans[startIndex];
            var span = startSpan;
            int direction = startDirection;
            int spanX = startX;
            int spanZ = startZ;
            int loopCount = 0;
            while (++loopCount < ushort.MaxValue)
            {
                if (!IsTheSameRegion(index, direction))
                {
                    int pX = spanX;
                    int pY = GetCornerHeight(index, direction);
                    int pZ = spanZ;

                    switch (direction)
                    {
                        case 0:
                            pZ++;
                            break;
                        case 1:
                            pX++;
                            pZ++;
                            break;
                        case 2:
                            pX++;
                            break;
                    }

                    int regionThisDirection = 0;
                    var nSpan = span.Neighbors[direction] != uint.MaxValue
                                    ? _chf.Spans[span.Neighbors[direction]]
                                    : null;
                    if (nSpan != null)
                    {
                        regionThisDirection = nSpan.RegionId;
                    }

                    outContourVertices.Add(pX);
                    outContourVertices.Add(pY);
                    outContourVertices.Add(pZ);
                    outContourVertices.Add(regionThisDirection);

                    _flags[index] &= (ushort)~(1 << direction);
                    direction = CalculateGeometry.RotateDirectionClockwise(direction);
                }
                else
                {
                    index = span.Neighbors[direction];
                    span = index != uint.MaxValue
                                ? _chf.Spans[index]
                                : null;

                    switch (direction)
                    {
                        case 0:
                            spanX--;
                            break;
                        case 1:
                            spanZ++;
                            break;
                        case 2:
                            spanX++;
                            break;
                        case 3:
                            spanZ--;
                            break;
                    }
                    direction = CalculateGeometry.RotateDirectionCounterClockwise(direction);
                }

                if (span == startSpan && direction == startDirection)
                    break;
            }
        }

        private int GetCornerHeight(uint spanIndex, int direction)
        {
            var span = _chf.Spans[spanIndex];
            int maxFloor = span.Floor;
            CompactHeightSpan dSpan = null;

            int directionOffset = CalculateGeometry.RotateDirectionClockwise(direction);
            var nSpan = span.Neighbors[direction] != uint.MaxValue
                            ? _chf.Spans[span.Neighbors[direction]]
                            : null;
            if (nSpan != null)
            {
                maxFloor = Mathf.Max(maxFloor, nSpan.Floor);
                dSpan = nSpan.Neighbors[directionOffset] != uint.MaxValue
                            ? _chf.Spans[nSpan.Neighbors[directionOffset]]
                            : null;
            }

            nSpan = span.Neighbors[directionOffset] != uint.MaxValue
                        ? _chf.Spans[span.Neighbors[directionOffset]]
                        : null;
            if (nSpan != null)
            {
                maxFloor = Mathf.Max(maxFloor, nSpan.Floor);
                if (dSpan == null)
                {
                    dSpan = nSpan.Neighbors[direction] != uint.MaxValue
                                ? _chf.Spans[nSpan.Neighbors[direction]]
                                : null;
                }
            }

            if (dSpan != null)
            {
                maxFloor = Mathf.Max(maxFloor, dSpan.Floor);
            }

            return maxFloor;
        }

        private void GenerateSimplifiedContour(int regionId, List<int> sourceVertices, List<int> outVertices)
        {
            bool noConnections = true;
            for (int i = 0; i < sourceVertices.Count; i += 4)
            {
                if (sourceVertices[i + 3] != 0)
                {
                    noConnections = false;
                    break;
                }
            }

            if (noConnections)
            {
                // ll => lower Left
                // ur => upper right
                int llX = sourceVertices[0];
                int llY = sourceVertices[1];
                int llZ = sourceVertices[2];
                int llIndex = 0;

                int urX = sourceVertices[0];
                int urY = sourceVertices[1];
                int urZ = sourceVertices[2];
                int urIndex = 0;

                for (int i = 0; i < sourceVertices.Count; i += 4)
                {
                    int x = sourceVertices[i];
                    int y = sourceVertices[i + 1];
                    int z = sourceVertices[i + 2];

                    if (x < llX || (x == llX && z < llZ))
                    {
                        llX = x;
                        llY = y;
                        llZ = z;
                        llIndex = i / 4;
                    }

                    if (x > urX || (x == urX && z > urZ))
                    {
                        urX = x;
                        urY = y;
                        urZ = z;
                        urIndex = i / 4;
                    }
                }

                outVertices.Add(llX);
                outVertices.Add(llY);
                outVertices.Add(llZ);
                outVertices.Add(llIndex);

                outVertices.Add(urX);
                outVertices.Add(urY);
                outVertices.Add(urZ);
                outVertices.Add(urIndex);
            }
            else
            {
                for (int i = 0, vertexCount = sourceVertices.Count / 4; i < vertexCount; i++)
                {
                    if (sourceVertices[i * 4 + 3] != sourceVertices[((i + 1) % vertexCount) * 4 + 3])
                    {
                        outVertices.Add(sourceVertices[i * 4]);
                        outVertices.Add(sourceVertices[i * 4 + 1]);
                        outVertices.Add(sourceVertices[i * 4 + 2]);
                        outVertices.Add(i);
                    }
                }
            }

            SimplifiedEdge(sourceVertices, outVertices);
            SplitLongEdge(sourceVertices, outVertices);

            int sourceVerticesCount = sourceVertices.Count / 4;
            int simplifiedVertexCount = outVertices.Count / 4;
            for (int i = 0; i < simplifiedVertexCount; i++)
            {
                int iVertexRegion = i * 4 + 3;
                int sourceVertexIndex = (outVertices[iVertexRegion] + 1) % sourceVerticesCount;
                outVertices[iVertexRegion] = sourceVertices[sourceVertexIndex * 4 + 3];
            }
        }

        private void SimplifiedEdge(List<int> sourceVertices, List<int> outVertices)
        {
            if (sourceVertices == null || outVertices == null)
                return;

            int sourceVerticesCount = sourceVertices.Count / 4;
            int simplifiedVertexCount = outVertices.Count / 4;
            int vertexA = 0;

            while (vertexA < outVertices.Count / 4)
            {
                int vertexB = (vertexA + 1) % simplifiedVertexCount;

                int ax = outVertices[vertexA * 4];
                int ay = outVertices[vertexA * 4 + 1];
                int az = outVertices[vertexA * 4 + 2];
                int sourceA = outVertices[vertexA * 4 + 3];

                int bx = outVertices[vertexB * 4];
                int by = outVertices[vertexB * 4 + 1];
                int bz = outVertices[vertexB * 4 + 2];
                int sourceB = outVertices[vertexB * 4 + 3];

                int testVertex = 0;
                float maxDeviation = 0;
                int vertexToInsert = -1;
                int endVertex = 0;
                int step = 1;
                if (bx > ax || (bx == ax && bz > az))
                {
                    step = 1;
                    testVertex = (sourceA + step) % sourceVerticesCount;
                    endVertex = sourceB;
                }
                else
                {
                    step = sourceVerticesCount - 1;
                    testVertex = (sourceB + step) % sourceVerticesCount;
                    endVertex = sourceA;
                    CalculateGeometry.Swap(ref ax, ref bx);
                    CalculateGeometry.Swap(ref ay, ref by);
                    CalculateGeometry.Swap(ref az, ref bz);
                }

                // TODO: 这里的讨论点比较多
                // 1. sourceVertices[testVertex * 4 + 3] == 0 指的是边界，那么这个判断其实是个优化，
                //      但是他会导致在这个阶段出现一些看上去不太合理的线段
                // 2. 计算 deviation 时，到底只考虑 xz 还是 xyz，这同要会对最终的结果有影响
                // if (sourceVertices[testVertex * 4 + 3] == 0)
                // {
                while (testVertex != endVertex)
                {
                    // float deviation = CalculateGeometry.PointSegmentDistance_Squared(
                    //                     sourceVertices[testVertex * 4],
                    //                     sourceVertices[testVertex * 4 + 1],
                    //                     sourceVertices[testVertex * 4 + 2],
                    //                     ax, ay, az, bx, by, bz
                    //                     );

                    float deviation = CalculateGeometry.PointSegmentDistance_Squared(
                        sourceVertices[testVertex * 4],
                        sourceVertices[testVertex * 4 + 2],
                        ax, az, bx, bz
                    );

                    if (deviation > maxDeviation)
                    {
                        maxDeviation = deviation;
                        vertexToInsert = testVertex;
                    }

                    testVertex = (testVertex + step) % sourceVerticesCount;
                }
                // }

                if (vertexToInsert != -1 && maxDeviation > (_deviationThreshold * _deviationThreshold))
                {
                    // _logger.Info("dev: " + Mathf.Sqrt(maxDeviation) + $" ax: {ax * _cellSize + _tile.Min.x}, ay: {ay * _cellHeight + _tile.Min.y}, az: {az * _cellSize + _tile.Min.z}, bx: {bx * _cellSize + _tile.Min.x}, by: {by * _cellHeight + _tile.Min.y}, bz: {bz * _cellSize + _tile.Min.z}, tx: {sourceVertices[vertexToInsert * 4] * _cellSize + _tile.Min.x}, ty: {sourceVertices[vertexToInsert * 4 + 1] * _cellHeight + _tile.Min.y}, tz: {sourceVertices[vertexToInsert * 4 + 2] * _cellSize + _tile.Min.z}");
                    int insertIndex = vertexA + 1;
                    int insertBase = insertIndex * 4;
                    int sourceBase = vertexToInsert * 4;
                    outVertices.Insert(insertBase, sourceVertices[sourceBase]);
                    outVertices.Insert(insertBase + 1, sourceVertices[sourceBase + 1]);
                    outVertices.Insert(insertBase + 2, sourceVertices[sourceBase + 2]);
                    outVertices.Insert(insertBase + 3, vertexToInsert);

                    simplifiedVertexCount = outVertices.Count / 4;
                }
                else
                {
                    vertexA++;
                }
            }
        }

        private void SplitLongEdge(List<int> sourceVertices, List<int> outVertices)
        {
            // 现在在 Editor 中限制了这个参数，确保不会小于5
            if (_maxEdgeLength <= 5) return;

            int sourceVerticesCount = sourceVertices.Count / 4;
            int simplifiedVertexCount = outVertices.Count / 4;
            int vertexA = 0;

            while (vertexA < outVertices.Count / 4)
            {
                int vertexB = (vertexA + 1) % simplifiedVertexCount;

                int ax = outVertices[vertexA * 4];
                int az = outVertices[vertexA * 4 + 2];
                int sourceA = outVertices[vertexA * 4 + 3];

                int bx = outVertices[vertexB * 4];
                int bz = outVertices[vertexB * 4 + 2];
                int sourceB = outVertices[vertexB * 4 + 3];

                int newVertex = -1;
                int testVertex = (sourceA + 1) % sourceVerticesCount;

                int dx = bx - ax;
                int dz = bz - az;
                if (dx * dx + dz * dz > _maxEdgeLength * _maxEdgeLength)
                {
                    int indexDistance = sourceB < sourceA
                                        ? (sourceB + (sourceVerticesCount - sourceA))
                                        : (sourceB - sourceA);
                    if (indexDistance > 1)
                    {
                        newVertex = (bx > ax || (bx == ax && bz > az))
                                    ? (sourceA + indexDistance / 2) % sourceVerticesCount
                                    : (sourceA + (indexDistance + 1) / 2) % sourceVerticesCount;
                    }
                }


                if (newVertex != -1)
                {
                    int insertBase = (vertexA + 1) * 4;
                    int newVertexBase = newVertex * 4;

                    outVertices.Insert(insertBase, sourceVertices[newVertexBase]);
                    outVertices.Insert(insertBase + 1, sourceVertices[newVertexBase + 1]);
                    outVertices.Insert(insertBase + 2, sourceVertices[newVertexBase + 2]);
                    outVertices.Insert(insertBase + 3, newVertex);

                    simplifiedVertexCount = outVertices.Count / 4;
                }
                else
                {
                    vertexA++;
                }
            }
        }

        private void RemoveDegenerateContour(List<int> outVertices)
        {

            int vertexCount = outVertices.Count / 4;
            for (int i = 0; i < vertexCount; i++)
            {
                int nextIndex = (i + 1) % vertexCount;
                if (outVertices[i] == outVertices[nextIndex]
                    && outVertices[i + 2] == outVertices[nextIndex + 2])
                {
                    outVertices.RemoveAt(nextIndex * 4 + 3);
                    outVertices.RemoveAt(nextIndex * 4 + 2);
                    outVertices.RemoveAt(nextIndex * 4 + 1);
                    outVertices.RemoveAt(nextIndex * 4);
                    vertexCount--;
                }
            }
        }

        private int CalcAreaOfPolygon2D(int[] vertices)
        {
            int verticesCount = vertices.Length / 4;
            int area = 0;
            for (int i = 0, j = verticesCount - 1; i < verticesCount; j = i++)
            {
                int vi = i * 4;
                int vj = j * 4;
                area += (vertices[vi] * vertices[vj + 2]) - (vertices[vj] * vertices[vi + 2]);
            }

            return (area + 1) / 2;
        }

        private void FindLeftMostVertex(ContourHole hole)
        {
            Contour contour = hole.Contour;
            hole.MinX = contour.Vertices[0];
            hole.MinZ = contour.Vertices[2];
            hole.LeftMost = 0;
            for (int i = 1; i < contour.VerticesCount; i++)
            {
                int x = contour.Vertices[i * 4 + 0];
                int z = contour.Vertices[i * 4 + 2];
                if (x < hole.MinX || (x == hole.MinX && z < hole.MinZ))
                {
                    hole.MinX = x;
                    hole.MinZ = z;
                    hole.LeftMost = i;
                }
            }
        }

        private void MergeRegionHoles(ContourRegion region)
        {
            for (int i = 0; i < region.holesCount; i++)
                FindLeftMostVertex(region.Holes[i]);

            Array.Sort(region.Holes, 0, region.holesCount, new HoleComparer());

            int maxVertexCount = region.Outline.VerticesCount;
            for (int i = 0; i < region.holesCount; i++)
                maxVertexCount += region.Holes[i].Contour.VerticesCount;

            PotentialDiagonal[] diagonals = new PotentialDiagonal[maxVertexCount];

            Contour outline = region.Outline;
            for (int i = 0; i < region.holesCount; i++)
            {
                Contour hole = region.Holes[i].Contour;

                int index = -1;
                int bestVertex = region.Holes[i].LeftMost;
                for (int j = 0; j < hole.VerticesCount; j++)
                {
                    int diagonalsCount = 0;
                    int[] corner = hole.Vertices[(bestVertex * 4)..(bestVertex * 4 + 4)];
                    for (int k = 0; k < outline.VerticesCount; k++)
                    {
                        if (InCone(k, outline, corner))
                        {
                            int dx = outline.Vertices[k * 4] - corner[0];
                            int dz = outline.Vertices[k * 4 + 2] - corner[2];
                            diagonals[diagonalsCount].vertex = k;
                            diagonals[diagonalsCount].distance = dx * dx + dz * dz;
                            diagonalsCount++;
                        }
                    }

                    Array.Sort(diagonals, new DiagonalComparer());

                    index = -1;
                    for (int k = 0; k < diagonalsCount; k++)
                    {
                        int[] pt = outline.Vertices[(diagonals[k].vertex * 4)..(diagonals[k].vertex * 4 + 4)];
                        bool intersect = IntersectSegmentContour(pt, corner, diagonals[i].vertex, outline.VerticesCount, outline.Vertices);
                        for (int l = i; l < region.holesCount && !intersect; l++)
                        {
                            intersect |= IntersectSegmentContour(pt, corner, -1, region.Holes[l].Contour.VerticesCount, region.Holes[l].Contour.Vertices);
                        }

                        if (!intersect)
                        {
                            index = diagonals[k].vertex;
                            break;
                        }
                    }

                    if (index != -1) break;

                    bestVertex = (bestVertex + 1) % hole.VerticesCount;
                }

                if (index != -1)
                {
                    MergeContours(region.Outline, hole, index, bestVertex);
                }
            }
        }

        private void MergeContours(Contour outline, Contour hole, int ia, int ib)
        {
            int verticesCount = outline.VerticesCount + hole.VerticesCount + 2;
            int index = 0;
            int[] vertices = new int[verticesCount * 4];
            for (int i = 0; i <= outline.VerticesCount; i++)
            {
                int base1 = index * 4;
                int base2 = ((ia + i) % outline.VerticesCount) * 4;

                vertices[base1] = outline.Vertices[base2];
                vertices[base1 + 1] = outline.Vertices[base2 + 1];
                vertices[base1 + 2] = outline.Vertices[base2 + 2];
                vertices[base1 + 3] = outline.Vertices[base2 + 3];
                index++;
            }

            for (int i = 0; i <= hole.VerticesCount; i++)
            {
                int base1 = index * 4;
                int base2 = ((ib + i) % hole.VerticesCount) * 4;
                vertices[base1] = hole.Vertices[base2];
                vertices[base1 + 1] = hole.Vertices[base2 + 1];
                vertices[base1 + 2] = hole.Vertices[base2 + 2];
                vertices[base1 + 3] = hole.Vertices[base2 + 3];
                index++;
            }

            outline.Vertices = vertices;
            hole.Vertices = new int[0];
        }

        private bool IntersectSegmentContour(int[] d0, int[] d1, int i, int n, int[] vertex)
        {
            // For each edge (k,k+1) of P
            for (int k = 0; k < n; k++)
            {
                int k1 = (k + 1) % n;
                // Skip edges incident to i.
                if (i == k || i == k1)
                    continue;
                int[] p0 = vertex[(k * 4)..(k * 4 + 4)];
                int[] p1 = vertex[(k1 * 4)..(k1 * 4 + 4)];
                if ((d0[0] == p0[0] && d0[2] == p0[2])
                    || (d1[0] == p0[0] && d1[2] == p0[2])
                    || (d0[0] == p1[0] && d0[2] == p1[2])
                    || (d1[0] == p1[0] && d1[2] == p1[2]))
                    continue;

                if (CalculateGeometry.Intersect(d0, d1, p0, p1))
                    return true;
            }
            return false;
        }

        private bool InCone(int i, Contour contour, int[] corner)
        {
            int[] vertex = contour.Vertices[(i * 4)..(i * 4 + 4)];
            int j = (i + 1) % contour.VerticesCount;
            int k = (i - 1) > 0 ? i - 1 : contour.VerticesCount - 1;
            int[] next = contour.Vertices[(j * 4)..(j * 4 + 4)];
            int[] prev = contour.Vertices[(k * 4)..(k * 4 + 4)];

            // vertex is a convex
            if (!CalculateGeometry.Left(prev, vertex, next))
                return CalculateGeometry.Left(vertex, corner, next)
                            && CalculateGeometry.Left(corner, vertex, prev);
            // reflex
            return !(CalculateGeometry.LeftOn(vertex, corner, prev)
                        && CalculateGeometry.LeftOn(corner, vertex, next));
        }

        internal class HoleComparer : IComparer
        {
            public int Compare(object x, object y)
            {
                ContourHole a = (ContourHole)x;
                ContourHole b = (ContourHole)y;
                if (a.MinX == b.MinX)
                {
                    if (a.MinZ < b.MinZ)
                        return -1;
                    if (a.MinZ > b.MinZ)
                        return 1;
                }
                else
                {
                    if (a.MinX < b.MinX)
                        return -1;
                    if (a.MinX > b.MinX)
                        return 1;
                }
                return 0;
            }
        }

        internal class DiagonalComparer : IComparer
        {
            public int Compare(object x, object y)
            {
                PotentialDiagonal a = (PotentialDiagonal)x;
                PotentialDiagonal b = (PotentialDiagonal)y;
                if (a.distance < b.distance)
                    return -1;
                if (a.distance > b.distance)
                    return 1;
                return 0;
            }
        }
    }
}