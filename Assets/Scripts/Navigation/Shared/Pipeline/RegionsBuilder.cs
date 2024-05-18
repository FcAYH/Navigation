using Navigation.PipelineData;
using Navigation.Flags;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Logger = Navigation.Utilities.Logger;

namespace Navigation.Pipeline
{
    public class RegionsBuilder : INavMeshPipeline
    {
        private DataSet _data;

        #region 快捷访问
        private Tile _tile => _data.CurrentTile;
        private CompactHeightField _chf => _data.CompactHeightField;
        private float _cellSize => _data.NavMeshPreference.CellSize;
        private float _cellHeight => _data.NavMeshPreference.CellHeight;
        private float _agentMaxSlope => _data.NavMeshPreference.AgentMaxSlope;
        private float _agentMaxStepHeight => _data.NavMeshPreference.AgentMaxStepHeight;
        private float _agentHeight => _data.NavMeshPreference.AgentHeight;
        private float _agentRadius => _data.NavMeshPreference.AgentRadius;
        private int _blurDistanceThreshold => _data.NavMeshPreference.BlurDistanceThreshold;
        private int _minRegionSize => _data.NavMeshPreference.MinRegionSize;
        private int _mergeRegionSize => _data.NavMeshPreference.MergeRegionSize;
        private Logger _logger => _data.Logger;
        #endregion

        private int _voxelXNum, _voxelYNum, _voxelZNum;
        private int _voxelNum;
        private int _minDistanceToBorder = int.MaxValue;
        private int _maxDistanceToBorder = int.MinValue;
        private int _regionCount;
        private int _traversableAreaWidth;
        private Region[] _regions;

        public void Process(DataSet data)
        {
            _data = data;
            _logger.Info("Start Build Regions");
            Initialize();

            if (_chf.Spans == null || _chf.Spans.Length <= _minRegionSize)
            {
                _data.RegionCount = 0;
                return;
            }

            ErodeWalkableArea();
            BuildRegions();

            _data.RegionCount = _regionCount;

            _logger.Info("Summary: Region Count: " + _regionCount);
            _logger.Info("Build Regions Finished");
        }

        private void Initialize()
        {
            _voxelXNum = Mathf.CeilToInt(_tile.Width / _cellSize);
            _voxelYNum = Mathf.CeilToInt(_tile.Height / _cellHeight);
            _voxelZNum = Mathf.CeilToInt(_tile.Depth / _cellSize);

            _voxelNum = _voxelXNum * _voxelYNum * _voxelZNum;

            _traversableAreaWidth = Mathf.FloorToInt(_agentRadius / _cellSize);
        }

        private void ErodeWalkableArea()
        {
            GenerateDistanceField(true);

            foreach (var span in _chf.Spans)
            {
                if (span.DistanceToBorder < _traversableAreaWidth)
                    span.Area = AreaMask.NotWalkable;
            }
        }

        private void GenerateDistanceField(bool relyOnBorder)
        {
            foreach (var cell in _chf.Cells)
            {
                if (cell.Count == 0) continue;

                for (int i = 0; i < cell.Count; i++)
                {
                    var span = _chf.Spans[cell.FirstSpan + i];

                    bool isBorder = false;
                    for (int dir = 0; dir < 4; dir++)
                    {
                        uint neighborId = span.Neighbors[dir];
                        if (neighborId == uint.MaxValue)
                        {
                            isBorder = true;
                            break;
                        }

                        if (!relyOnBorder   // 这种情况下，与不同Area相邻的Span也算做Border（用于生成Regions）
                            && neighborId != uint.MaxValue && _chf.Spans[neighborId].Area != span.Area)
                        {
                            isBorder = true;
                            break;
                        }
                    }

                    span.DistanceToBorder = isBorder ? 0 : int.MaxValue;
                }
            }

            SaitoAlgorithm();
            BlurDistanceField();
        }

        private void SaitoAlgorithm()
        {
            // Saito 算法

            /*
             *  逆时针访问   
             *    (-1, 1) (0, 1) (1, 1)  ForwardLeft  Forward  RightForward
             *    (-1, 0)  span  (1, 0)  Left         span     Right
             *    (-1,-1) (0,-1) (1,-1)  LeftBack     Back     BackRight
             */

            // Pass 1 正序枚举，顺序访问 (-1, 0) (-1,-1) (0,-1) (1,-1)
            foreach (var cell in _chf.Cells)
            {
                if (cell.Count == 0) continue;

                for (uint i = 0; i < cell.Count; i++)
                {
                    var span = _chf.Spans[cell.FirstSpan + i];
                    if (span.DistanceToBorder == 0)
                        continue;

                    // 以下处理的span都不是border span，所以Left和back span理论上一定存在
                    var leftSpan = _chf.Spans[span.Left];
                    if (span.DistanceToBorder > leftSpan.DistanceToBorder + 2)
                        span.DistanceToBorder = leftSpan.DistanceToBorder + 2;

                    var leftBackSpan = leftSpan.Back != uint.MaxValue
                                            ? _chf.Spans[leftSpan.Back]
                                            : null;
                    if (leftBackSpan != null)
                    {
                        if (span.DistanceToBorder > leftBackSpan.DistanceToBorder + 3)
                            span.DistanceToBorder = leftBackSpan.DistanceToBorder + 3;
                    }

                    var backSpan = _chf.Spans[span.Back];
                    if (span.DistanceToBorder > backSpan.DistanceToBorder + 2)
                        span.DistanceToBorder = backSpan.DistanceToBorder + 2;

                    var backRightSpan = backSpan.Right != uint.MaxValue
                                            ? _chf.Spans[backSpan.Right]
                                            : null;
                    if (backRightSpan != null)
                    {
                        if (span.DistanceToBorder > backRightSpan.DistanceToBorder + 3)
                            span.DistanceToBorder = backRightSpan.DistanceToBorder + 3;
                    }
                }
            }

            // Pass 2 倒叙枚举，顺序访问 (1, 0) (1, 1) (0, 1) (-1, 1)
            for (int id = _chf.Cells.Length - 1; id >= 0; id--)
            {
                var cell = _chf.Cells[id];
                if (cell.Count == 0) continue;

                for (int i = (int)cell.Count - 1; i >= 0; i--)
                {
                    var span = _chf.Spans[cell.FirstSpan + i];
                    if (span.DistanceToBorder == 0)
                        continue;

                    var rightSpan = _chf.Spans[span.Right];
                    if (span.DistanceToBorder > rightSpan.DistanceToBorder + 2)
                        span.DistanceToBorder = rightSpan.DistanceToBorder + 2;

                    var rightForwardSpan = rightSpan.Forward != uint.MaxValue
                                            ? _chf.Spans[rightSpan.Forward]
                                            : null;
                    if (rightForwardSpan != null)
                    {
                        if (span.DistanceToBorder > rightForwardSpan.DistanceToBorder + 3)
                            span.DistanceToBorder = rightForwardSpan.DistanceToBorder + 3;
                    }

                    var forwardSpan = _chf.Spans[span.Forward];
                    if (span.DistanceToBorder > forwardSpan.DistanceToBorder + 2)
                        span.DistanceToBorder = forwardSpan.DistanceToBorder + 2;

                    var forwardLeftSpan = forwardSpan.Left != uint.MaxValue
                                            ? _chf.Spans[forwardSpan.Left]
                                            : null;
                    if (forwardLeftSpan != null)
                    {
                        if (span.DistanceToBorder > forwardLeftSpan.DistanceToBorder + 3)
                            span.DistanceToBorder = forwardLeftSpan.DistanceToBorder + 3;
                    }
                }
            }
        }

        private void BlurDistanceField()
        {
            int[] blurResult = new int[_chf.Spans.Length];

            for (int i = 0; i < _chf.Spans.Length; i++)
            {
                var span = _chf.Spans[i];
                if (span.DistanceToBorder <= _blurDistanceThreshold)
                {
                    blurResult[i] = span.DistanceToBorder;
                    continue;
                }

                int workingDistance = span.DistanceToBorder;
                for (int dir = 0; dir < 4; dir++)
                {
                    uint neighborId = span.Neighbors[dir];
                    if (neighborId == uint.MaxValue)
                    {
                        workingDistance += span.DistanceToBorder * 2;
                        continue;
                    }

                    var neighborSpan = _chf.Spans[neighborId];
                    workingDistance += neighborSpan.DistanceToBorder;
                    neighborId = neighborSpan.Neighbors[(dir + 1) & 0x3];

                    workingDistance += (neighborId == uint.MaxValue)
                                        ? span.DistanceToBorder
                                        : _chf.Spans[neighborId].DistanceToBorder;
                }

                blurResult[i] = (workingDistance + 5) / 9;
            }

            for (int i = 0; i < blurResult.Length; i++)
            {
                _chf.Spans[i].DistanceToBorder = blurResult[i];
            }
        }

        private void BuildRegions()
        {
            GenerateDistanceField(false);

            foreach (var span in _chf.Spans)
            {
                _maxDistanceToBorder = Mathf.Max(_maxDistanceToBorder, span.DistanceToBorder);
                _minDistanceToBorder = Mathf.Min(_minDistanceToBorder, span.DistanceToBorder);
            }

            int expandIterations = 8;
            int distance = (_maxDistanceToBorder - 1) & ~1; // 变成偶数

            Queue<uint> workingQueue = new Queue<uint>(1024);
            int nextRegionId = 1; // 0用来表示NotRegion => 例如后面被过滤掉的

            List<uint>[] levelLists = new List<uint>[distance / 2 + 1];
            int level = 0;
            int rangeLow = distance, rangeHigh = _maxDistanceToBorder + 1;
            while (rangeLow >= 0)
            {
                levelLists[level] = new List<uint>(1024);
                for (uint i = 0; i < _chf.Spans.Length; i++)
                {
                    var span = _chf.Spans[i];
                    if (span.Area != AreaMask.NotWalkable
                        && span.DistanceToBorder >= rangeLow
                        && span.DistanceToBorder < rangeHigh)
                    {
                        levelLists[level].Add(i);
                    }
                }

                rangeHigh = rangeLow;
                rangeLow -= 2;
                level++;
            }

            for (int i = 0; i < level; i++)
            {
                var levelList = levelLists[i];
                if (levelList.Count == 0)
                    continue;

                if (nextRegionId > 1)
                {
                    ExpandRegions(levelList, expandIterations);
                }

                // flood New Region
                foreach (var index in levelList)
                {
                    if (index == uint.MaxValue || _chf.Spans[index].RegionId != 0)
                        continue;

                    var span = _chf.Spans[index];
                    span.RegionId = nextRegionId;
                    workingQueue.Enqueue(index);

                    while (workingQueue.Count > 0)
                    {
                        var currentSpanIndex = workingQueue.Dequeue();
                        var currentSpan = _chf.Spans[currentSpanIndex];

                        for (int dir = 0; dir < 4; dir++)
                        {
                            uint neighborId = currentSpan.Neighbors[dir];
                            if (neighborId == uint.MaxValue || !levelList.Contains(neighborId))
                                continue;

                            var neighborSpan = _chf.Spans[neighborId];
                            if (neighborSpan.RegionId == 0)
                            {
                                neighborSpan.RegionId = nextRegionId;
                                workingQueue.Enqueue(neighborId);
                            }
                        }
                    }

                    nextRegionId++;
                }
            }

            _regionCount = nextRegionId;

            // CleanNullRegionBorders(); 这个方法到底在干嘛不太清楚，感觉不需要
            if (_regionCount < 2)
                return;

            _regions = new Region[_regionCount];
            for (int i = 0; i < _regionCount; i++)
            {
                _regions[i] = new Region(i);
            }

            CollectAdjacencyInformation();

            // 过滤过小的独立区域以及合并相邻的小区域
            FilterOutSmallRegions();
        }

        private void ExpandRegions(List<uint> levelList, int maxIterations)
        {
            if (levelList == null || levelList.Count == 0)
                return;

            Dictionary<uint, int> cacheDict = new Dictionary<uint, int>(256);
            int iterCount = 0;
            while (true)
            {
                int skipped = 0;
                for (int i = 0; i < levelList.Count; i++)
                {
                    var index = levelList[i];
                    var curSpan = (index != uint.MaxValue) ? _chf.Spans[index] : null;
                    if (curSpan == null || curSpan.RegionId != 0)
                    {
                        skipped++;
                        continue;
                    }

                    int spanRegion = 0;
                    for (int dir = 0; dir < 4; dir++)
                    {
                        var nSpan = curSpan.Neighbors[dir] != uint.MaxValue
                                        ? _chf.Spans[curSpan.Neighbors[dir]] : null;
                        if (nSpan == null)
                            continue;

                        if (nSpan.RegionId > 0)
                        {
                            spanRegion = nSpan.RegionId;
                        }
                    }

                    if (spanRegion != 0)
                    {
                        cacheDict.Add(index, spanRegion);
                        levelList[i] = uint.MaxValue; // 标记成已被使用了
                    }
                    else
                    {
                        skipped++;
                    }
                }

                foreach (var pair in cacheDict)
                {
                    _chf.Spans[pair.Key].RegionId = pair.Value;
                }

                if (skipped == levelList.Count)
                {
                    break;
                }

                if (maxIterations != -1)
                {
                    iterCount++;
                    if (iterCount > maxIterations)
                        break;
                }
            }
        }

        private void CollectAdjacencyInformation()
        {
            // 收集邻接信息
            foreach (var span in _chf.Spans)
            {
                if (span.RegionId > 0)
                {
                    var region = _regions[span.RegionId];
                    region.SpanCount++;

                    // Overlapping
                    for (uint i = span.Next; i != uint.MaxValue; i = _chf.Spans[i].Next)
                    {
                        int nextSpanRegionId = _chf.Spans[i].RegionId;
                        if (nextSpanRegionId > 0
                            && !region.OverlappingRegions.Contains(nextSpanRegionId))
                        {
                            region.OverlappingRegions.Add(nextSpanRegionId);
                        }
                    }

                    // Connections
                    if (region.Connections.Count <= 0)
                    {
                        int edgeDirection = GetRegionEdgeDirection(span);
                        if (edgeDirection != -1)
                        {
                            FindRegionConnections(span, edgeDirection, region.Connections);
                        }
                    }
                }
            }
        }

        private void FilterOutSmallRegions()
        {
            // 合并小的Region
            int mergeCount = 0;
            do
            {
                mergeCount = 0;
                foreach (var region in _regions)
                {
                    if (region.Id <= 0 || region.SpanCount == 0)
                        continue;

                    if (region.SpanCount > _mergeRegionSize)
                        continue;

                    Region targetMergeRegion = null;
                    int smallestSizeFound = int.MaxValue;

                    foreach (int nRegionId in region.Connections)
                    {
                        if (nRegionId <= 0)
                            continue;

                        Region nRegion = _regions[nRegionId];
                        if (nRegion.SpanCount < smallestSizeFound
                            && CanMerge(region, nRegion))
                        {
                            targetMergeRegion = nRegion;
                            smallestSizeFound = nRegion.SpanCount;
                        }
                    }

                    if (targetMergeRegion != null
                        && MergeRegions(targetMergeRegion, region))
                    {
                        _logger.Info($"Merge region {region.Id} to {targetMergeRegion.Id}");
                        int oldRegionId = region.Id;
                        region.ResetWithId(targetMergeRegion.Id);

                        foreach (var r in _regions)
                        {
                            if (r.Id <= 0)
                                continue;
                            if (r.Id == oldRegionId)
                                r.Id = targetMergeRegion.Id;
                            else
                                ReplaceNeighborRegionId(r, oldRegionId, targetMergeRegion.Id);
                        }

                        mergeCount++;
                    }
                }
            } while (mergeCount > 0);

            // 清理孤岛Region
            for (int regionId = 1; regionId < _regionCount; regionId++)
            {
                Region region = _regions[regionId];
                if (region.SpanCount == 0)
                    continue;

                if (region.Connections.Count == 1
                    && region.Connections[0] == 0)
                {
                    if (region.SpanCount < _minRegionSize)
                    {
                        _logger.Info($"Remove region {region.Id}");
                        region.ResetWithId(0);
                    }
                }
            }

            ReMapRegions();
        }


        private void ReMapRegions()
        {
            // Re-map 区域Id， 保持Id的连续性
            foreach (var region in _regions)
            {
                if (region.Id > 0)
                {
                    region.Remap = true;
                }
            }

            int curRegionId = 0;
            foreach (var region in _regions)
            {
                if (!region.Remap)
                    continue;

                int oldId = region.Id;
                int newId = ++curRegionId;

                foreach (var r in _regions)
                {
                    if (r.Id == oldId)
                    {
                        r.Id = curRegionId;
                        r.Remap = false;
                    }
                }
            }

            _regionCount = curRegionId + 1;

            foreach (var span in _chf.Spans)
            {
                if (span.RegionId != 0)
                {
                    span.RegionId = _regions[span.RegionId].Id;
                }
            }
        }

        private void ReplaceNeighborRegionId(Region region, int oldId, int newId)
        {
            bool connectionsChanged = false;

            for (int i = 0; i < region.Connections.Count; i++)
            {
                if (region.Connections[i] == oldId)
                {
                    region.Connections[i] = newId;
                    connectionsChanged = true;
                }
            }

            for (int i = 0; i < region.OverlappingRegions.Count; i++)
            {
                if (region.OverlappingRegions[i] == oldId)
                {
                    region.OverlappingRegions[i] = newId;
                }
            }

            if (connectionsChanged)
            {
                RemoveAdjacentDuplicateConnections(region);
            }
        }

        private void RemoveAdjacentDuplicateConnections(Region region)
        {
            int iConnection = 0;
            while (iConnection < region.Connections.Count
                    && region.Connections.Count > 1)
            {
                int iNextConnection = (iConnection + 1) % region.Connections.Count;
                // 直接将重复的移除
                if (region.Connections[iConnection] == region.Connections[iNextConnection])
                    region.Connections.RemoveAt(iNextConnection);
                else
                    iConnection++;
            }
        }

        private bool MergeRegions(Region target, Region candidate)
        {
            int connectionPointOnTargetIndex = target.Connections.IndexOf(candidate.Id);
            if (connectionPointOnTargetIndex == -1)
                return false;

            int connectionPointOnCandidateIndex = candidate.Connections.IndexOf(target.Id);
            if (connectionPointOnCandidateIndex == -1)
                return false;

            List<int> targetConnections = new List<int>(target.Connections);
            target.Connections.Clear();
            int workingSize = targetConnections.Count;
            for (int i = 0; i < workingSize - 1; i++)
            {
                int newIndex = (connectionPointOnTargetIndex + 1 + i) % workingSize;
                target.Connections.Add(targetConnections[newIndex]);
            }

            workingSize = candidate.Connections.Count;
            for (int i = 0; i < workingSize - 1; i++)
            {
                int newIndex = (connectionPointOnCandidateIndex + 1 + i) % workingSize;
                target.Connections.Add(candidate.Connections[newIndex]);
            }

            RemoveAdjacentDuplicateConnections(target);

            foreach (int i in candidate.OverlappingRegions)
            {
                if (!target.OverlappingRegions.Contains(i))
                {
                    target.OverlappingRegions.Add(i);
                }
            }

            target.SpanCount += candidate.SpanCount;
            return true;
        }

        private bool CanMerge(Region regionA, Region regionB)
        {
            int connectionPointCount = 0;
            foreach (int connectionId in regionA.Connections)
            {
                if (connectionId == regionB.Id)
                {
                    connectionPointCount++;
                }
            }

            if (connectionPointCount != 1) // < 1 不相邻，> 1 中间有夹层
                return false;

            if (regionA.OverlappingRegions.Contains(regionB.Id))
                return false;
            if (regionB.OverlappingRegions.Contains(regionA.Id))
                return false;

            return true;
        }

        private void FindRegionConnections(CompactHeightSpan startSpan, int startDirection, List<int> outConnections)
        {
            CompactHeightSpan span = startSpan;
            int direction = startDirection;
            int lastEdgeRegionId = 0;

            CompactHeightSpan nSpan = span.Neighbors[direction] != uint.MaxValue
                                        ? _chf.Spans[span.Neighbors[direction]]
                                        : null;
            if (nSpan != null)
            {
                lastEdgeRegionId = nSpan.RegionId;
            }

            outConnections.Add(lastEdgeRegionId);

            int loopCount = 0;
            while (++loopCount < ushort.MaxValue)
            {
                nSpan = span.Neighbors[direction] != uint.MaxValue
                            ? _chf.Spans[span.Neighbors[direction]]
                            : null;
                int currentEdgeRegionId = 0;
                if (nSpan == null || nSpan.RegionId != span.RegionId)
                {
                    if (nSpan != null)
                        currentEdgeRegionId = nSpan.RegionId;

                    if (currentEdgeRegionId != lastEdgeRegionId)
                    {
                        outConnections.Add(currentEdgeRegionId);
                        lastEdgeRegionId = currentEdgeRegionId;
                    }

                    direction = RotateDirectionClockwise(direction);
                }
                else
                {
                    span = nSpan;
                    direction = RotateDirectionCounterClockwise(direction);
                }

                if (startSpan == span && startDirection == direction)
                {
                    break;
                }
            }

            int connectionsCount = outConnections.Count;
            if (connectionsCount > 1
                && outConnections[0] == outConnections[connectionsCount - 1])
            {
                outConnections.RemoveAt(connectionsCount - 1);
            }
        }

        private int GetRegionEdgeDirection(CompactHeightSpan span)
        {
            for (int i = 0; i < 4; i++)
            {
                CompactHeightSpan nSpan = span.Neighbors[i] != uint.MaxValue
                                            ? _chf.Spans[span.Neighbors[i]]
                                            : null;

                if (nSpan == null || nSpan.RegionId != span.RegionId)
                {
                    return i;
                }
            }

            return -1;
        }

        //顺时针
        private int RotateDirectionClockwise(int dir)
        {
            return (dir + 1) & 0x3;
        }

        // 逆时针
        private int RotateDirectionCounterClockwise(int dir)
        {
            return (dir + 3) & 0x3;
        }

        internal class Region
        {
            public int Id { get; set; }
            public int SpanCount { get; set; }
            public bool Remap { get; set; }

            public List<int> Connections = new List<int>();
            public List<int> OverlappingRegions = new List<int>();

            public Region(int id)
            {
                Id = id;
            }

            public void ResetWithId(int newRegionId)
            {
                Id = newRegionId;
                SpanCount = 0;
                Connections.Clear();
                OverlappingRegions.Clear();
            }

            public override string ToString()
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine($"Region: Id = {Id}, SpanCount = {SpanCount}, Remap = {Remap}, ConnectionsCount = {Connections.Count}, OverlappingRegionsCount = {OverlappingRegions.Count}");
                foreach (int i in Connections)
                    sb.Append(i + " ");
                sb.AppendLine("");
                return sb.ToString();
            }
        }
    }
}