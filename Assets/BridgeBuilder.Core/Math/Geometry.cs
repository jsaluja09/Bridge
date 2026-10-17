using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>A 2D point in a plane's own coordinates.</summary>
    public readonly struct Vec2
    {
        public readonly double X;
        public readonly double Y;

        public Vec2(double x, double y)
        {
            X = x;
            Y = y;
        }

        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;
        public static double DistanceSquared(Vec2 a, Vec2 b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
    }

    public static class Geometry
    {
        public static Vec3 Centroid(IReadOnlyList<Vec3> pts)
        {
            double x = 0, y = 0, z = 0;
            foreach (var p in pts)
            {
                x += p.X;
                y += p.Y;
                z += p.Z;
            }
            int n = Math.Max(1, pts.Count);
            return new Vec3(x / n, y / n, z / n);
        }

        /// <summary>Least-squares plane. maxDistance is the worst point's distance from it.</summary>
        public static void FitPlane(IReadOnlyList<Vec3> pts, out Vec3 centroid, out Vec3 normal, out double maxDistance)
        {
            centroid = Centroid(pts);
            double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (var p in pts)
            {
                Vec3 d = p - centroid;
                xx += d.X * d.X; xy += d.X * d.Y; xz += d.X * d.Z;
                yy += d.Y * d.Y; yz += d.Y * d.Z; zz += d.Z * d.Z;
            }

            // The normal is the smallest eigenvector: find the two largest by power iteration with deflation.
            Vec3 e1 = PowerIterate(xx, xy, xz, yy, yz, zz, Vec3.Zero, out double l1);
            Vec3 e2 = PowerIterate(xx - l1 * e1.X * e1.X, xy - l1 * e1.X * e1.Y, xz - l1 * e1.X * e1.Z,
                                   yy - l1 * e1.Y * e1.Y, yz - l1 * e1.Y * e1.Z, zz - l1 * e1.Z * e1.Z, e1, out _);
            normal = Vec3.Cross(e1, e2);
            normal = normal.Length < 1e-12 ? AnyPerpendicular(e1) : normal.Normalized();

            maxDistance = 0;
            foreach (var p in pts)
                maxDistance = Math.Max(maxDistance, Math.Abs(Vec3.Dot(p - centroid, normal)));
        }

        static Vec3 PowerIterate(double xx, double xy, double xz, double yy, double yz, double zz, Vec3 avoid, out double eigenvalue)
        {
            // Start from a generic direction (minus any component along 'avoid'), never one that could be
            // exactly perpendicular to flat data.
            Vec3 v = new Vec3(0.577, 0.578, 0.579);
            if (avoid.LengthSquared > 0)
            {
                v -= avoid * Vec3.Dot(v, avoid);
                if (v.Length < 1e-9)
                    v = AnyPerpendicular(avoid);
                v = v.Normalized();
            }
            eigenvalue = 0;
            for (int i = 0; i < 60; i++)
            {
                var next = new Vec3(xx * v.X + xy * v.Y + xz * v.Z, xy * v.X + yy * v.Y + yz * v.Z, xz * v.X + yz * v.Y + zz * v.Z);
                double len = next.Length;
                if (len < 1e-18)
                    break;
                eigenvalue = len;
                v = next / len;
            }
            return v.Normalized();
        }

        public static Vec3 AnyPerpendicular(Vec3 v)
        {
            Vec3 other = Math.Abs(v.Y) < 0.9 ? Vec3.UnitY : Vec3.UnitX;
            return Vec3.Cross(v, other).Normalized();
        }

        public static void PlaneBasis(Vec3 normal, out Vec3 u, out Vec3 v)
        {
            u = AnyPerpendicular(normal);
            v = Vec3.Cross(normal, u);
        }

        public static double DistanceToSegment(Vec3 p, Vec3 a, Vec3 b)
        {
            Vec3 ab = b - a;
            double len2 = ab.LengthSquared;
            double t = len2 > 1e-18 ? Math.Clamp(Vec3.Dot(p - a, ab) / len2, 0, 1) : 0;
            return Vec3.Distance(p, a + ab * t);
        }

        /// <summary>Closest point on segment ab to p, and its parameter t in [0, 1].</summary>
        public static Vec3 ClosestOnSegment(Vec3 p, Vec3 a, Vec3 b, out double t)
        {
            Vec3 ab = b - a;
            double len2 = ab.LengthSquared;
            t = len2 > 1e-18 ? Math.Clamp(Vec3.Dot(p - a, ab) / len2, 0, 1) : 0;
            return a + ab * t;
        }

        public static double SignedArea(IReadOnlyList<Vec2> poly)
        {
            double a = 0;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                a += poly[j].X * poly[i].Y - poly[i].X * poly[j].Y;
            return a * 0.5;
        }

        /// <summary>
        /// Ear-clipping triangulation that clips the ear with the shortest new diagonal each time, so a long
        /// strip becomes a zigzag of short braces rather than a fan from one corner. Points on a candidate
        /// diagonal block it. Returns index triples into poly.
        /// </summary>
        public static List<int> Triangulate(IReadOnlyList<Vec2> poly)
        {
            var tris = new List<int>();
            int n = poly.Count;
            if (n < 3)
                return tris;
            double sign = SignedArea(poly) >= 0 ? 1 : -1;
            double size = 0;
            foreach (var p in poly)
                size = Math.Max(size, Math.Max(Math.Abs(p.X), Math.Abs(p.Y)));
            double eps = 1e-9 * Math.Max(1, size * size);

            var idx = new List<int>(n);
            for (int i = 0; i < n; i++)
                idx.Add(i);

            while (idx.Count > 3)
            {
                int count = idx.Count;
                int fallback = 0;
                double fallbackArea = double.MinValue;
                int best = -1;
                double bestDiagonal = double.MaxValue;
                for (int i = 0; i < count; i++)
                {
                    int a = idx[(i + count - 1) % count], b = idx[i], c = idx[(i + 1) % count];
                    double area = Vec2.Cross(poly[b] - poly[a], poly[c] - poly[b]) * sign;
                    if (area > fallbackArea)
                    {
                        fallbackArea = area;
                        fallback = i;
                    }
                    if (area <= eps)
                        continue;
                    double diagonal = Vec2.DistanceSquared(poly[a], poly[c]);
                    if (diagonal >= bestDiagonal)
                        continue;
                    bool blocked = false;
                    for (int k = 0; k < count && !blocked; k++)
                    {
                        int p = idx[k];
                        if (p != a && p != b && p != c)
                            blocked = InTriangle(poly[p], poly[a], poly[b], poly[c], sign, eps);
                    }
                    if (blocked)
                        continue;
                    bestDiagonal = diagonal;
                    best = i;
                }
                int clip = best >= 0 ? best : fallback;
                tris.Add(idx[(clip + count - 1) % count]);
                tris.Add(idx[clip]);
                tris.Add(idx[(clip + 1) % count]);
                idx.RemoveAt(clip);
            }
            tris.Add(idx[0]);
            tris.Add(idx[1]);
            tris.Add(idx[2]);
            return tris;
        }

        static bool InTriangle(Vec2 p, Vec2 a, Vec2 b, Vec2 c, double sign, double eps)
        {
            double d1 = Vec2.Cross(b - a, p - a) * sign;
            double d2 = Vec2.Cross(c - b, p - b) * sign;
            double d3 = Vec2.Cross(a - c, p - c) * sign;
            return d1 >= -eps && d2 >= -eps && d3 >= -eps;
        }

        /// <summary>Barycentric weights of p projected onto triangle abc in the horizontal (XZ) plane.</summary>
        public static bool BarycentricXZ(Vec3 p, Vec3 a, Vec3 b, Vec3 c, out double wa, out double wb, out double wc)
        {
            double v0x = b.X - a.X, v0z = b.Z - a.Z, v1x = c.X - a.X, v1z = c.Z - a.Z, v2x = p.X - a.X, v2z = p.Z - a.Z;
            double den = v0x * v1z - v1x * v0z;
            wa = wb = wc = 0;
            if (Math.Abs(den) < 1e-12)
                return false;
            wb = (v2x * v1z - v1x * v2z) / den;
            wc = (v0x * v2z - v2x * v0z) / den;
            wa = 1 - wb - wc;
            const double e = -1e-6;
            return wa >= e && wb >= e && wc >= e;
        }

        /// <summary>Resamples a polyline at a fixed step, keeping both ends.</summary>
        public static List<Vec3> Resample(IReadOnlyList<Vec3> pts, double step)
        {
            var output = new List<Vec3> { pts[0] };
            double carried = 0;
            for (int i = 1; i < pts.Count; i++)
            {
                Vec3 a = pts[i - 1], b = pts[i];
                double seg = Vec3.Distance(a, b);
                if (seg < 1e-12)
                    continue;
                double t = step - carried;
                while (t <= seg)
                {
                    output.Add(Vec3.Lerp(a, b, t / seg));
                    t += step;
                }
                carried = seg - (t - step);
            }
            if (Vec3.Distance(output[output.Count - 1], pts[pts.Count - 1]) > step * 0.25)
                output.Add(pts[pts.Count - 1]);
            else
                output[output.Count - 1] = pts[pts.Count - 1];
            return output;
        }

        public static double PathLength(IReadOnlyList<Vec3> pts)
        {
            double len = 0;
            for (int i = 1; i < pts.Count; i++)
                len += Vec3.Distance(pts[i - 1], pts[i]);
            return len;
        }
    }
}
