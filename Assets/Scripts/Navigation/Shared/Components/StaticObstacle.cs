using Navigation.Flags;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Navigation.Components
{
    public class StaticObstacle : MonoBehaviour
    {
        public AreaMask Area = AreaMask.NotWalkable;
    }
}
