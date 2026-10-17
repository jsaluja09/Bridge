using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Planarity check for a fill loop (spec section 2.4): the player can fill "a closed, planar
    /// polygon" and non-planar loops are rejected. A loop is planar when some plane through three of
    /// its vertices has every vertex within the tolerance, so the result does not depend on which
    /// vertex comes first. Positions are in room units (meters).
    /// </summary>
    public static class FillPlanarity
    {
        /// <summary>Checks the loop against <see cref="Constants.Fill.PlanarityToleranceMeters"/>.</summary>
        /// <param name="vertices">Loop vertices in order, first not repeated, in room units.</param>
        public static bool IsPlanar(IReadOnlyList<Vec3> vertices)
        {
            return IsPlanar(vertices, Constants.Fill.PlanarityToleranceMeters);
        }

        /// <summary>
        /// Returns true when every vertex lies within <paramref name="toleranceMeters"/> of a plane
        /// through three of the vertices. A vertex exactly at the tolerance passes. Returns false for
        /// fewer than three vertices or when all vertices are collinear, because no polygon plane exists.
        /// </summary>
        /// <param name="vertices">Loop vertices in order, first not repeated, in room units.</param>
        /// <param name="toleranceMeters">Largest allowed vertex-to-plane distance, room units.</param>
        /// <exception cref="ArgumentNullException"><paramref name="vertices"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="toleranceMeters"/> is negative, NaN, or infinite.</exception>
        /// <exception cref="ArgumentException">A vertex has an infinite component.</exception>
        public static bool IsPlanar(IReadOnlyList<Vec3> vertices, double toleranceMeters)
        {
            if (vertices == null)
            {
                throw new ArgumentNullException(nameof(vertices));
            }

            if (double.IsNaN(toleranceMeters) || double.IsInfinity(toleranceMeters) || toleranceMeters < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(toleranceMeters), toleranceMeters, "The tolerance must be finite and not negative.");
            }

            int count = vertices.Count;
            for (int i = 0; i < count; i++)
            {
                Vec3 vertex = vertices[i];
                if (double.IsInfinity(vertex.X) || double.IsInfinity(vertex.Y) || double.IsInfinity(vertex.Z))
                {
                    throw new ArgumentException("Vertex " + i + " has an infinite component.", nameof(vertices));
                }
            }

            // Rejecting a large loop visits every vertex triple. Retrying the vertex that broke the
            // previous candidate plane first keeps each failed triple cheap when one vertex is the stray.
            int lastOutlier = -1;
            for (int a = 0; a < count - 2; a++)
            {
                for (int b = a + 1; b < count - 1; b++)
                {
                    for (int c = b + 1; c < count; c++)
                    {
                        Vec3 origin = vertices[a];
                        Vec3 normal = Vec3.Cross(vertices[b] - origin, vertices[c] - origin);
                        double lengthSquared = normal.LengthSquared;
                        if (lengthSquared == 0.0 || double.IsInfinity(lengthSquared))
                        {
                            continue;
                        }

                        Vec3 unitNormal = normal.Normalized();
                        if (lastOutlier >= 0 && IsOutside(vertices[lastOutlier], origin, unitNormal, toleranceMeters))
                        {
                            continue;
                        }

                        int outlier = FindOutlier(vertices, origin, unitNormal, toleranceMeters);
                        if (outlier < 0)
                        {
                            return true;
                        }

                        lastOutlier = outlier;
                    }
                }
            }

            return false;
        }

        private static int FindOutlier(IReadOnlyList<Vec3> vertices, Vec3 origin, Vec3 unitNormal, double toleranceMeters)
        {
            for (int i = 0; i < vertices.Count; i++)
            {
                if (IsOutside(vertices[i], origin, unitNormal, toleranceMeters))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsOutside(Vec3 vertex, Vec3 origin, Vec3 unitNormal, double toleranceMeters)
        {
            return Math.Abs(Vec3.Dot(vertex - origin, unitNormal)) > toleranceMeters;
        }
    }
}
