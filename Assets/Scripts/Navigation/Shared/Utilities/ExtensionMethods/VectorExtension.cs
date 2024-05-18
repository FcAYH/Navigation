using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RTS.Utilities.Extensions
{

    public static class VectorExtension
    {
        public static Vector3 ComponentMin(this Vector3 v1, Vector3 v2)
        {
            return new Vector3(Mathf.Min(v1.x, v2.x),
                                Mathf.Min(v1.y, v2.y),
                                Mathf.Min(v1.z, v2.z));
        }

        public static Vector3 ComponentMax(this Vector3 v1, Vector3 v2)
        {
            return new Vector3(Mathf.Max(v1.x, v2.x),
                                Mathf.Max(v1.y, v2.y),
                                Mathf.Max(v1.z, v2.z));
        }
    }
}
