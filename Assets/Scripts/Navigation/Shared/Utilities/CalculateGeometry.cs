using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Navigation.Utilities
{
    // TODO: 感觉这个库里面方法命名上不太直观，例如有些只在XOZ平面上运算，有的考虑三维空间，应该稍微改一下命名，以免误导
    public static class CalculateGeometry
    {
        public static readonly float epsilon = 1e-6f;
        public static readonly int[] DirectionX = { -1, 0, 1, 0 };
        public static readonly int[] DirectionZ = { 0, 1, 0, -1 };

        public static float PointSegmentDistance_Squared(float pointX, float pointY, float pointZ,
                                        float ax, float ay, float az, float bx, float by, float bz)
        {
            float deltaABx = bx - ax;
            float deltaABy = by - ay;
            float deltaABz = bz - az;

            float deltaAPx = pointX - ax;
            float deltaAPy = pointY - ay;
            float deltaAPz = pointZ - az;

            float segmentLength_sqr = deltaABx * deltaABx + deltaABy * deltaABy + deltaABz * deltaABz;

            if (segmentLength_sqr == 0)
                return deltaAPx * deltaAPx + deltaAPy * deltaAPy + deltaAPz * deltaAPz;

            float u = (deltaABx * deltaAPx + deltaABy * deltaAPy + deltaABz * deltaAPz) / segmentLength_sqr;
            if (u < 0)
                return deltaAPx * deltaAPx + deltaAPy * deltaAPy + deltaAPz * deltaAPz;
            else if (u > 1)
                return (pointX - bx) * (pointX - bx) + (pointY - by) * (pointY - by) + (pointZ - bz) * (pointZ - bz);

            float deltaX = (ax + u * deltaABx) - pointX;
            float deltaY = (ay + u * deltaABy) - pointY;
            float deltaZ = (az + u * deltaABz) - pointZ;

            return deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ;
            // float abX = bx - ax;
            // float abY = by - ay;
            // float abZ = bz - az;

            // float apX = pointX - ax;
            // float apY = pointY - ay;
            // float apZ = pointZ - az;

            // float bpX = pointX - bx;
            // float bpY = pointY - by;
            // float bpZ = pointZ - bz;

            // // AP · AB < 0 说明点 p 在线段 AB 的反方向，距离为 AP 的长度
            // if (apX * abX + apY * abY + apZ * abZ < 0)
            //     return apX * apX + apY * apY + apZ * apZ;

            // // BP · AB > 0 说明点 p 在线段 AB 的正方向，距离为 BP 的长度
            // if (bpX * abX + bpY * abY + bpZ * abZ > 0)
            //     return bpX * bpX + bpY * bpY + bpZ * bpZ;

            // // 否则，点 p 在线段 AB 的垂线上，距离为 AP × AB 的长度
            // return apX * abY * abZ - apY * abX * abZ + apZ * abX * abY;
        }

        public static float PointSegmentDistance_Squared(int pointX, int pointY, int pointZ,
                                                int ax, int ay, int az, int bx, int by, int bz)
        {
            // float deltaABx = bx - ax;
            // float deltaABy = by - ay;
            // float deltaABz = bz - az;

            // float deltaAPx = pointX - ax;
            // float deltaAPy = pointY - ay;
            // float deltaAPz = pointZ - az;

            // float segmentLength_sqr = deltaABx * deltaABx + deltaABy * deltaABy + deltaABz * deltaABz;

            // if (segmentLength_sqr == 0)
            //     return deltaAPx * deltaAPx + deltaAPy * deltaAPy + deltaAPz * deltaAPz;

            // float u = (deltaABx * deltaAPx + deltaABy * deltaAPy + deltaABz * deltaAPz) / segmentLength_sqr;
            // if (u < 0)
            //     return deltaAPx * deltaAPx + deltaAPy * deltaAPy + deltaAPz * deltaAPz;
            // else if (u > 1)
            //     return (pointX - bx) * (pointX - bx) + (pointY - by) * (pointY - by) + (pointZ - bz) * (pointZ - bz);

            // float deltaX = (ax + u * deltaABx) - pointX;
            // float deltaY = (ay + u * deltaABy) - pointY;
            // float deltaZ = (az + u * deltaABz) - pointZ;

            // return deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ;
            float abX = bx - ax;
            float abY = by - ay;
            float abZ = bz - az;

            float apX = pointX - ax;
            float apY = pointY - ay;
            float apZ = pointZ - az;

            float bpX = pointX - bx;
            float bpY = pointY - by;
            float bpZ = pointZ - bz;

            // AP · AB < 0 说明点 p 在线段 AB 的反方向，距离为 AP 的长度
            if (apX * abX + apY * abY + apZ * abZ < 0)
                return apX * apX + apY * apY + apZ * apZ;

            // BP · AB > 0 说明点 p 在线段 AB 的正方向，距离为 BP 的长度
            if (bpX * abX + bpY * abY + bpZ * abZ > 0)
                return bpX * bpX + bpY * bpY + bpZ * bpZ;

            // 否则，点 p 在线段 AB 的一条垂线上，距离为 |AP × AB| / |AB|
            float x = apY * abZ - apZ * abY;
            float y = apZ * abX - apX * abZ;
            float z = apX * abY - apY * abX;
            float length = x * x + y * y + z * z;
            return length / (abX * abX + abY * abY + abZ * abZ);
            // return (apY * abZ - apZ * abY) * (apY * abZ - apZ * abY) +
            //        (apZ * abX - apX * abZ) * (apZ * abX - apX * abZ) +
            //        (apX * abY - apY * abX) * (apX * abY - apY * abX);
        }

        /*
        public static double PointToSegmentDistance(Point3D p, Point3D A, Point3D B)
        {
            Vector3D AB = B - A;
            Vector3D AP = p - A;
            Vector3D BP = p - B;
            if (Vector3D.DotProduct(AP, AB) < 0)
                return AP.Length;
            else if (Vector3D.DotProduct(BP, AB) > 0)
                return BP.Length;
            else
                return Vector3D.CrossProduct(AP, AB).Length / AB.Length;
        }
        */

        public static float PointSegmentDistance_Squared(float pointX, float pointZ, float ax, float az,
                                                            float bx, float bz)
        {
            float abX = bx - ax;
            float abZ = bz - az;
            float apX = pointX - ax;
            float apZ = pointZ - az;

            float length_squared = abX * abX + abZ * abZ;
            float dot = abX * apX + abZ * apZ;

            /*
             * point  -> a - b 的距离
             * dot = ab · ap = |ab| * |ap| * cos(θ)
             * |ap| * cos(θ) = dot / |ab|
             * 下面也即是用余弦定理求出了点在线段上垂足的坐标
             * 当然因为 a - b 是线段，所以“垂足”要限制在点 a 和点 b 之间
             */

            if (length_squared > 0)
                dot /= length_squared;
            if (dot < 0)
                dot = 0;
            else if (dot > 1)
                dot = 1;

            apX = ax + dot * abX - pointX;
            apZ = az + dot * abZ - pointZ;

            return apX * apX + apZ * apZ;
        }

        public static float PointSegmentDistance_Squared(int pointX, int pointZ, int ax, int az, int bx, int bz)
        {
            // 与上方 float 参数的方法相同。
            float abX = bx - ax;
            float abZ = bz - az;
            float apX = pointX - ax;
            float apZ = pointZ - az;

            float length_squared = abX * abX + abZ * abZ;
            float dot = abX * apX + abZ * apZ;

            if (length_squared > 0)
                dot /= length_squared;
            if (dot < 0)
                dot = 0;
            else if (dot > 1)
                dot = 1;

            apX = ax + dot * abX - pointX;
            apZ = az + dot * abZ - pointZ;

            return apX * apX + apZ * apZ;
        }

        /// <summary>
        /// 判断线段 ab 和 cd 是否重叠
        /// </summary>
        public static bool SegmentsOverlap(float[] a, float[] b, float[] c, float[] d)
        {
            // QUESTION：如果两个线段共线，我这个代码会判断为重叠，哪怕他们确实不重叠
            //           但是recastNavigation的代码，会判断为不重叠，哪怕他们确实重叠

            float area2ABD = Area2(a, b, d);
            float area2ABC = Area2(a, b, c);

            // 如果 ab × ac 与 ab × ad 同号，
            // 说明线段 cd 在 ab 的同一侧，肯定是不重叠的
            if (area2ABC * area2ABD > 0.0f)
                return false;
            else
            {
                float area2CDA = Area2(c, d, a);
                float area2CDB = Area2(c, d, b); // area2CDA + area2ABC - area2ABD;

                // 如果 cd × ca 与 cd × cb 同号，
                // 说明线段 ab 在 cd 的同一侧，肯定是不重叠的
                if (area2CDA * area2CDB > 0.0f)
                    return false;
            }

            // 如果异号，说明线段 ab 和 cd 重叠
            return true;

            /*
                (0, 0, 0) (1, 0, 0) (2, 0, 0) (3, 0, 0)
                a b c d

                a = (0 ,0 ,0)
                b = (1, 0, 1)
                c = (0, 0, 0)
                d = (2, 0, 2)

                ab = (1, 0, 1)
                ad = (2, 0, 2)
                ab x ad = 0

                ac = (0, 0, 0)
                ab x ac = 0

                ?? area2CDB = area2CDA + area2ABC - area2ABD;
                    这个公式应该是对于z分量来说是成立的，但是y分量
                    应该是另一个类似的公式

                a = (0, 0, 0)
                b = (3, 0, 4)
                c = (1, 0, 0)
                d = (3, 0, 5)

                ab = (3, 0, 4)
                ad = (3, 0, 5)
                ab x ad = -1

                ac = (1, 0, 0)
                ab x ac = 4

                cd = (2, 0, 5)
                ca = (-1, 0, 0)
                cd x ca = -5

                cb = (2, 0, 4)
                cd x cb = 2

                2 = -5 + 4 - 1  ??
            */
        }

        public static float Distance_Squared(float ax, float ay, float bx, float by)
        {
            float deltaX = (ax - bx);
            float deltaY = (ay - by);
            return deltaX * deltaX + deltaY * deltaY;
        }

        public static float Distance(float[] a, float[] b)
        {
            float deltaX = (a[0] - b[0]);
            float deltaZ = (a[2] - b[2]);
            return Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
        }

        public static float Distance_Squared(float[] a, float[] b)
        {
            float deltaX = (a[0] - b[0]);
            float deltaZ = (a[2] - b[2]);
            return deltaX * deltaX + deltaZ * deltaZ;
        }

        public static float Distance(float ax, float ay, float bx, float by)
        {
            float deltaX = (ax - bx);
            float deltaY = (ay - by);
            return Mathf.Sqrt(deltaX * deltaX + deltaY * deltaY);
        }

        //顺时针
        public static int RotateDirectionClockwise(int dir)
        {
            return (dir + 1) & 0x3;
        }

        // 逆时针
        public static int RotateDirectionCounterClockwise(int dir)
        {
            return (dir + 3) & 0x3;
        }

        // 调转方向
        public static int ReverseDirection(int dir)
        {
            return (dir + 2) & 0x3;
        }

        public static void Swap<T>(ref T a, ref T b)
        {
            T temp = a; a = b; b = temp;
        }

        /// <summary>
        /// <para>ab × ac, y 分量</para>
        /// <para>(b.z - a.z) * (c.x - a.x) - (c.z - a.z) * (b.x - a.x)</para>
        /// </summary>
        public static int Area2(int[] a, int[] b, int[] c)
        {
            return (b[2] - a[2]) * (c[0] - a[0]) - (c[2] - a[2]) * (b[0] - a[0]);
        }

        /// <summary>
        /// <para>ab × ac, y 分量</para>
        /// <para>ab.z * ac.x - ac.z * ab.x</para>
        /// </summary>
        public static float Area2(float[] ab, float[] ac)
        {
            return ab[2] * ac[0] - ac[2] * ab[0];
        }

        public static float Area2(float ax, float az, float bx, float bz, float cx, float cz)
        {
            return (bz - az) * (cx - ax) - (cz - az) * (bx - ax);
        }

        public static float Area2(float[] a, float[] b, float[] c)
        {
            return (b[2] - a[2]) * (c[0] - a[0]) - (c[2] - a[2]) * (b[0] - a[0]);
        }

        /// <summary>
        /// vec1 dot vec2 向量点乘，但是仅乘 x 和 z 分量
        /// </summary>
        public static float Dot2(float[] vec1, float[] vec2)
        {
            return vec1[0] * vec2[0] + vec1[2] * vec2[2];
        }

        /// <summary>
        /// return true if c is on the left side of ab
        /// </summary>
        public static bool Left(int[] a, int[] b, int[] c)
        {
            return Area2(a, b, c) < 0;
        }

        /// <summary>
        /// return true if c is on the left side of ab or on the line
        /// </summary>
        public static bool LeftOn(int[] a, int[] b, int[] c)
        {
            return Area2(a, b, c) <= 0;
        }

        public static bool Collinear(int[] a, int[] b, int[] c)
        {
            return Area2(a, b, c) == 0;
        }

        public static bool IntersectStrictly(int[] a, int[] b, int[] c, int[] d)
        {
            if (Collinear(a, b, c) || Collinear(a, b, d) ||
                Collinear(c, d, a) || Collinear(c, d, b))
                return false;

            return (Left(a, b, c) ^ Left(a, b, d)) && (Left(c, d, a) ^ Left(c, d, b));
        }

        public static bool Between(int[] a, int[] b, int[] c)
        {
            if (!Collinear(a, b, c))
                return false;

            if (a[0] != b[0])
                return ((a[0] <= c[0]) && (c[0] <= b[0])) || ((a[0] >= c[0]) && (c[0] >= b[0]));
            else
                return ((a[2] <= c[2]) && (c[2] <= b[2])) || ((a[2] >= c[2]) && (c[2] >= b[2]));
        }

        public static bool Intersect(int[] a, int[] b, int[] c, int[] d)
        {
            if (IntersectStrictly(a, b, c, d))
                return true;
            else if (Between(a, b, c) || Between(a, b, d) ||
                    Between(c, d, a) || Between(c, d, b))
                return true;
            else
                return false;
        }

        /// <summary>
        /// 在xoz平面上计算三角形的外接圆，圆心存放在入参 center 中，返回圆的半径
        /// </summary>
        /// <param name="center">外接圆的圆心 (x, 0, z)</param>
        /// <returns>外接圆半径</returns>
        public static float CircumCircle(float ax, float az, float bx, float bz, float cx, float cz, out float[] center)
        {
            const float EPS = 1e-6f;
            center = new float[] { 0, 0, 0 };

            float[] ab = { bx - ax, 0, bz - az };
            float[] ac = { cx - ax, 0, cz - az };

            float radius = -1f;
            float area2 = Area2(ab, ac);
            if (Mathf.Abs(area2) > EPS)
            {
                float abLength_squared = Dot2(ab, ab);
                float acLength_squared = Dot2(ac, ac);

                center[0] = (acLength_squared * ab[2] - abLength_squared * ac[2]) / (2 * area2);
                center[2] = (abLength_squared * ac[0] - acLength_squared * ab[0]) / (2 * area2);
                radius = (float)Mathf.Sqrt(center[0] * center[0] + center[2] * center[2]);

                center[0] += ax;
                center[2] += az;
            }

            return radius;
        }
    }
}