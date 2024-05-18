using Navigation.Flags;
using Navigation.Finder.PathFinding;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Navigation.Components
{
    public class NavigationAgent : MonoBehaviour
    {
        [Header("Basic")]
        public AgentType AgentType;
        public float Speed = 4f;
        public float RotationSpeed = 120f;
        public float StoppingDistance = 0.2f;
        public float Acceleration = 8f;
        public bool AutoBraking = true;

        [Header("Obstacle Avoidance")]
        public OAQualityLevel QualityLevel = OAQualityLevel.Medium;
        public int Priority = 50;

        [Header("Path Finding")]
        public AreaMask WalkableArea;
        public bool AutoRepath = true;

        [HideInInspector]
        /// <summary>
        /// 设置 Agent 的目的地，
        /// 当 Agent 距离目的地大于 StoppingDistance 时，会触发寻路，并且移动 Agent
        /// 当 Agent 距离目的地小于 StoppingDistance 时，Agent 将停止移动
        /// </summary>
        public Vector3 Destination
        {
            get => _dest;
            set
            {
                _dest = value;
                FindPath();
            }
        }

        [HideInInspector]
        public event Action<NavigationAgent> OnDestinationReached;

        [HideInInspector]
        public List<Vector3> Path => _path;

        private Vector3 _dest;
        private List<Vector3> _path; // 路径倒着记录，每走过一个，就删除一个

        private void Awake()
        {
            _dest = transform.position;
        }

        private void FindPath()
        {
            if (Vector3.SqrMagnitude(transform.position - _dest) < StoppingDistance * StoppingDistance)
            {
                // 到达目的地
                OnDestinationReached?.Invoke(this);
                return;
            }

            AStarOption option = new AStarOption
            {
                Start = transform.position,
                Destination = _dest,
                WalkableAreas = WalkableArea,
                Agent = AgentType
            };

            _path = AStar.FindPath(option);
            Debug.Log("Dest: " + Destination + ", path end: " + _path[0]);
        }

        private void Update()
        {
            if (Vector3.SqrMagnitude(transform.position - _dest) < StoppingDistance * StoppingDistance)
            {
                // 到达目的地
                OnDestinationReached?.Invoke(this);
                return;
            }

            if (_path == null || _path.Count == 0)
            {
                Debug.Log(Vector3.SqrMagnitude(transform.position - _dest));
                // 没有路径
                return;
            }

            // 移动
            Vector3 dir = _path[_path.Count - 1] - transform.position;
            dir.Normalize();

            transform.position += dir * Speed * Time.deltaTime;
            dir.y = 0;

            // 旋转
            Quaternion targetRotation = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, RotationSpeed * Time.deltaTime);

            // 到达路径点
            if (Vector3.SqrMagnitude(transform.position - _path[_path.Count - 1]) < StoppingDistance * StoppingDistance)
            {
                _path.RemoveAt(_path.Count - 1);
            }
        }
    }

    /// <summary>
    /// 避障的质量
    /// </summary>
    public enum OAQualityLevel
    {
        Low = 0,
        Medium = 1,
        High = 2
    }
}