using Navigation.Flags;
using Navigation.PipelineData;
using Navigation.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Logger = Navigation.Utilities.Logger;

namespace Navigation.Pipeline
{
    public class TriangleMeshBuilder : INavMeshPipeline
    {
        private const int UNDEFINED = -1;
        private const int HULL = -2;
        private const int MAX_VERTICES = 127; // 一个Poly中最多的采样点数
        private const int MAX_VERTICES_PRE_EDGE = 32; // 边采样时一条边最大的采样数
        private const int MAX_TRIANGLES = 255; // delaunay 过程最大的三角面数 
        private DataSet _data;

        #region 快捷访问
        private Tile _tile => _data.CurrentTile;
        private ContourSet _contourSet => _data.ContourSet;
        private CompactHeightField _chf => _data.CompactHeightField;
        private PolyMeshField _pmf => _data.PolyMeshField;
        private TriangleMesh _tm => _data.TriangleMesh;
        private float _cellSize => _data.NavMeshPreference.CellSize;
        private float _cellHeight => _data.NavMeshPreference.CellHeight;
        private int _regionCount => _data.RegionCount;
        private int _maxEdgeError => _data.NavMeshPreference.MaxEdgeError;
        private int _verticesPerPoly => _data.NavMeshPreference.VerticesPerPoly;
        private float _sampleDistance => (float)_data.NavMeshPreference.SampleDistance;
        private int _maxSampleError => _data.NavMeshPreference.MaxSampleError;
        private Logger _logger => _data.Logger;
        #endregion

        private int _voxelXNum, _voxelYNum, _voxelZNum;
        private float _inverseCellSize;

        public void Process(DataSet data)
        {
            _data = data;
            _logger.Info("Start Build TriangleMesh(Poly details)");
            Initialize();

            BuildTriangleMesh();

            _logger.Info($"Summary: TriangleMesh Count: {_tm.Regions.Length}");
            _logger.Info("Build TriangleMesh Finished");
        }

        private void Initialize()
        {
            _voxelXNum = Mathf.CeilToInt(_tile.Width / _cellSize);
            _voxelYNum = Mathf.CeilToInt(_tile.Height / _cellHeight);
            _voxelZNum = Mathf.CeilToInt(_tile.Depth / _cellSize);

            _inverseCellSize = 1.0f / _cellSize;

            _tm.Initialize(_tile);
        }

        private void BuildTriangleMesh()
        {
            int sourcePolyCount = _pmf.RegionIndices.Length;

            float[] minBounds = new float[3] { _tile.Min.x, _tile.Min.y, _tile.Min.z };

            // 记录每个多边形的 Bounds
            // 每个poly占用4个int，分别是 minX, maxX, minZ, maxZ
            int[] polyXZBounds = new int[sourcePolyCount * 4];
            int totalPolyVerticesCount = 0;

            int maxPolyWidth = 0;
            int maxPolyDepth = 0;

            for (int i = 0; i < sourcePolyCount; i++)
            {
                // 在 _pmf.Polygons 里面，每个 poly 占用长度 2 * _verticesPerPoly 
                // 一组是自己的顶点，一组和别的Poly相连的顶点
                int polyBase = i * _verticesPerPoly * 2;
                int minXIndex = i * 4;
                int maxXIndex = i * 4 + 1;
                int minZIndex = i * 4 + 2;
                int maxZIndex = i * 4 + 3;

                polyXZBounds[minXIndex] = _voxelXNum;
                polyXZBounds[maxXIndex] = 0;
                polyXZBounds[minZIndex] = _voxelZNum;
                polyXZBounds[maxZIndex] = 0;

                // 构建 polyXZBounds
                for (int offset = 0; offset < _verticesPerPoly; offset++)
                {
                    if (_pmf.Polygons[polyBase + offset] == -1)
                    {
                        break;
                    }

                    int vertexIndex = _pmf.Polygons[polyBase + offset] * 3;
                    polyXZBounds[minXIndex] = Mathf.Min(polyXZBounds[minXIndex], _pmf.Vertices[vertexIndex]);
                    polyXZBounds[maxXIndex] = Mathf.Max(polyXZBounds[maxXIndex], _pmf.Vertices[vertexIndex]);
                    polyXZBounds[minZIndex] = Mathf.Min(polyXZBounds[minZIndex], _pmf.Vertices[vertexIndex + 2]);
                    polyXZBounds[maxZIndex] = Mathf.Max(polyXZBounds[maxZIndex], _pmf.Vertices[vertexIndex + 2]);

                    totalPolyVerticesCount++;
                }

                // QUESTION: 为什么min要 - 1， max要 + 1 
                polyXZBounds[minXIndex] = Mathf.Max(0, polyXZBounds[minXIndex] - 1);
                polyXZBounds[maxXIndex] = Mathf.Min(_voxelXNum, polyXZBounds[maxXIndex] + 1);
                polyXZBounds[minZIndex] = Mathf.Max(0, polyXZBounds[minZIndex] - 1);
                polyXZBounds[maxZIndex] = Mathf.Min(_voxelZNum, polyXZBounds[maxZIndex] + 1);

                if (polyXZBounds[minXIndex] >= polyXZBounds[maxXIndex]
                    || polyXZBounds[minZIndex] >= polyXZBounds[maxZIndex])
                {
                    continue;
                }

                // 获取高度Patch最大的长宽
                maxPolyWidth = Mathf.Max(maxPolyWidth, polyXZBounds[maxXIndex] - polyXZBounds[minXIndex]);
                maxPolyDepth = Mathf.Max(maxPolyDepth, polyXZBounds[maxZIndex] - polyXZBounds[minZIndex]);
            }

            // (x,y,z) 为一组，存储顶点相对Tile Min的坐标 
            float[] polyVertices = new float[_verticesPerPoly * 3];
            int polyVerticesCount = 0;

            HeightPatch hp = new HeightPatch(maxPolyWidth * maxPolyDepth);

            // 求 HeightPatch时所用的数据
            Queue<int> workingIndexQueue = new Queue<int>(256);
            Queue<CompactHeightSpan> workingSpanQueue = new Queue<CompactHeightSpan>(128);
            int[] workingWidthDepth = new int[2];

            // BuildPolyDetails 时所用的数据
            List<int> polyTriangles = new List<int>(_verticesPerPoly * 2 * 3); // poly生成高度细节后的全部三角面
            float[] polyTriangleVertices = new float[MAX_VERTICES * 3]; // poly生成高度细节后全部的顶点
            int polyTriangleVerticesCount = 0;
            List<int> workingEdges = new List<int>(MAX_VERTICES_PRE_EDGE * 4);
            List<int> workingSamples = new List<int>(512);

            // 每组 (x,y,z)
            List<float> globalVertices = new List<float>(totalPolyVerticesCount * 2 * 3);

            // (vertexAIndex, vertexBIndex, vertexCIndex, regionID)
            List<int> globalTriangles = new List<int>(totalPolyVerticesCount * 2 * 4);

            for (int i = 0; i < sourcePolyCount; i++)
            {
                int polyBase = i * _verticesPerPoly * 2;

                polyVerticesCount = 0;
                for (int offset = 0; offset < _verticesPerPoly; offset++)
                {
                    if (_pmf.Polygons[polyBase + offset] == -1)
                    {
                        break;
                    }

                    // 以xyz为一组数据,所以取出来的索引需要 * 3 
                    int vertexIndex = _pmf.Polygons[polyBase + offset] * 3;

                    // 记录 x，z是记录的左下角，但是 y 是记录的上表面
                    polyVertices[offset * 3 + 0] = _pmf.Vertices[vertexIndex] * _cellSize;
                    polyVertices[offset * 3 + 1] = _pmf.Vertices[vertexIndex + 1] * _cellHeight + _cellHeight;
                    polyVertices[offset * 3 + 2] = _pmf.Vertices[vertexIndex + 2] * _cellSize;

                    polyVerticesCount++;
                }

                // 初始化HeightPatch
                hp.MinWidthIndex = polyXZBounds[i * 4];
                hp.MinDepthIndex = polyXZBounds[i * 4 + 2];
                hp.Width = polyXZBounds[i * 4 + 1] - polyXZBounds[i * 4 + 0];
                hp.Depth = polyXZBounds[i * 4 + 3] - polyXZBounds[i * 4 + 2];
                GetHeightData(_pmf.RegionIndices[i], polyBase, polyVerticesCount, hp, workingIndexQueue, workingSpanQueue, workingWidthDepth);

                polyTriangleVerticesCount = BuildPolyDetails(polyVertices, polyVerticesCount,
                                                hp, polyTriangleVertices, polyTriangles,
                                                workingEdges, workingSamples);

                if (polyTriangleVerticesCount < 3)
                {
                    _logger.Error($"[TriangleMeshBuilder - BuildTriangleMesh] Build polygon details failed, the poly {_pmf.RegionIndices[i]} get {polyTriangleVerticesCount} triangle vertices!");
                    continue;
                }

                // 提前扩容
                globalVertices.Capacity = Math.Max(globalVertices.Capacity,
                                            globalVertices.Count + polyTriangleVerticesCount * 3);
                globalTriangles.Capacity = Math.Max(globalTriangles.Capacity,
                                             globalTriangles.Count + polyTriangles.Count * 4 / 3);


                int indexOffset = globalVertices.Count / 3; //原来所有三角形的末尾
                for (int vertexIndex = 0; vertexIndex < polyTriangleVerticesCount; vertexIndex++)
                {
                    //高度域的坐标转换为世界坐标
                    int baseIndex = vertexIndex * 3;
                    globalVertices.Add(polyTriangleVertices[baseIndex] + minBounds[0]);
                    globalVertices.Add(polyTriangleVertices[baseIndex + 1] + minBounds[1]);
                    globalVertices.Add(polyTriangleVertices[baseIndex + 2] + minBounds[2]);
                }


                for (int triangleIndex = 0; triangleIndex < polyTriangles.Count; triangleIndex += 4)
                {
                    globalTriangles.Add(polyTriangles[triangleIndex] + indexOffset);
                    globalTriangles.Add(polyTriangles[triangleIndex + 1] + indexOffset);
                    globalTriangles.Add(polyTriangles[triangleIndex + 2] + indexOffset);
                    globalTriangles.Add(_pmf.RegionIndices[i]);
                }
            }

            _tm.Vertices = globalVertices.ToArray();

            _tm.Indices = new int[globalTriangles.Count * 3 / 4];
            int triangleCount = globalTriangles.Count / 4;
            _tm.Regions = new int[triangleCount];

            //因为 Region 信息要单独存
            for (int i = 0; i < triangleCount; i++)
            {
                int sourcePointer = i * 4;
                int destinationPointer = i * 3;
                _tm.Indices[destinationPointer] = globalTriangles[sourcePointer];
                _tm.Indices[destinationPointer + 1] = globalTriangles[sourcePointer + 1];
                _tm.Indices[destinationPointer + 2] = globalTriangles[sourcePointer + 2];
                _tm.Regions[i] = globalTriangles[sourcePointer + 3];
            }
        }

        private void GetHeightData(int region, int polyBase, int verticesCount, HeightPatch hp, Queue<int> gridIndexQueue,
                                    Queue<CompactHeightSpan> spanQueue, int[] widthDepth)
        {
            hp.ResetData();
            gridIndexQueue.Clear();
            spanQueue.Clear();

            for (int z = hp.MinDepthIndex; z < hp.MinDepthIndex + hp.Depth; z++)
            {
                for (int x = hp.MinWidthIndex; x < hp.MinWidthIndex + hp.Width; x++)
                {
                    var cell = _chf.Cells[x + z * _voxelXNum];
                    if (cell.Count == 0) continue;

                    for (uint spanIndex = cell.FirstSpan; spanIndex < cell.FirstSpan + cell.Count; spanIndex++)
                    {
                        var span = _chf.Spans[spanIndex];
                        if (span.RegionId == region)
                        {
                            hp[x, z] = span.Floor;
                            gridIndexQueue.Enqueue(x);
                            gridIndexQueue.Enqueue(z);
                            spanQueue.Enqueue(span);
                        }
                    }
                }
            }

            while (spanQueue.Count > 0)
            {
                int widthIndex = gridIndexQueue.Dequeue();
                int depthIndex = gridIndexQueue.Dequeue();
                CompactHeightSpan span = spanQueue.Dequeue();

                // bfs
                for (int dir = 0; dir < 4; dir++)
                {
                    CompactHeightSpan nSpan = span.Neighbors[dir] != uint.MaxValue
                                            ? _chf.Spans[span.Neighbors[dir]]
                                            : null;

                    if (nSpan == null)
                    {
                        continue;
                    }

                    int nWidthIndex = widthIndex + CalculateGeometry.DirectionX[dir];
                    int nDepthIndex = depthIndex + CalculateGeometry.DirectionZ[dir];

                    if (!hp.InPatch(nWidthIndex, nDepthIndex))
                    {
                        continue;
                    }

                    if (hp[nWidthIndex, nDepthIndex] == HeightPatch.UNSET)
                    {
                        hp[nWidthIndex, nDepthIndex] = nSpan.Floor;

                        gridIndexQueue.Enqueue(nWidthIndex);
                        gridIndexQueue.Enqueue(nDepthIndex);
                        spanQueue.Enqueue(nSpan);
                    }
                }
            }
        }

        /// <summary>
        /// 处理Poly，构建高度细节
        /// </summary>
        /// <param name="sourcePolyVertices">当前Poly的全部顶点，相对Tile.Min的世界坐标</param>
        /// <param name="sourceVerticesCount">当前Poly的顶点数</param>
        /// <param name="hp">HeightPatch</param>
        /// <param name="outVertices">当前Poly经过边采样和面采样后全部的顶点</param>
        /// <param name="outTriangles">当前Poly处理内部高度细节后细分得到的全部三角形</param>
        /// <param name="workingEdges"></param>
        /// <param name="workingSamples"></param>
        /// <returns></returns>
        private int BuildPolyDetails(float[] sourcePolyVertices, int sourceVerticesCount, HeightPatch hp, float[] outVertices,
                                        List<int> outTriangles, List<int> workingEdges, List<int> workingSamples)
        {
            // 用于记录边采样时，采样点的数据
            float[] workingVertices = new float[(MAX_VERTICES_PRE_EDGE + 1) * 3];
            int[] workingIndices = new int[MAX_VERTICES_PRE_EDGE];
            int workingIndicesCount = 0;

            // 记录Poly的顶点索引，不过仅记录边上的，即Poly原顶点 + 边采样的顶点
            // 其实就是描述了一个凸包，所以命名为 Hull， 注意这里面不包含面采样添加的内部顶点
            int[] hullIndices = new int[MAX_VERTICES];
            int hullIndicesCount = 0;

            Array.Copy(sourcePolyVertices, 0, outVertices, 0, sourceVerticesCount * 3);
            int outVertexCount = sourceVerticesCount;

            float minExtend = PolyMinExtend(sourcePolyVertices, sourceVerticesCount);

            // 边采样
            if (_sampleDistance > 0)
            {
                for (int vertexBIndex = 0, vertexAIndex = sourceVerticesCount - 1;
                        vertexBIndex < sourceVerticesCount;
                        vertexAIndex = vertexBIndex++)
                {
                    int vertexA = vertexAIndex * 3;
                    int vertexB = vertexBIndex * 3;
                    bool swapped = false;

                    // 交换A，B，让A，B满足 A.x <= B.x, x相同时 A.z <= B.z
                    if (Mathf.Abs(sourcePolyVertices[vertexA] - sourcePolyVertices[vertexB]) < 1e-6)
                    {
                        if (sourcePolyVertices[vertexA + 2] > sourcePolyVertices[vertexB + 2])
                        {
                            vertexA = vertexBIndex * 3;
                            vertexB = vertexAIndex * 3;
                            swapped = true;
                        }
                    }
                    else if (sourcePolyVertices[vertexA] > sourcePolyVertices[vertexB])
                    {
                        vertexA = vertexBIndex * 3;
                        vertexB = vertexAIndex * 3;
                        swapped = true;
                    }

                    // deltaX 一定 >= 0，因为前面的顺序保证了这一点
                    float deltaX = sourcePolyVertices[vertexB] - sourcePolyVertices[vertexA];

                    // deltaZ可能会为负，当 deltaX == 0 时，deltaZ才一定 >= 0
                    float deltaZ = sourcePolyVertices[vertexB + 2] - sourcePolyVertices[vertexA + 2];

                    float edgeXZLength = Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ);

                    // 可以分成多少份来采样
                    int maxEdgeCount = 1 + (int)Mathf.Floor(edgeXZLength / _sampleDistance);
                    maxEdgeCount = Mathf.Min(maxEdgeCount, MAX_VERTICES_PRE_EDGE);
                    if (maxEdgeCount + outVertexCount >= MAX_VERTICES)
                    {
                        maxEdgeCount = MAX_VERTICES - 1 - outVertexCount;
                    }

                    // 将 A -> B 这条边中的全部采样点添加进去
                    for (int edgeVertexIndex = 0; edgeVertexIndex <= maxEdgeCount; edgeVertexIndex++)
                    {
                        float percentOffset = (float)edgeVertexIndex / maxEdgeCount;
                        int edgeVertex = edgeVertexIndex * 3;
                        workingVertices[edgeVertex] = sourcePolyVertices[vertexA] + (deltaX * percentOffset);
                        workingVertices[edgeVertex + 2] = sourcePolyVertices[vertexA + 2] + (deltaZ * percentOffset);
                        workingVertices[edgeVertex + 1] = getHeightWithinField(workingVertices[edgeVertex],
                                                            workingVertices[edgeVertex + 2], hp) * _cellHeight;
                    }

                    // 后面的流程其实与Simplify Contour的思路是一样的
                    workingIndices[0] = 0;
                    workingIndices[1] = maxEdgeCount;
                    workingIndicesCount = 2;

                    for (int index = 0; index < workingIndicesCount - 1;)
                    {
                        // 从workingIndices中取出一对起始点和结束点, 后面就对start -> end这一段进行细分
                        int startVertexIndex = workingIndices[index];
                        int endVertexIndex = workingIndices[index + 1];
                        int startVertex = startVertexIndex * 3;
                        int endVertex = endVertexIndex * 3;

                        float maxDistance_squared = 0;
                        int maxDistanceVertex = -1;

                        // 遍历采样点
                        for (int testIndex = startVertexIndex + 1; testIndex < endVertexIndex; testIndex++)
                        {
                            int testVertex = testIndex * 3;

                            // 找到距离start -> end最远的点，不过这里算的是欧氏距离，而不是Span数量
                            // 记录下最远的点，在之后插入到边中
                            float distance_square = CalculateGeometry.PointSegmentDistance_Squared
                                                        (
                                                            workingVertices[testVertex],
                                                            workingVertices[testVertex + 1],
                                                            workingVertices[testVertex + 2],
                                                            workingVertices[startVertex],
                                                            workingVertices[startVertex + 1],
                                                            workingVertices[startVertex + 2],
                                                            workingVertices[endVertex],
                                                            workingVertices[endVertex + 1],
                                                            workingVertices[endVertex + 2]
                                                        );

                            if (distance_square > maxDistance_squared)
                            {
                                maxDistance_squared = distance_square;
                                maxDistanceVertex = testIndex;
                            }
                        }

                        // 找到了一个待插入点，并且这个点距离边的距离超过了最大允许的距离，那么就插入这个点
                        if (maxDistanceVertex != -1 && maxDistance_squared > _maxSampleError * _maxSampleError)
                        {
                            // 插入到 index 后面，因为是数组，所以要整体后移
                            for (int i = workingIndicesCount; i > index; i--)
                            {
                                workingIndices[i] = workingIndices[i - 1];
                            }

                            workingIndices[index + 1] = maxDistanceVertex;
                            workingIndicesCount++;
                        }
                        else
                        {
                            index++;
                        }
                    }

                    hullIndices[hullIndicesCount++] = vertexAIndex;
                    if (swapped)
                    {
                        for (int i = workingIndicesCount - 2; i > 0; i--)
                        {
                            int index = outVertexCount * 3;

                            outVertices[index] = workingVertices[workingIndices[i] * 3];
                            outVertices[index + 1] = workingVertices[workingIndices[i] * 3 + 1];
                            outVertices[index + 2] = workingVertices[workingIndices[i] * 3 + 2];
                            hullIndices[hullIndicesCount++] = outVertexCount;
                            outVertexCount++;
                        }
                    }
                    else
                    {
                        for (int i = 1; i < workingIndicesCount - 1; i++)
                        {
                            int index = outVertexCount * 3;
                            outVertices[index] = workingVertices[workingIndices[i] * 3];
                            outVertices[index + 1] = workingVertices[workingIndices[i] * 3 + 1];
                            outVertices[index + 2] = workingVertices[workingIndices[i] * 3 + 2];
                            hullIndices[hullIndicesCount++] = outVertexCount;
                            outVertexCount++;
                        }
                    }
                }
            }

            outTriangles.Clear();
            TriangulateHull(outVertices, sourceVerticesCount,
                                        hullIndices, hullIndicesCount,
                                        outTriangles);

            // 如果Poly的面最小范围较小（薄片或小三角形），就不进行面采样再在内部添加点了。
            if (minExtend < _sampleDistance * 2)
            {
                return outVertexCount;
            }

            if (outTriangles.Count == 0)
            {
                _logger.Warning("[TriangleMeshBuilder - BuildPolyDetails] Could not triangulate polygon");
                return outVertexCount;
            }

            // 面采样
            if (_sampleDistance > 0)
            {
                float[] minVertex = { sourcePolyVertices[0], sourcePolyVertices[1], sourcePolyVertices[2] };
                float[] maxVertex = { minVertex[0], minVertex[1], minVertex[2] };

                // 获取 sourcePoly 的 Bounds，基于真正的坐标值 
                for (int i = 1; i < sourceVerticesCount; i++)
                {
                    int vertex = i * 3;
                    for (int j = 0; j < 3; j++)
                    {
                        minVertex[j] = Mathf.Min(minVertex[j], sourcePolyVertices[vertex + j]);
                        maxVertex[j] = Mathf.Max(maxVertex[j], sourcePolyVertices[vertex + j]);
                    }
                }

                // 基于 _sampleDistance，将 Bounds 区域转换成一个Grid
                // 就像之前体素化一样，在Gird节点中进行面采样
                int x0 = (int)Mathf.Floor(minVertex[0] / _sampleDistance);
                int z0 = (int)Mathf.Floor(minVertex[2] / _sampleDistance);
                int x1 = (int)Mathf.Ceil(maxVertex[0] / _sampleDistance);
                int z1 = (int)Mathf.Ceil(maxVertex[2] / _sampleDistance);

                workingSamples.Clear();
                for (int z = z0; z < z1; z++)
                {
                    for (int x = x0; x < x1; x++)
                    {
                        float[] point =
                        {
                            x * _sampleDistance,
                            (minVertex[1] + maxVertex[1]) * 0.5f,
                            z * _sampleDistance
                        };

                        // 排除掉在多边形外部的采样点
                        // 正值代表在多边形外，而比 -_sampleDistance/2还要大的值，表示非常靠近边缘，
                        // 靠近边缘的高度细节我们希望利用边采样在上一步解决，
                        // 所以这里忽略掉太靠近边缘的面采样点。
                        if (PointToPolygonSignedDistance_Squared(point, sourcePolyVertices, sourceVerticesCount) > (-_sampleDistance * 0.5f))
                        {
                            continue;
                        }

                        workingSamples.Add(x);
                        workingSamples.Add(getHeightWithinField(point[0], point[2], hp));
                        workingSamples.Add(z);

                        // 0 表示这个采样点最后没取用，1则表示取用了，在后面的逻辑中就会修改这个
                        workingSamples.Add(0);
                    }
                }

                // 从距离差距最大的的采样点开始添加。
                // 当所有采样点都被添加进去或者所有采样点的最大距离差距在范围内时 (<_maxSampleError) ，该过程将停止。
                int sampleCount = workingSamples.Count / 4;
                for (int iter = 0; iter < sampleCount; iter++)
                {
                    // 一个Poly下的顶点数超过最大限制，强行停止
                    if (outVertexCount >= MAX_VERTICES)
                    {
                        break;
                    }

                    float selectedX = 0;
                    float selectedY = 0;
                    float selectedZ = 0;

                    float maxDistance = 0;
                    int selectedSample = -1;

                    for (int sampleVertexIndex = 0; sampleVertexIndex < sampleCount; sampleVertexIndex++)
                    {
                        int sampleVertex = sampleVertexIndex * 4;

                        // 已经被使用了
                        if (workingSamples[sampleVertex + 3] != 0)
                            continue;

                        float sampleX = workingSamples[sampleVertex] * _sampleDistance + getJitterX(sampleVertexIndex) * _cellSize * 0.1f;
                        float sampleY = workingSamples[sampleVertex + 1] * _cellHeight;
                        float sampleZ = workingSamples[sampleVertex + 2] * _sampleDistance + getJitterZ(sampleVertexIndex) * _cellSize * 0.1f;

                        float sampleDistance = InternalPointToMeshDistance(sampleX, sampleY, sampleZ,
                                                                            outVertices, outTriangles);
                        if (sampleDistance < 0) // 未接触 Mesh
                            continue;
                        if (sampleDistance > maxDistance)
                        {
                            maxDistance = sampleDistance;
                            selectedSample = sampleVertexIndex;
                            selectedX = sampleX;
                            selectedY = sampleY;
                            selectedZ = sampleZ;
                        }
                    }

                    if (maxDistance <= _maxSampleError || selectedSample == -1)
                    {
                        break;
                    }

                    // 标记这个采样点被使用了
                    workingSamples[selectedSample * 4 + 3] = 1;

                    // 将采用的采样点添加到Poly的顶点集中
                    int newVertex = outVertexCount * 3;
                    outVertices[newVertex] = selectedX;
                    outVertices[newVertex + 1] = selectedY;
                    outVertices[newVertex + 2] = selectedZ;
                    outVertexCount++;

                    /*
                     * 每次选中一个采样点后，都要重新计算一般三角剖分结果
                     * 为什么不是全部采样点都选取后，只计算一遍三角剖分呢？
                     * 假设我们的地形是中间隆起来的，然后凸包是周围一圈平地，那么中部的全部采样点都会被选中
                     * 这样总体算下来新增的采样点过多了。
                     * 1 1 1 1 1 1 1  
                     * 1 2 2 2 2 2 1  
                     * 1 2 2 2 2 2 1   例如左侧这个例子，若当前凸包是周围一圈高度为 1 的 
                     * 1 2 2 2 2 2 1   _maxSampleError = 1
                     * 1 2 2 2 2 2 1   那么所有落在 2 的采样点都会因为距离 >= _maxSampleError 而被选中
                     * 1 2 2 2 2 2 1   但是其实我们只需要选择靠近边角的四个采样点即可
                     * 1 1 1 1 1 1 1
                     * 
                     * 1 1 1 1 1 1 1
                     * 1 2 2 2 2 2 1
                     * 1 2 A B C 2 1   例如左侧，A, B, C, D, E, F, G, H, I 是九个采样点
                     * 1 2 D E F 2 1   但是我们只要选择了 A, C, G, I四个采样点，就足够了
                     * 1 2 G H I 2 1   这就是为啥每选择一个采样点都需要跑一边三角剖分的原因
                     * 1 2 2 2 2 2 1
                     * 1 1 1 1 1 1 1
                     *
                     * 但是完整的跑一边三角剖分仍然存在比较大的性能问题， 如果我们可以使用上次的
                     * 剖分结果做一个增量更新就好了
                     */
                    DelaunayHull(outVertices, outVertexCount, hullIndices, hullIndicesCount, workingEdges, outTriangles);
                }
            }

            return outVertexCount;
        }

        private float getJitterX(int i)
        {
            return (((i * 0x8da6b343) & 0xffff) / 65535.0f * 2.0f) - 1.0f;
        }

        private float getJitterZ(int i)
        {
            return (((i * 0xd8163841) & 0xffff) / 65535.0f * 2.0f) - 1.0f;
        }

        private float PolyMinExtend(float[] vertices, int verticesCount)
        {
            float minDistance = float.MinValue;
            for (int i = 0; i < verticesCount; i++)
            {
                int j = (i + 1) % verticesCount;

                float maxEdgeDistance = 0;
                for (int k = 0; k < verticesCount; k++)
                {
                    if (k == i || k == j)
                    {
                        continue;
                    }

                    float distance = CalculateGeometry.PointSegmentDistance_Squared
                                        (
                                            vertices[k * 3],
                                            vertices[k * 3 + 2],
                                            vertices[i * 3],
                                            vertices[i * 3 + 2],
                                            vertices[j * 3],
                                            vertices[j * 3 + 2]
                                        );
                    maxEdgeDistance = Mathf.Max(maxEdgeDistance, distance);
                }
            }

            return Mathf.Sqrt(minDistance);
        }

        /// <summary>
        /// 三角形化经过边采样后的凸多边形
        /// </summary>
        /// <param name="vertices">边采样后Poly的全部顶点</param>
        /// <param name="sourceCount">Poly原始顶点数目</param>
        /// <param name="hull">当前Poly的顶点索引</param>
        /// <param name="hullVertexCount">当前Poly的顶点数量</param>
        /// <param name="outTriangles"></param>
        private void TriangulateHull(float[] vertices, int sourceCount, int[] hull, int hullVertexCount, List<int> outTriangles)
        {
            int start = 0, left = 1, right = hullVertexCount - 1;

            /*
             * 将凸包三角形化，
             * 1. 先在凸包中枚举三角形的顶点，找到周长最小的那个三角形（TriA）
             * 2. 将 TriA 这个三角形切掉
             * 3. 以 TriA 还留在凸包中的两个顶点为新的三角形顶点，比较这两个三角形周长，取周长最小的那个三角形（TriB）
             * 4. 将 TriB 这个三角形切掉
             * 5. 反复重复 3、4 步骤，直到凸包中只剩下三个顶点
             */

            float minDistance = float.MaxValue;
            for (int i = 0; i < hullVertexCount; i++)
            {
                /*
                 * 这里continue是因为，大于sourceCount的 hull[i] 顶点是边采样时新增的
                 * 而新增的这些点是不能作为三角形化的起点的（想想看，它们成不了三角形）
                 *  A-----B------C------D
                 *   \                 /  
                 *     \            /
                 *       \       /
                 *         \  /
                 *          E
                 * 例如上面这个三角形，B、C、D是边采样新增的点，很明显，A、E、D是可以作为
                 * 一个三角形的顶点，例如 A -> ABE， D -> CDE
                 * 但是 B 显然是不可以的， 因为 ABC 并不是一个三角形
                 * 所以我们最开始找 TriA的时候，是要跳过这些新增的点的
                 */
                if (hull[i] >= sourceCount) continue;
                int prevI = (i + hullVertexCount - 1) % hullVertexCount;
                int nextI = (i + 1) % hullVertexCount;

                float[] prevVertex = vertices[(hull[prevI] * 3)..(hull[prevI] * 3 + 3)];
                float[] currentVertex = vertices[(hull[i] * 3)..(hull[i] * 3 + 3)];
                float[] nextVertex = vertices[(hull[nextI] * 3)..(hull[nextI] * 3 + 3)];

                // 求周长，因为这边主要目的是比大小，所以可以不用开方
                float distance = CalculateGeometry.Distance_Squared(prevVertex, currentVertex)
                                    + CalculateGeometry.Distance_Squared(currentVertex, nextVertex)
                                    + CalculateGeometry.Distance_Squared(nextVertex, prevVertex);
                if (distance < minDistance)
                {
                    start = i;
                    left = nextI;
                    right = prevI;
                    minDistance = distance;
                }
            }

            // 添加第一个三角形
            outTriangles.Add(hull[start]);
            outTriangles.Add(hull[left]);
            outTriangles.Add(hull[right]);
            outTriangles.Add(0);

            // 通过向左或向右移动来三角化面，具体取决于哪个三角形的周长较短。
            while ((left + 1) % hullVertexCount != right)
            {
                int nLeft = (left + 1) % hullVertexCount;
                int nRight = (right + hullVertexCount - 1) % hullVertexCount;
                float[] leftVertex = vertices[(hull[left] * 3)..(hull[left] * 3 + 3)];
                float[] rightVertex = vertices[(hull[right] * 3)..(hull[right] * 3 + 3)];
                float[] nLeftVertex = vertices[(hull[nLeft] * 3)..(hull[nLeft] * 3 + 3)];
                float[] nRightVertex = vertices[(hull[nRight] * 3)..(hull[nRight] * 3 + 3)];
                float leftDistance = CalculateGeometry.Distance_Squared(leftVertex, nLeftVertex)
                                        + CalculateGeometry.Distance_Squared(nLeftVertex, rightVertex);
                float rightDistance = CalculateGeometry.Distance_Squared(rightVertex, nRightVertex)
                                        + CalculateGeometry.Distance_Squared(nRightVertex, leftVertex);

                if (leftDistance < rightDistance)
                {
                    outTriangles.Add(hull[left]);
                    outTriangles.Add(hull[nLeft]);
                    outTriangles.Add(hull[right]);
                    outTriangles.Add(0);
                    left = nLeft;
                }
                else
                {
                    outTriangles.Add(hull[left]);
                    outTriangles.Add(hull[nRight]);
                    outTriangles.Add(hull[right]);
                    outTriangles.Add(0);
                    right = nRight;
                }
            }
        }

        private float PointToPolygonSignedDistance_Squared(float[] point, float[] vertices, int verticesCount)
        {
            float minDistance = float.MaxValue;
            bool isInside = false;

            // 采用射线法去判断点是否在多边形内
            for (int vertexB = 0, vertexA = verticesCount - 1; vertexB < verticesCount; vertexA = vertexB++)
            {
                // 以 A - B 为边
                int b = vertexB * 3;
                int a = vertexA * 3;

                // 从 (x, z) 向 x 正方向发出射线，检测是否和 AB 相交
                if (((vertices[b + 2] > point[2]) // a, b的 z 坐标同时大于或小于 z 则说明肯定不相交
                        != (vertices[a + 2] > point[2]))
                        && (point[0] < (vertices[a] - vertices[b])
                                        * (point[2] - vertices[b + 2])
                                        / (vertices[a + 2] - vertices[b + 2])
                                        + vertices[b]))
                {
                    // 奇数次交点，说明在多边形内，偶数次交点，说明在多边形外
                    isInside = !isInside;
                }

                minDistance = Mathf.Min(minDistance,
                                        CalculateGeometry.PointSegmentDistance_Squared
                                        (
                                            point[0], point[2],
                                            vertices[a], vertices[a + 2],
                                            vertices[b], vertices[b + 2]
                                        ));
            }

            return isInside ? -minDistance : minDistance;
        }

        /// <summary>
        /// Delaunay 三角化，用于将面采样后的点集三角化
        /// </summary>
        /// <param name="vertices">面采样后Poly所包含的全部顶点</param>
        /// <param name="verticesCount">面采样后Poly的顶点数</param>
        /// <param name="hull">凸包中的全部顶点索引</param>
        /// <param name="hullVertexCount">凸包中的顶点数</param>
        /// <param name="workingEdges"></param>
        /// <param name="outTriangles"></param>
        private void DelaunayHull(float[] vertices, int verticesCount, int[] hull, int hullVertexCount,
                                    List<int> workingEdges, List<int> outTriangles)
        {
            /*
             * Delaunay Triangulation 算法
             * 可以参考： https://en.wikipedia.org/wiki/Delaunay_triangulation
             * 有点复杂，这里就不详细解释了
             */

            int triangleCount = 0;
            int edgeCount = 0;
            int maxEdges = verticesCount * 10;

            // 四位存一条边，分别是：起点索引，终点索引，左三角形索引，右三角形索引
            // (vertexAIndex, vertexBIndex, leftTriangle, rightTriangle)
            workingEdges.Clear();
            workingEdges.Capacity = maxEdges * 4;

            for (int hullVertexB = 0, hullVertexA = hullVertexCount - 1;
                hullVertexB < hullVertexCount;
                hullVertexA = hullVertexB++)
            {
                edgeCount = AddEdge(workingEdges, edgeCount, maxEdges,
                                    hull[hullVertexA], hull[hullVertexB],
                                    HULL, UNDEFINED);
            }

            int currentEdge = 0;
            while (currentEdge < edgeCount)
            {
                if (workingEdges[currentEdge * 4 + 2] == UNDEFINED)
                    completeTriangle(currentEdge, maxEdges, vertices, verticesCount,
                                                        ref triangleCount, ref edgeCount, workingEdges);

                if (workingEdges[currentEdge * 4 + 3] == UNDEFINED)
                    completeTriangle(currentEdge, maxEdges, vertices, verticesCount,
                                                        ref triangleCount, ref edgeCount, workingEdges);

                currentEdge++;
            }

            // 先全初始化成 -1 然后后面再根据workingEdges赋值
            outTriangles.Clear();
            outTriangles.Capacity = triangleCount * 4;
            for (int i = 0; i < triangleCount * 4; i++)
                outTriangles.Add(UNDEFINED);

            // 枚举每一条边，然后根据边的左右三角形索引，将三角形的三个顶点索引赋值给 outTriangles
            for (int edgeIndex = 0; edgeIndex < edgeCount; edgeIndex++)
            {
                int edge = edgeIndex * 4;
                if (workingEdges[edge + 3] >= 0)
                {
                    int triangle = workingEdges[edge + 3] * 4;
                    if (outTriangles[triangle] == UNDEFINED)
                    {
                        outTriangles[triangle] = workingEdges[edge];
                        outTriangles[triangle + 1] = workingEdges[edge + 1];
                    }
                    else if (outTriangles[triangle] == workingEdges[edge + 1])
                    {
                        outTriangles[triangle + 2] = workingEdges[edge];
                    }
                    else if (outTriangles[triangle + 1] == workingEdges[edge])
                    {
                        outTriangles[triangle + 2] = workingEdges[edge + 1];
                    }
                }

                if (workingEdges[edge + 2] >= 0)
                {
                    int triangle = workingEdges[edge + 2] * 4;
                    if (outTriangles[triangle] == UNDEFINED)
                    {
                        outTriangles[triangle] = workingEdges[edge + 1];
                        outTriangles[triangle + 1] = workingEdges[edge];
                    }
                    else if (outTriangles[triangle] == workingEdges[edge])
                    {
                        outTriangles[triangle + 2] = workingEdges[edge + 1];
                    }
                    else if (outTriangles[triangle + 1] == workingEdges[edge + 1])
                    {
                        outTriangles[triangle + 2] = workingEdges[edge];
                    }
                }
            }

            for (int i = triangleCount - 1; i >= 0; i--)
            {
                int triangle = i * 4;
                if (outTriangles[triangle] == UNDEFINED
                    || outTriangles[triangle + 1] == UNDEFINED
                    || outTriangles[triangle + 2] == UNDEFINED)
                {
                    outTriangles.RemoveRange(triangle, 4);
                }
            }
        }

        private int AddEdge(List<int> workingEdges, int edgeCount, int maxEdges,
                                int hullVertexA, int hullVertexB, int TriangleA, int TriangleB)
        {
            if (edgeCount >= maxEdges)
            {
                _logger.Error($"[TriangleMeshBuilder - AddEdge] There are too many edges. (edgeCount: {edgeCount}, max: {maxEdges})");
            }

            var edge = FindEdge(workingEdges, edgeCount, hullVertexA, hullVertexB);
            if (edge == UNDEFINED)
            {
                workingEdges.Add(hullVertexA);
                workingEdges.Add(hullVertexB);
                workingEdges.Add(TriangleA);
                workingEdges.Add(TriangleB);
                edgeCount++;
            }

            return edgeCount;
        }

        private int FindEdge(List<int> workingEdges, int edgeCount, int hullVertexA, int hullVertexB)
        {
            for (int i = 0; i < edgeCount; i++)
            {
                int edgeIndex = i * 4;
                if ((workingEdges[edgeIndex] == hullVertexA && workingEdges[edgeIndex + 1] == hullVertexB)
                    || (workingEdges[edgeIndex] == hullVertexB && workingEdges[edgeIndex + 1] == hullVertexA))
                {
                    return i;
                }
            }

            return UNDEFINED;
        }

        private float InternalPointToMeshDistance(float px, float py, float pz, float[] vertices, List<int> triangles)
        {
            // TODO： 这个方法还没细看，默认是没bug了，，需要后面再看一下
            float minDistance = float.MaxValue;
            int triangleCount = triangles.Count / 4;

            for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
            {
                int triangle = triangleIndex * 4;  //指向哪个三角形 
                                                   //分别对应三角形的三个顶点
                int vertexA = triangles[triangle] * 3;
                int vertexB = triangles[triangle + 1] * 3;
                int vertexC = triangles[triangle + 2] * 3;

                float distance = float.MaxValue;

                /*  reference: https://zhuanlan.zhihu.com/p/65495373
                 *  
                 *  对于三角形内的任意一点P
                 *  向量AP、AB、AC线性相关，因此可以写成
                 *  AP = u * AB + v * AC
                 *   拆分成点 =>
                 *  A - P = u * (A-B) + v * (A-c)
                 *   求出P点  =>
                 *  P = (1 - u - v) * A + u * B + v * C 
                 *  其中 0 <= u,v <= 1
                 *   
                 *  而这式子，又有点像线性插值的公式  
                 *  
                 *  可以将ABC看成坐标系，A为了原点，基为 AB和AC，这样就构造了一个重心坐标系
                 *  所以知道一点P，可以得到 AP = u * AB + v * AC
                 *  => u * AB + v * AC - AP = 0   
                 *  拆成 x和y轴 =>  u * ABx + v * ACx + PAx = 0  和 u * ABy + v * ACy + PAy = 0 
                 *  => 转换成矩阵：
                 *              [ ABx ]                     [ ABy ]
                 *  [ u , v , 1][ ACx ] = 0 和  [ u , v , 1][ ACy ] = 0 
                 *              [ PAx ]                     [ PAy ]
                 *  实际上寻找重点坐标，就变成了寻找向量(u,v,1)同时垂直于向量(ABx,ACx,PAx) 和 (ABy,ACy,PAy),也就是叉乘             
                 *              
                 *  
                 *  同时可知，有了一点P，可以求u和v的思路：
                 *  xvector = (B_x - A_x, C_x - A_x, A_x - P_x)
                 *  yvector = (B_y - A_y, C_y - A_y, A_y - P_y)
                 *  u = xvector x yvector  (外积的性质，外积的向量同时垂直于两者)
                 *  # 因为我们讨论的是二维的三角形，如果 u 的 z 分量不等于1则说明P点不在三角形内
                 *  
                 */

                //AC
                float deltaACx = vertices[vertexC] - vertices[vertexA];
                float deltaACy = vertices[vertexC + 1] - vertices[vertexA + 1];
                float deltaACz = vertices[vertexC + 2] - vertices[vertexA + 2];

                //AB
                float deltaABx = vertices[vertexB] - vertices[vertexA];
                float deltaABy = vertices[vertexB + 1] - vertices[vertexA + 1];
                float deltaABz = vertices[vertexB + 2] - vertices[vertexA + 2];

                //AP
                float deltaAPx = px - vertices[vertexA];
                float deltaAPz = pz - vertices[vertexA + 2];

                float dotACAC = deltaACx * deltaACx + deltaACx * deltaACx;
                float dotACAB = deltaACx * deltaABx + deltaACz * deltaABz;
                float dotACAP = deltaACx * deltaAPx + deltaACz * deltaAPz;
                float dotABAB = deltaABx * deltaABx + deltaABz * deltaABz;
                float dotABAP = deltaABx * deltaAPx + deltaABz * deltaAPz;

                float inverseDenominator = 1.0f / (dotACAC * dotABAB - dotACAB * dotACAB);
                float u = (dotABAB * dotACAP - dotACAB * dotABAP) * inverseDenominator;
                float v = (dotACAC * dotABAP - dotACAB * dotACAP) * inverseDenominator;

                float tolerane = 1e-4f;
                if (u >= -tolerane
                    && v >= -tolerane
                    && (u + v) <= 1 + tolerane)  //要小于1才是在三角形内
                {
                    //然后再利用插值，求出 y 点的高度
                    float y = vertices[vertexA + 1] + deltaACy * u + deltaABy * v;
                    distance = Mathf.Abs(y - py);
                }

                if (distance < minDistance)
                {
                    minDistance = distance;
                }

            }  // for iTriangle -> triangleCount 

            if (float.MaxValue == minDistance)
            {
                return UNDEFINED;
            }

            return minDistance;
        }

        private void completeTriangle(int currentEdge, int maxEdges, float[] vertices, int verticesCount,
                                        ref int triangleCount, ref int edgeCount, List<int> workingEdges)
        {
            const float EPS = 1e-5f;

            int edgeIndex = currentEdge * 4;

            int vertexAIndex;
            int vertexBIndex;

            if (workingEdges[edgeIndex + 2] == UNDEFINED)
            {
                vertexAIndex = workingEdges[edgeIndex];
                vertexBIndex = workingEdges[edgeIndex + 1];
            }
            else if (workingEdges[currentEdge * 4 + 3] == UNDEFINED)
            {
                vertexAIndex = workingEdges[edgeIndex + 1];
                vertexBIndex = workingEdges[edgeIndex];
            }
            else
            {
                return; // 两边都不是 UNDEFINED，说明这条边已经被处理过了
            }

            int vertexA = vertexAIndex * 3;
            int vertexB = vertexBIndex * 3;

            int selectedIndex = -1;

            // 外接圆圆心 (x, y, z)
            float[] circleCenter = { 0, 0, 0 };
            float radius = -1; // 外接圆半径
            float tolerance = 0.001f;

            for (int index = 0; index < verticesCount; index++)
            {
                if (index == vertexAIndex || index == vertexBIndex)
                {
                    continue;
                }

                int testVertex = index * 3;

                float area = CalculateGeometry.Area2(vertices[vertexA], vertices[vertexA + 2],
                                                        vertices[vertexB], vertices[vertexB + 2],
                                                        vertices[testVertex], vertices[testVertex + 2]);

                //TODO 这里应该想表达的就是 testVertex在 vertexA - vertexB 的左侧
                if (area > EPS)
                {
                    // 证明还没有算过
                    if (radius < 0)
                    {
                        selectedIndex = index;
                        radius = CalculateGeometry.CircumCircle(vertices[vertexA], vertices[vertexA + 2],
                                                                vertices[vertexB], vertices[vertexB + 2],
                                                                vertices[testVertex], vertices[testVertex + 2],
                                                                out circleCenter);

                        continue;
                    }

                    //前面已经算过外接圆了，
                    float distanceToCenter = CalculateGeometry.Distance(circleCenter[0], circleCenter[2],
                                                                vertices[testVertex], vertices[testVertex + 2]);

                    // Delaunay 过程中，要保证每个三角面的外接圆都不覆盖其他节点
                    // 圆心距大于半径，就说明说明这个点在圆外面了，这里引入tolerance，是为了避免浮点误差
                    if (distanceToCenter > radius * (1 + tolerance))
                    {
                        continue;
                    }
                    else if (distanceToCenter < radius * (1 - tolerance))
                    {
                        // 之前生成的外接圆覆盖了当前节点，所以要重新生成外接圆
                        selectedIndex = index;
                        radius = CalculateGeometry.CircumCircle(vertices[vertexA], vertices[vertexA + 2],
                                                                vertices[vertexB], vertices[vertexB + 2],
                                                                vertices[testVertex], vertices[testVertex + 2],
                                                                out circleCenter);
                    }
                    else
                    {
                        // 在 tolerance 误差范围内，进行额外的测试以确保边缘有效。
                        // vertexA - index 和 vertexB - index 不能与 vertexA - selected 或 vertexB - selected 重叠
                        if (OverlapEdges(vertexAIndex, index, vertices, edgeCount, workingEdges)
                            || OverlapEdges(vertexBIndex, index, vertices, edgeCount, workingEdges))
                        {
                            continue;
                        }

                        selectedIndex = index;
                        radius = CalculateGeometry.CircumCircle(vertices[vertexA], vertices[vertexA + 2],
                                                                vertices[vertexB], vertices[vertexB + 2],
                                                                vertices[testVertex], vertices[testVertex + 2],
                                                                out circleCenter);
                    }
                }
            }

            // 如果 vertexA - vertexB 在凸包上，就更新边信息，否则添加新的三角形
            if (selectedIndex != -1)
            {
                // 更新正在完成的边缘的三角面信息。
                updateLeftFace(vertexAIndex, vertexBIndex, triangleCount, currentEdge, workingEdges);

                currentEdge = FindEdge(workingEdges, edgeCount, selectedIndex, vertexAIndex);
                if (currentEdge == UNDEFINED)
                {
                    edgeCount = AddEdge(workingEdges, edgeCount, maxEdges, selectedIndex,
                                            vertexAIndex, triangleCount, UNDEFINED);
                }
                else
                {
                    updateLeftFace(selectedIndex, vertexAIndex, triangleCount, currentEdge, workingEdges);
                }

                currentEdge = FindEdge(workingEdges, edgeCount, vertexBIndex, selectedIndex);
                if (currentEdge == UNDEFINED)
                {
                    edgeCount = AddEdge(workingEdges, edgeCount, maxEdges, vertexBIndex,
                                            selectedIndex, triangleCount, UNDEFINED);
                }
                else
                {
                    updateLeftFace(vertexBIndex, selectedIndex, triangleCount, currentEdge, workingEdges);
                }

                triangleCount++;
            }
            else
            {
                updateLeftFace(vertexAIndex, vertexBIndex, HULL, currentEdge, workingEdges);
            }
        }

        private bool OverlapEdges(int vertexA, int vertexB, float[] vertices, int edgeCount, List<int> workingEdges)
        {
            // 枚举所有非相邻边，看看是否有重叠的情况
            for (int index = 0; index < edgeCount; index++)
            {
                int edgeVertexA = workingEdges[index * 4];
                int edgeVertexB = workingEdges[index * 4 + 1];

                if (edgeVertexA == vertexA || edgeVertexA == vertexB
                    || edgeVertexB == vertexA || edgeVertexB == vertexB)
                {
                    continue;
                }

                // 两条边是否有重叠
                if (CalculateGeometry.SegmentsOverlap(vertices[(edgeVertexA * 3)..(edgeVertexA * 3 + 3)],
                                                        vertices[(edgeVertexB * 3)..(edgeVertexB * 3 + 3)],
                                                        vertices[(vertexA * 3)..(vertexA * 3 + 3)],
                                                        vertices[(vertexB * 3)..(vertexB * 3 + 3)]))
                {
                    return true;
                }
            }

            return false;
        }

        private void updateLeftFace(int startVertex, int endVertex, int triangleIndex, int currentEdge, List<int> workingEdges)
        {
            int edgeIndex = currentEdge * 4;
            if (workingEdges[edgeIndex] == startVertex
                && workingEdges[edgeIndex + 1] == edgeIndex
                && workingEdges[edgeIndex + 2] == UNDEFINED)
            {
                workingEdges[edgeIndex + 2] = triangleIndex;
            }
            else if (workingEdges[edgeIndex] == endVertex
                        && workingEdges[edgeIndex + 1] == startVertex
                        && workingEdges[edgeIndex + 3] == UNDEFINED)
            {
                workingEdges[edgeIndex + 3] = triangleIndex;
            }
        }


        private int getHeightWithinField(float x, float z, HeightPatch hp)
        {
            int widthIndex = (int)Mathf.Floor(x * _inverseCellSize + 0.01f);
            int depthIndex = (int)Mathf.Floor(z * _inverseCellSize + 0.01f);

            int height = hp[widthIndex, depthIndex];
            if (height == HeightPatch.UNSET)
            {
                //使用8邻居来找该端点对应的高度
                int[] neighborOffset = { -1, 0, -1, -1, 0, -1, 1, -1, 1, 0, 1, 1, 0, 1, -1, 1 };
                float minNeighborDistance_Squared = float.MaxValue;
                for (int i = 0; i < neighborOffset.Length; i += 2)
                {
                    int nWidthIndex = widthIndex + neighborOffset[i];
                    int nDepthIndex = depthIndex + neighborOffset[i + 1];

                    if (!hp.InPatch(nWidthIndex, nDepthIndex))
                    {
                        continue;
                    }

                    int nNeighborHeight = hp[nWidthIndex, nDepthIndex];
                    if (HeightPatch.UNSET == nNeighborHeight)
                    {
                        continue;
                    }

                    // 0.5是为了取整
                    // 因为 nWidthIndex和nDepthIndex都已经偏移了原来的x/z了，所以选一个最近距离的作为高度
                    float deltaWidth = (nWidthIndex + 0.5f) * _cellSize - x;
                    float deltaDepth = (nDepthIndex + 0.5f) * _cellSize - z;

                    float neighborDistance_Squared = deltaWidth * deltaWidth + deltaDepth * deltaDepth;
                    if (neighborDistance_Squared < minNeighborDistance_Squared)
                    {
                        height = nNeighborHeight;
                        minNeighborDistance_Squared = neighborDistance_Squared;
                    }
                }
            }

            return height;
        }

        private class HeightPatch
        {
            public const int UNSET = int.MaxValue;

            public int MinWidthIndex;
            public int MinDepthIndex;

            public int Width;
            public int Depth;

            private int[] _data;

            public HeightPatch(int size)
            {
                _data = new int[size];
            }

            public int this[int globalWidthIndex, int globalDepthIndex]
            {
                get
                {
                    return Get(globalWidthIndex, globalDepthIndex);
                }
                set
                {
                    Set(globalWidthIndex, globalDepthIndex, value);
                }
            }

            public bool InPatch(int globalWidthIndex, int globalDepthIndex)
            {
                return (globalWidthIndex >= MinWidthIndex
                    && globalDepthIndex >= MinDepthIndex
                    && globalWidthIndex < MinWidthIndex + Width
                    && globalDepthIndex < MinDepthIndex + Depth);
            }

            public void ResetData()
            {
                if (_data == null)
                {
                    return;
                }

                Array.Fill<int>(_data, UNSET);
            }

            public int Get(int globalWidthIndex, int globalDepthIndex)
            {
                int idx = Mathf.Min(Mathf.Max(globalWidthIndex - MinWidthIndex, 0), Width - 1) * Depth
                    + Mathf.Min(Mathf.Max(globalDepthIndex - MinDepthIndex, 0), Depth - 1);

                return _data[idx];
            }

            public void Set(int globalWidthIndex, int globalDepthIndex, int value)
            {
                _data[(globalWidthIndex - MinWidthIndex) * Depth
                        + globalDepthIndex - MinDepthIndex] = value;
            }
        }
    }
}