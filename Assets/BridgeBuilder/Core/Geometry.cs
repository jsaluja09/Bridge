using System;
using System.Collections.Generic;
using System.Numerics;

namespace BridgeBuilder.Core
{
    public static class Geometry
    {
        public static Vector3 Centroid(IReadOnlyList<Vector3> pts)
        {
            Vector3 c = Vector3.Zero;
            for (int i = 0; i < pts.Count; i++)
                c += pts[i];
            return pts.Count > 0 ? c / pts.Count : c;
        }

        /// <summary>Least-squares plane through the points. maxDistance is the worst point's distance from it.</summary>
        public static void FitPlane(IReadOnlyList<Vector3> pts, out Vector3 centroid, out Vector3 normal, out float maxDistance)
        {
            centroid = Centroid(pts);
            double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (var p in pts)
            {
                Vector3 d = p - centroid;
                xx += d.X * d.X; xy += d.X * d.Y; xz += d.X * d.Z;
                yy += d.Y * d.Y; yz += d.Y * d.Z; zz += d.Z * d.Z;
            }

            // The normal is the eigenvector with the smallest eigenvalue: find the two largest by
            // power iteration with deflation, then take their cross product.
            Vector3 e1 = PowerIterate(xx, xy, xz, yy, yz, zz, Vector3.Zero, out double l1);
            Vector3 e2 = PowerIterate(xx - l1 * e1.X * e1.X, xy - l1 * e1.X * e1.Y, xz - l1 * e1.X * e1.Z,
                                      yy - l1 * e1.Y * e1.Y, yz - l1 * e1.Y * e1.Z, zz - l1 * e1.Z * e1.Z, e1, out _);
            normal = Vector3.Cross(e1, e2);
            if (normal.LengthSquared() < 1e-12f)
                normal = AnyPerpendicular(e1);
            normal = Vector3.Normalize(normal);

            maxDistance = 0f;
            foreach (var p in pts)
                maxDistance = Math.Max(maxDistance, Math.Abs(Vector3.Dot(p - centroid, normal)));
        }

        static Vector3 PowerIterate(double xx, double xy, double xz, double yy, double yz, double zz, Vector3 avoid, out double eigenvalue)
        {
            double x = 0.577, y = 0.577, z = 0.577;
            if (avoid != Vector3.Zero)
            {
                Vector3 p = AnyPerpendicular(avoid);
                x = p.X; y = p.Y; z = p.Z;
            }
            eigenvalue = 0;
            for (int i = 0; i < 40; i++)
            {
                double nx = xx * x + xy * y + xz * z;
                double ny = xy * x + yy * y + yz * z;
                double nz = xz * x + yz * y + zz * z;
                double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len < 1e-18)
                    break;
                eigenvalue = len;
                x = nx / len; y = ny / len; z = nz / len;
            }
            return Vector3.Normalize(new Vector3((float)x, (float)y, (float)z));
        }

        public static Vector3 AnyPerpendicular(Vector3 v)
        {
            Vector3 other = Math.Abs(v.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
            return Vector3.Normalize(Vector3.Cross(v, other));
        }

        /// <summary>Two unit vectors spanning the plane with this normal.</summary>
        public static void PlaneBasis(Vector3 normal, out Vector3 u, out Vector3 v)
        {
            u = AnyPerpendicular(normal);
            v = Vector3.Cross(normal, u);
        }

        public static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float len2 = ab.LengthSquared();
            float t = len2 > 1e-12f ? Math.Clamp(Vector3.Dot(p - a, ab) / len2, 0f, 1f) : 0f;
            return Vector3.Distance(p, a + ab * t);
        }

        public static float SignedArea(IReadOnlyList<Vector2> poly)
        {
            float a = 0f;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                a += poly[j].X * poly[i].Y - poly[i].X * poly[j].Y;
            return a * 0.5f;
        }

        static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

        /// <summary>
        /// Ear-clipping triangulation of a simple polygon. Returns index triples into poly.
        /// Clips the ear with the shortest new diagonal each time, so a long deck becomes a zigzag of short
        /// braces rather than a fan of long stays from one corner. Points lying on a candidate ear's
        /// diagonal block it, so bracing never runs through a boundary node.
        /// </summary>
        public static List<int> Triangulate(IReadOnlyList<Vector2> poly)
        {
            var tris = new List<int>();
            int n = poly.Count;
            if (n < 3)
                return tris;
            float sign = SignedArea(poly) >= 0f ? 1f : -1f;
            float scale = 0f;
            foreach (var p in poly)
                scale = Math.Max(scale, Math.Max(Math.Abs(p.X), Math.Abs(p.Y)));
            float eps = 1e-6f * Math.Max(1f, scale * scale);

            var idx = new List<int>(n);
            for (int i = 0; i < n; i++)
                idx.Add(i);

            while (idx.Count > 3)
            {
                int count = idx.Count;
                int bestFallback = -1;
                float bestFallbackArea = float.MinValue;
                int bestEar = -1;
                float bestDiagonal = float.MaxValue;

                for (int i = 0; i < count; i++)
                {
                    int a = idx[(i + count - 1) % count], b = idx[i], c = idx[(i + 1) % count];
                    float area = Cross(poly[b] - poly[a], poly[c] - poly[b]) * sign;
                    if (area > bestFallbackArea)
                    {
                        bestFallbackArea = area;
                        bestFallback = i;
                    }
                    if (area <= eps)
                        continue;
                    float diagonal = Vector2.DistanceSquared(poly[a], poly[c]);
                    if (diagonal >= bestDiagonal)
                        continue;

                    bool blocked = false;
                    for (int k = 0; k < count && !blocked; k++)
                    {
                        int p = idx[k];
                        if (p == a || p == b || p == c)
                            continue;
                        blocked = InTriangleInclusive(poly[p], poly[a], poly[b], poly[c], sign, eps);
                    }
                    if (blocked)
                        continue;
                    bestDiagonal = diagonal;
                    bestEar = i;
                }

                // A degenerate polygon may have no clean ear: clip the most convex vertex so we always finish.
                int clip = bestEar >= 0 ? bestEar : bestFallback;
                tris.Add(idx[(clip + count - 1) % count]);
                tris.Add(idx[clip]);
                tris.Add(idx[(clip + 1) % count]);
                idx.RemoveAt(clip);
            }
            tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]);
            return tris;
        }

        static bool InTriangleInclusive(Vector2 p, Vector2 a, Vector2 b, Vector2 c, float sign, float eps)
        {
            float d1 = Cross(b - a, p - a) * sign;
            float d2 = Cross(c - b, p - b) * sign;
            float d3 = Cross(a - c, p - c) * sign;
            return d1 >= -eps && d2 >= -eps && d3 >= -eps;
        }

        /// <summary>Barycentric weights of p projected onto triangle abc in the XZ plane. False if outside.</summary>
        public static bool BarycentricXZ(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out float wa, out float wb, out float wc)
        {
            float v0x = b.X - a.X, v0z = b.Z - a.Z;
            float v1x = c.X - a.X, v1z = c.Z - a.Z;
            float v2x = p.X - a.X, v2z = p.Z - a.Z;
            float den = v0x * v1z - v1x * v0z;
            wa = wb = wc = 0f;
            if (Math.Abs(den) < 1e-9f)
                return false;
            wb = (v2x * v1z - v1x * v2z) / den;
            wc = (v0x * v2z - v2x * v0z) / den;
            wa = 1f - wb - wc;
            const float e = -1e-4f;
            return wa >= e && wb >= e && wc >= e;
        }
    }
}
