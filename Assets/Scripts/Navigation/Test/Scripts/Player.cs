using Navigation.Components;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Navigation.Test
{
    [RequireComponent(typeof(NavigationAgent))]
    public class Player : MonoBehaviour
    {
        public Color PathColor = Color.red;
        private NavigationAgent _agent;
        private LineRenderer _pathLine;

        private void Awake()
        {
            _agent = GetComponent<NavigationAgent>();
        }

        void Update()
        {
            if (_agent != null && _agent.Path != null)
            {
                if (_pathLine == null)
                {
                    _pathLine = gameObject.AddComponent<LineRenderer>();
                    _pathLine.material = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply"));
                    _pathLine.startColor = PathColor;
                    _pathLine.endColor = PathColor;
                    _pathLine.startWidth = 0.1f;
                    _pathLine.endWidth = 0.1f;
                }

                _pathLine.positionCount = _agent.Path.Count + 1;
                _pathLine.SetPositions(_agent.Path.ToArray());
                _pathLine.SetPosition(_pathLine.positionCount - 1, transform.position);
            }
        }
    }
}