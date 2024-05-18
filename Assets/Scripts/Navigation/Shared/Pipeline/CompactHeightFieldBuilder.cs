using Navigation.Flags;
using Navigation.PipelineData;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Logger = Navigation.Utilities.Logger;

namespace Navigation.Pipeline
{
    public class CompactHeightFieldBuilder : INavMeshPipeline
    {
        private DataSet _data;

        #region 快捷访问
        private Tile _tile => _data.CurrentTile;
        private CompactHeightField _chf => _data.CompactHeightField;
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

        public void Process(DataSet data)
        {
            _data = data;
            _logger.Info("Start Build CompactHeightField");
            Initialize();

            if (_shf.SpanDict.Count <= 0)
                return;

            GenerateCompactHeightField();
            LinkNeighborCompactHeightSpans();

            _logger.Info($"Summary: CompactHeightSpan Count: {_chf.Count}");
            _logger.Info("Build CompactHeightField Finished");
        }

        private void Initialize()
        {
            _voxelXNum = Mathf.CeilToInt(_tile.Width / _cellSize);
            _voxelYNum = Mathf.CeilToInt(_tile.Height / _cellHeight);
            _voxelZNum = Mathf.CeilToInt(_tile.Depth / _cellSize);

            _voxelNum = _voxelXNum * _voxelYNum * _voxelZNum;

            _chf.Initialize(_tile, _voxelXNum, _voxelYNum, _voxelZNum, _voxelNum);
        }

        private void GenerateCompactHeightField()
        {
            List<CompactHeightSpan> compactHeightSpanList = new List<CompactHeightSpan>(_shf.SpanDict.Count);

            foreach (var id in _shf.SpanDict.Keys)
            {
                var currentSpan = _shf.SpanDict[id];
                int curX = id % _voxelXNum;
                int curZ = id / _voxelXNum;
                CompactHeightSpan prev = null;

                int count = 0;
                while (currentSpan != null)
                {
                    if (currentSpan.Area > AreaMask.NotWalkable)
                    {
                        int floor = currentSpan.Top;
                        int ceiling = (currentSpan.Next != null) ? currentSpan.Next.Bottom : _voxelYNum;

                        var compactSpan = new CompactHeightSpan(floor, ceiling, uint.MaxValue, currentSpan.Area);
                        compactHeightSpanList.Add(compactSpan);
                        uint compactSpanId = (uint)compactHeightSpanList.Count - 1;
                        count++;

                        if (_chf.Cells[id].FirstSpan == uint.MaxValue)
                            _chf.Cells[id].FirstSpan = compactSpanId;

                        if (prev != null)
                            prev.Next = compactSpanId;
                        prev = compactSpan;
                    }

                    currentSpan = currentSpan.Next;
                }

                if (count > 0)
                {
                    _chf.Cells[id].Count = (uint)count;
                }
            }

            _chf.Spans = compactHeightSpanList.ToArray();
            LinkNeighborCompactHeightSpans();
        }

        private void LinkNeighborCompactHeightSpans()
        {
            int[] dirX = new int[] { -1, 0, 1, 0 };
            int[] dirZ = new int[] { 0, 1, 0, -1 };
            int maxTraversableStep = Mathf.CeilToInt(_agentMaxStepHeight / _cellHeight);
            int minTraversableHeight = Mathf.CeilToInt(_agentHeight / _cellHeight);

            for (int id = 0; id < _chf.Cells.Length; id++)
            {
                if (_chf.Cells[id].Count == 0)
                    continue;

                int curX = id % _voxelXNum;
                int curZ = id / _voxelXNum;

                for (int i = 0; i < _chf.Cells[id].Count; i++)
                {
                    uint curSpanId = _chf.Cells[id].FirstSpan + (uint)i;
                    var curSpan = _chf.Spans[curSpanId];

                    for (int j = 0; j < 4; j++)
                    {
                        int nextX = curX + dirX[j];
                        int nextZ = curZ + dirZ[j];

                        if (nextX < 0 || nextX >= _voxelXNum || nextZ < 0 || nextZ >= _voxelZNum)
                            continue;

                        uint nextSpanId = _chf.Cells[nextX + nextZ * _voxelXNum].FirstSpan;
                        if (nextSpanId == uint.MaxValue)
                            continue;

                        var nextSpan = _chf.Spans[nextSpanId];
                        while (nextSpan != null)
                        {
                            if (Mathf.Min(nextSpan.Ceiling, curSpan.Ceiling)
                                - Mathf.Max(nextSpan.Floor, curSpan.Floor) >= minTraversableHeight
                                && Mathf.Abs(nextSpan.Floor - curSpan.Floor) <= maxTraversableStep)
                            {
                                curSpan.Neighbors[j] = nextSpanId;
                                break;
                            }

                            nextSpanId = nextSpan.Next;
                            nextSpan = (nextSpanId != uint.MaxValue) ? _chf.Spans[nextSpanId] : null;
                        }
                    }

                }
            }
        }
    }
}