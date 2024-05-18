using Navigation.Flags;
using UnityEngine;

namespace Navigation.Finder.PathFinding
{
    public class AStarOption
    {
        public Vector3 Start;
        public Vector3 Destination;
        public AreaMask WalkableAreas;
        public AgentType Agent;
    }
}
