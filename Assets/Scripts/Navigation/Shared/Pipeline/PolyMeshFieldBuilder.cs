using Navigation.PipelineData;
using Navigation.Utilities;
using Navigation.Flags;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Logger = Navigation.Utilities.Logger;

namespace Navigation.Pipeline
{

    public class PolyMeshFieldBuilder : INavMeshPipeline
    {
        private const int FLAG = 0x8000000; //最高位用于标记Center 顶点  1e9
        private const int CLEAR_FLAG = 0x0FFFFFF;  //最高位空出来为了消除标记，同时还原顶点。 24位1
        private DataSet _data;

        #region 快捷访问
        private Tile _tile => _data.CurrentTile;
        private ContourSet _contourSet => _data.ContourSet;
        private CompactHeightField _chf => _data.CompactHeightField;
        private PolyMeshField _pmf => _data.PolyMeshField;
        private float _cellSize => _data.NavMeshPreference.CellSize;
        private float _cellHeight => _data.NavMeshPreference.CellHeight;
        private int _regionCount => _data.RegionCount;
        private int _maxEdgeError => _data.NavMeshPreference.MaxEdgeError;
        private int _verticesPerPoly => _data.NavMeshPreference.VerticesPerPoly;
        private Logger _logger => _data.Logger;
        #endregion

        public void Process(DataSet data)
        {
            _data = data;
            _logger.Info("Start Build PolyMeshField");
            Initialize();

            if (_contourSet.Count <= 0)
                return;

            BuildPolyMesh();

            _logger.Info($"Summary: PolyMesh Count: {_pmf.RegionIndices.Length}");
            _logger.Info("Build PolyMeshField Finished");
        }

        private void Initialize()
        {
            _pmf.Initialize(_tile);
        }

        private void BuildPolyMesh()
        {
            int sourceVerticesCount = 0;
            int maxPossiblePolygons = 0;
            int maxVerticesPerContour = 0;

            for (int i = 0; i < _contourSet.Count; i++)
            {
                int count = _contourSet[i].VerticesCount;
                sourceVerticesCount += count;

                //过 n 边形的一个顶点，能把n边形最多分成 n - 2 个三角形
                maxPossiblePolygons += count - 2;
                maxVerticesPerContour = Math.Max(maxVerticesPerContour, count);
            }

            // 顶点数组， 存的是所有的顶点，每个顶点有3个数据，x,y,z
            int[] globalVertices = new int[sourceVerticesCount * 3];
            int globalVertexCount = 0;

            // 所有的Polygon，每个Polygon最多有 _verticesPerPoly 个顶点，用 -1 表示空位
            // 记录的为Polygon的顶点在globalVertices中的索引
            int[] globalPolygons = new int[maxPossiblePolygons * _verticesPerPoly];
            Array.Fill(globalPolygons, -1);

            // 记录Polygon所在的Region
            int[] globalRegions = new int[maxPossiblePolygons];
            // 记录Polygon所在的Area
            AreaMask[] globalAreaMasks = new AreaMask[maxPossiblePolygons];
            int globalPolyCount = 0;

            // 在求 globalVertices 时用于剔除重复点
            Dictionary<(int x, int y, int z), int> vertexIndicesDict = new Dictionary<(int, int, int), int>();

            // 后面计算的时候用，创建在外面每次反复用，避免反复内存分配
            int[] vertexGlobalIndex = new int[maxVerticesPerContour];
            int[] workingPolygons = new int[(maxVerticesPerContour + 1) * _verticesPerPoly];
            List<int> workingIndices = new List<int>(maxVerticesPerContour);
            List<int> workingTriangles = new List<int>(maxVerticesPerContour);

            for (int contourIndex = 0; contourIndex < _contourSet.Count; contourIndex++)
            {
                Contour contour = _contourSet[contourIndex];
                //4个数据一组，(x,y,z,regionID)，最少需要三个顶点
                if (contour.VerticesCount < 3)
                {
                    _logger.Error($"[PolyMeshFieldBuilder][BuildPolyMesh] Bad Contour! Id: {contour.RegionId}");
                    continue;
                }

                workingIndices.Clear();
                for (int i = 0; i < contour.VerticesCount; i++)
                {
                    workingIndices.Add(i);
                }

                //三角剖分
                int triangleCount = Triangulate(contour.Vertices, workingIndices, workingTriangles);
                if (triangleCount <= 0)
                {
                    _logger.Error($"[PolyMeshFieldBuilder - BuildPolyMesh] Triangulate failed, contour {contour.RegionId} was split into {triangleCount} triangles.");
                    triangleCount = -triangleCount;
                }


                for (int i = 0; i < contour.VerticesCount; i++)
                {
                    int vertex = i * 4;
                    var keyTuple = (contour.Vertices[vertex], contour.Vertices[vertex + 1], contour.Vertices[vertex + 2]);

                    //vertIndices 里面储存的是根据顶点xyz hash出来的key，对应的在全局顶点表中的索引
                    //全局顶点表 以 xyz 3个为一组，储存轮廓的顶点
                    int globalVertexIndex = 0;
                    if (!vertexIndicesDict.TryGetValue(keyTuple, out globalVertexIndex))
                    {
                        globalVertexIndex = globalVertexCount;
                        globalVertexCount++;
                        vertexIndicesDict.Add(keyTuple, globalVertexIndex);

                        int newVertexBase = globalVertexIndex * 3;
                        globalVertices[newVertexBase] = contour.Vertices[vertex];
                        globalVertices[newVertexBase + 1] = contour.Vertices[vertex + 1];
                        globalVertices[newVertexBase + 2] = contour.Vertices[vertex + 2];
                    }

                    // 记录 Contour 中顶点的索引，对应的到全局顶点表中索引
                    vertexGlobalIndex[i] = globalVertexIndex;
                }

                Array.Fill(workingPolygons, -1);

                // 从三角形 -> Polygon，先把顶点赋值过去，用于后续合并三角形的Poly得到最终的Polygon
                int workingPolyCount = 0;
                for (int i = 0; i < triangleCount; i++)
                {
                    int polyIndexBase = workingPolyCount * _verticesPerPoly;
                    int triangleIndexBase = i * 3;
                    workingPolygons[polyIndexBase] =
                        vertexGlobalIndex[workingTriangles[triangleIndexBase]];
                    workingPolygons[polyIndexBase + 1] =
                        vertexGlobalIndex[workingTriangles[triangleIndexBase + 1]];
                    workingPolygons[polyIndexBase + 2] =
                        vertexGlobalIndex[workingTriangles[triangleIndexBase + 2]];

                    workingPolyCount++;
                }

                // 合并三角形
                if (_verticesPerPoly > 3)
                {
                    while (true)
                    {
                        int longestMergeEdge = -1;
                        int bestPolyA = -1;
                        int polyAVertex = -1;
                        int bestPolyB = -1;
                        int polyBVertex = -1;

                        for (int polyA = 0; polyA < workingPolyCount - 1; polyA++)
                        {
                            for (int polyB = polyA + 1; polyB < workingPolyCount; polyB++)
                            {
                                // 一个Poly的顶点索引 poly * _verticesPerPoly
                                GetPolyMergeValue(polyA * _verticesPerPoly,
                                    polyB * _verticesPerPoly,
                                    workingPolygons,
                                    globalVertices,
                                    out int[] mergeInfo);

                                if (mergeInfo[0] > longestMergeEdge)
                                {
                                    longestMergeEdge = mergeInfo[0];
                                    bestPolyA = polyA * _verticesPerPoly;
                                    polyAVertex = mergeInfo[1];
                                    bestPolyB = polyB * _verticesPerPoly;
                                    polyBVertex = mergeInfo[2];
                                }
                            }
                        }

                        if (longestMergeEdge <= 0)
                        {
                            break;
                        }

                        int[] mergedPoly = new int[_verticesPerPoly];
                        Array.Fill(mergedPoly, -1);

                        int vertexCountA = GetPolyVertexCount(bestPolyA, workingPolygons);
                        int vertexCountB = GetPolyVertexCount(bestPolyB, workingPolygons);
                        int position = 0;

                        for (int i = 0; i < vertexCountA - 1; i++)
                        {
                            int polyIndex = bestPolyA + ((polyAVertex + 1 + i) % vertexCountA);
                            mergedPoly[position++] = workingPolygons[polyIndex];
                        }

                        for (int i = 0; i < vertexCountB - 1; i++)
                        {
                            int polyIndex = bestPolyB + ((polyBVertex + 1 + i) % vertexCountB);
                            mergedPoly[position++] = workingPolygons[polyIndex];
                        }

                        // 将合并之后的顶点拷到A指定的多边形
                        Array.Copy(mergedPoly, 0, workingPolygons, bestPolyA, _verticesPerPoly);
                        // 将多边形B删除
                        Array.Copy(workingPolygons, bestPolyB + _verticesPerPoly, workingPolygons, bestPolyB, workingPolygons.Length - bestPolyB - _verticesPerPoly);

                        workingPolyCount--;
                    }
                }

                for (int i = 0; i < workingPolyCount; i++)
                {
                    Array.Copy(workingPolygons, i * _verticesPerPoly,
                        globalPolygons, globalPolyCount * _verticesPerPoly, _verticesPerPoly);
                    globalRegions[globalPolyCount] = contour.RegionId;
                    globalPolyCount++;
                }
            }

            _pmf.Vertices = new int[globalVertexCount * 3];
            Array.Copy(globalVertices, 0, _pmf.Vertices, 0, globalVertexCount * 3);

            _pmf.Polygons = new int[globalPolyCount * _verticesPerPoly * 2];
            for (int i = 0; i < globalPolyCount; i++)
            {
                int poly = i * _verticesPerPoly;  // Poly的索引
                for (int offset = 0; offset < _verticesPerPoly; offset++)
                {
                    _pmf.Polygons[poly * 2 + offset] = globalPolygons[poly + offset]; // 多边形的顶点索引
                    _pmf.Polygons[poly * 2 + _verticesPerPoly + offset] = -1; // 邻接多边形的顶点索引
                }
            }

            _pmf.RegionIndices = new int[globalPolyCount];
            Array.Copy(globalRegions, 0, _pmf.RegionIndices, 0, globalPolyCount);

            BuildAdjacencyData();
        }

        private int Triangulate(int[] vertices, List<int> indices, List<int> outTriangles)
        {
            // 耳切法，将 Contour 转成多个三角形
            outTriangles.Clear();
            for (int i = 0; i < indices.Count; i++)
            {
                int j = (i + 1) % indices.Count;
                int k = (j + 1) % indices.Count;
                if (Diagonal(i, k, vertices, indices))
                {
                    indices[j] = indices[j] | FLAG;
                }
            }

            while (indices.Count > 3)
            {
                int minLength_Squared = -1;
                int targetVertex = -1;

                for (int i = 0; i < indices.Count; i++)
                {
                    int next1 = (i + 1) % indices.Count;

                    if ((indices[next1] & FLAG) == FLAG) // 是一个耳朵
                    {
                        int vertex = (indices[i] & CLEAR_FLAG) * 4;
                        int next2 = (next1 + 1) % indices.Count;
                        int vertexNext2 = (indices[next2] & CLEAR_FLAG) * 4;

                        int deltaX = vertices[vertexNext2] - vertices[vertex];
                        int deltaZ = vertices[vertexNext2 + 2] - vertices[vertex + 2];
                        int length_Squared = deltaX * deltaX + deltaZ * deltaZ;

                        if (minLength_Squared < 0
                            || length_Squared < minLength_Squared)
                        {
                            minLength_Squared = length_Squared;
                            targetVertex = i;
                        }
                    }
                }

                // Debug.Log($"cut at {indices[targetVertex]}");

                //三角化失败了，剩余的顶点不能再变成三角形， 理论上不会发生
                if (targetVertex == -1)
                {
                    return -(outTriangles.Count / 3);
                }

                int j = targetVertex;
                int jNext1 = (j + 1) % indices.Count;
                int jNext2 = (jNext1 + 1) % indices.Count;

                outTriangles.Add(indices[j] & CLEAR_FLAG);
                outTriangles.Add(indices[jNext1] & CLEAR_FLAG);
                outTriangles.Add(indices[jNext2] & CLEAR_FLAG);

                indices.RemoveAt(jNext1); // jPlus1 为耳朵，删除耳朵

                //耳朵是 0 号，则 j 要减一, 耳朵是indices.Count - 1 则 jNext1 要变成 0
                if (jNext1 == 0 || jNext1 >= indices.Count)
                {
                    j = indices.Count - 1;
                    jNext1 = 0;
                }

                // 检测一下删除了 jNext1 后的 j 点是否是耳朵
                int jPrev = (j - 1 + indices.Count) % indices.Count;
                bool isEar = Diagonal(jPrev, jNext1, vertices, indices);
                indices[j] = (isEar) ? indices[j] | FLAG : indices[j] & CLEAR_FLAG;

                // 检测一下 原 jNext2 点（就是现在的 jNext1） 是否是耳朵
                jNext2 = (jNext1 + 1) % indices.Count;
                isEar = Diagonal(j, jNext2, vertices, indices);
                indices[jNext1] = (isEar) ? indices[jNext1] | FLAG : indices[jNext1] & CLEAR_FLAG;
            }

            // 最后剩下的三个点成一个三角形
            outTriangles.Add(indices[0] & CLEAR_FLAG);
            outTriangles.Add(indices[1] & CLEAR_FLAG);
            outTriangles.Add(indices[2] & CLEAR_FLAG);

            return outTriangles.Count / 3;
        }

        private void GetPolyMergeValue(int polyAPointer, int polyBPointer, int[] polys, int[] vertices, out int[] outResult)
        {
            outResult = new int[] { -1, -1, -1 };

            int vertexCountA = GetPolyVertexCount(polyAPointer, polys);
            int vertexCountB = GetPolyVertexCount(polyBPointer, polys);

            // A B 合并后顶点数大于 _verticesPerPoly，不能合并
            if (vertexCountA + vertexCountB - 2 > _verticesPerPoly)
            {
                return;
            }

            for (int polyVertexA = 0; polyVertexA < vertexCountA; polyVertexA++)
            {
                int vertexA = polys[polyAPointer + polyVertexA];
                int vertexANext = polys[polyAPointer + (polyVertexA + 1) % vertexCountA];

                for (int polyVertexB = 0; polyVertexB < vertexCountB; polyVertexB++)
                {
                    int vertexB = polys[polyBPointer + polyVertexB];
                    int vertexBNext = polys[polyBPointer + (polyVertexB + 1) % vertexCountB];

                    /*
                     *    polyA, polyB 共享该边 (vertexA, vertexB)
                     *    A/BNext
                     *     \
                     *      \ 
                     *      ANext/B
                     *   即找到了Merge的点
                     *
                     *   在做这一步操作时，两个多边形只会有一个共享边，
                     *   因为这些多边形源自于三角分割出来的三角形，
                     *   即顶点都在最原始的那个Poly的边上，而不会在面中
                     *   可以自己画画图就明白了QwQ
                     */

                    if (vertexA == vertexBNext && vertexANext == vertexB)
                    {
                        outResult[1] = polyVertexA;
                        outResult[2] = polyVertexB;
                    }
                }
            }

            // 没有找到共享边, 无法Merge
            if (outResult[1] == -1)
            {
                return;
            }

            // 检测合并后的角度是否合法
            int prevSharedVertex;
            int sharedVertex;
            int nextSharedVertex;

            /*
            *    APrev  BNext2
            *     \    /
            *      \ /
            *      A/BNext
            *      |
            *      ANext/B
            *   （A，ANext）是共享边，则要检测APrev，A，BNext2这个角是不是凸的
            *   同时下方的角ANext2，B，BPrev（未画出）也要检测
            */

            // APrev
            int prevIndex = (outResult[1] - 1 + vertexCountA) % vertexCountA;
            prevSharedVertex = polys[polyAPointer + prevIndex] * 3;
            //A
            sharedVertex = polys[polyAPointer + outResult[1]] * 3;
            // BNext2
            nextSharedVertex = polys[polyBPointer + ((outResult[2] + 2) % vertexCountB)] * 3;

            if (CalculateGeometry.Left(vertices[prevSharedVertex..(prevSharedVertex + 3)],
                    vertices[sharedVertex..(sharedVertex + 3)],
                    vertices[nextSharedVertex..(nextSharedVertex + 3)]))
            {
                return;
            }

            prevIndex = (outResult[2] - 1 + vertexCountB) % vertexCountB;
            // BPrev
            prevSharedVertex = polys[polyBPointer + prevIndex] * 3;
            // B
            sharedVertex = polys[polyBPointer + outResult[2]] * 3;
            // ANext2
            nextSharedVertex = polys[polyAPointer + ((outResult[1] + 2) % vertexCountA)] * 3;

            if (CalculateGeometry.Left(vertices[prevSharedVertex..(prevSharedVertex + 3)],
                vertices[sharedVertex..(sharedVertex + 3)],
                vertices[nextSharedVertex..(nextSharedVertex + 3)]))
            {
                return;
            }

            //共享边
            prevSharedVertex = polys[polyAPointer + outResult[1]] * 3; // A
            sharedVertex = polys[polyAPointer + ((outResult[1] + 1) % vertexCountA)] * 3; // ANext

            int deltaX = vertices[prevSharedVertex + 0] - vertices[sharedVertex + 0];
            int deltaZ = vertices[prevSharedVertex + 2] - vertices[sharedVertex + 2];
            outResult[0] = deltaX * deltaX + deltaZ * deltaZ;
        }

        private int GetPolyVertexCount(int polyPointer, int[] polys)
        {
            for (int i = 0; i < _verticesPerPoly; i++)
                if (polys[polyPointer + i] == -1)
                    return i;

            return _verticesPerPoly;
        }

        private void BuildAdjacencyData()
        {
            int vertexCount = _pmf.Vertices.Length / 3;
            int polyCount = _pmf.RegionIndices.Length;
            int maxEdgeCount = polyCount * _verticesPerPoly; // 最大边数

            int[] edges = new int[maxEdgeCount * 6];
            int edgeCount = 0;

            int[] startEdge = new int[vertexCount];
            Array.Fill(startEdge, -1);

            //数组链表
            int[] nextEdge = new int[maxEdgeCount];
            for (int i = 0; i < polyCount; i++)
            {
                int poly = i * _verticesPerPoly * 2;
                for (int offset = 0; offset < _verticesPerPoly; offset++)
                {
                    int vertex = _pmf.Polygons[poly + offset];
                    if (vertex == -1)
                        break;

                    // 下一个点，如果是最后一个点，就选择第一个点
                    int nextVertex = (offset + 1 >= _verticesPerPoly || _pmf.Polygons[poly + offset + 1] == -1)
                                    ? _pmf.Polygons[poly]
                                    : _pmf.Polygons[poly + offset + 1];

                    if (vertex < nextVertex)
                    {
                        int edgeBaseIndex = edgeCount * 6;
                        //一条边的两个端点
                        edges[edgeBaseIndex] = vertex;
                        edges[edgeBaseIndex + 1] = nextVertex;

                        //这条边在多边形里面的信息
                        edges[edgeBaseIndex + 2] = i;
                        edges[edgeBaseIndex + 3] = offset;

                        //默认是边界边
                        edges[edgeBaseIndex + 4] = -1;
                        edges[edgeBaseIndex + 5] = -1;

                        //倒插链表 类似邻接表的思路？
                        nextEdge[edgeCount] = startEdge[vertex];
                        startEdge[vertex] = edgeCount;

                        edgeCount++;
                    }
                }
            }

            for (int i = 0; i < polyCount; ++i)
            {
                int poly = i * _verticesPerPoly * 2;
                for (int offset = 0; offset < _verticesPerPoly; offset++)
                {
                    int vertex = _pmf.Polygons[poly + offset];
                    if (vertex == -1)
                        break;

                    int nextVertex = (offset + 1 >= _verticesPerPoly
                                        || _pmf.Polygons[poly + offset] == -1)
                                        ? _pmf.Polygons[poly]
                                        : _pmf.Polygons[poly + offset];

                    if (vertex > nextVertex)
                    {
                        for (int edgeIndex = startEdge[nextVertex]; edgeIndex != -1; edgeIndex = nextEdge[edgeIndex])
                        {
                            if (vertex == edges[edgeIndex * 6 + 1])
                            {
                                edges[edgeIndex * 6 + 4] = i;
                                edges[edgeIndex * 6 + 5] = offset;
                                break;
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < edgeCount; i += 6)
            {
                if (edges[i + 4] != -1)
                {
                    int polyA = edges[i + 2] * _verticesPerPoly * 2;
                    int polyB = edges[i + 4] * _verticesPerPoly * 2;

                    _pmf.Polygons[polyA + _verticesPerPoly + edges[i + 3]] = edges[i + 4];
                    _pmf.Polygons[polyB + _verticesPerPoly + edges[i + 5]] = edges[i + 2];
                }
            }
        }

        private bool Diagonal(int indexA, int indexB, int[] vertices, List<int> indices)
        {
            return InCone(indexA, indexB, vertices, indices)
                && DiagonalWithoutIntersect(indexA, indexB, vertices, indices);
        }

        private bool InCone(int indexA, int indexB, int[] vertices, List<int> indices)
        {
            int vertexA = ((indices[indexA] & CLEAR_FLAG) * 4);
            int vertexB = ((indices[indexB] & CLEAR_FLAG) * 4);

            int indexPrevA = (indexA - 1 + indices.Count) % indices.Count;
            int indexNextA = (indexA + 1) % indices.Count;
            int vertexPrevA = ((indices[indexPrevA] & CLEAR_FLAG) * 4);
            int vertexNextA = ((indices[indexNextA] & CLEAR_FLAG) * 4);

            // Debug.Log("inCone: " + vertexA + " " + vertexB + " " + vertexPrevA + " " + vertexNextA);
            // vertexA is convex
            if (!CalculateGeometry.Left(vertices[vertexPrevA..(vertexPrevA + 3)],
                vertices[vertexA..(vertexA + 3)], vertices[vertexNextA..(vertexNextA + 3)]))
                return CalculateGeometry.Left(vertices[vertexA..(vertexA + 3)],
                            vertices[vertexB..(vertexB + 3)], vertices[vertexNextA..(vertexNextA + 3)])
                        && CalculateGeometry.Left(vertices[vertexB..(vertexB + 3)],
                                vertices[vertexA..(vertexA + 3)], vertices[vertexPrevA..(vertexPrevA + 3)]);

            // vertexA is reflex
            return !(CalculateGeometry.LeftOn(vertices[vertexA..(vertexA + 3)],
                        vertices[vertexB..(vertexB + 3)], vertices[vertexPrevA..(vertexPrevA + 3)])
                    && CalculateGeometry.LeftOn(vertices[vertexB..(vertexB + 3)],
                            vertices[vertexA..(vertexA + 3)], vertices[vertexNextA..(vertexNextA + 3)]));
        }

        private bool DiagonalWithoutIntersect(int indexA, int indexB, int[] vertices, List<int> indices)
        {
            int vertexA = (indices[indexA] & CLEAR_FLAG) * 4;
            int vertexB = (indices[indexB] & CLEAR_FLAG) * 4;

            for (int edgeBeginIndex = 0; edgeBeginIndex < indices.Count; ++edgeBeginIndex)
            {
                int edgeEndIndex = (edgeBeginIndex + 1) % indices.Count;
                if (!(edgeBeginIndex == indexA || edgeBeginIndex == indexB
                        || edgeEndIndex == indexA || edgeEndIndex == indexB))
                {
                    int beginVertex = (indices[edgeBeginIndex] & CLEAR_FLAG) * 4;
                    int endVertex = (indices[edgeEndIndex] & CLEAR_FLAG) * 4;

                    if (CalculateGeometry.Intersect(vertices[vertexA..(vertexA + 3)],
                        vertices[vertexB..(vertexB + 3)],
                        vertices[beginVertex..(beginVertex + 3)],
                        vertices[endVertex..(endVertex + 3)]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}