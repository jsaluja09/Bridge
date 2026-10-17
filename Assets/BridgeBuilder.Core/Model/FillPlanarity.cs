using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>Spec 2.4: fills must be closed, planar polygons; non-planar loops are rejected.</summary>
    public static class FillPlanarity
    {
        public static bool IsPlanar(IReadOnlyList<Vec3> loop) => IsPlanar(loop, Constants.Fill.PlanarityToleranceMeters);

        /// <summary>
        /// True when every point is within tolerance (inclusive) of the plane through the loop's first three
        /// non-collinear points, so "this vertex is 1 cm off the fill" means exactly that.
        /// </summary>
        public static bool IsPlanar(IReadOnlyList<Vec3> loop, double tolerance)
        {
            if (loop.Count < 3 || !TryReferencePlane(loop, out Vec3 origin, out Vec3 normal))
                return false;
            foreach (var p in loop)
            {
                if (System.Math.Abs(Vec3.Dot(p - origin, normal)) > tolerance + 1e-12)
                    return false;
            }
            return true;
        }

        static bool TryReferencePlane(IReadOnlyList<Vec3> loop, out Vec3 origin, out Vec3 normal)
        {
            origin = loop[0];
            for (int i = 1; i < loop.Count; i++)
            {
                for (int j = i + 1; j < loop.Count; j++)
                {
                    Vec3 n = Vec3.Cross(loop[i] - origin, loop[j] - origin);
                    if (n.Length > 1e-9)
                    {
                        normal = n.Normalized();
                        return true;
                    }
                }
            }
            normal = Vec3.UnitY;
            return false;
        }

        public static Vec3 Normal(IReadOnlyList<Vec3> loop)
        {
            Geometry.FitPlane(loop, out _, out Vec3 normal, out _);
            return normal.Y < 0 ? -normal : normal;
        }
    }
}
