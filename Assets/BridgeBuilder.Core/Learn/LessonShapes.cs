using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>Geometry helpers the lesson bridges are built from, real meters.</summary>
    public static class LessonShapes
    {
        /// <summary>
        /// Points on a cable from a to b that hangs `sag` below the straight line at its middle: an evenly loaded
        /// cable hangs in a parabola, y drops by 4 sag u (1 - u) at fraction u of the way.
        /// </summary>
        public static List<Vec3> Cable(Vec3 a, Vec3 b, double sag, int pieces)
        {
            var points = new List<Vec3>(pieces + 1);
            for (int i = 0; i <= pieces; i++)
            {
                double u = (double)i / pieces;
                points.Add(Vec3.Lerp(a, b, u) - Vec3.UnitY * (4 * sag * u * (1 - u)));
            }
            return points;
        }

        /// <summary>Height of that cable above x (a and b at the same z), for hanging hangers from it.</summary>
        public static double CableY(Vec3 a, Vec3 b, double sag, double x)
        {
            double u = (x - a.X) / (b.X - a.X);
            return a.Y + (b.Y - a.Y) * u - 4 * sag * u * (1 - u);
        }

        /// <summary>A parabolic arch from a to b rising `rise` above the line between them.</summary>
        public static List<Vec3> Arch(Vec3 a, Vec3 b, double rise, int pieces) => Cable(a, b, -rise, pieces);

        public static void Polyline(LessonPart part, IReadOnlyList<Vec3> points)
        {
            for (int i = 1; i < points.Count; i++)
                part.Segments.Add(new Segment(points[i - 1], points[i]));
        }

        public static void Line(LessonPart part, Vec3 a, Vec3 b) => part.Segments.Add(new Segment(a, b));

        public static void Quad(LessonPart part, Vec3 a, Vec3 b, Vec3 c, Vec3 d)
        {
            part.Faces.Add(new Face(a, b, c));
            part.Faces.Add(new Face(a, c, d));
        }

        /// <summary>A solid block from min to max (all six sides).</summary>
        public static void Box(LessonPart part, Vec3 min, Vec3 max)
        {
            Vec3 P(double x, double y, double z) => new Vec3(x, y, z);
            double x0 = min.X, y0 = min.Y, z0 = min.Z, x1 = max.X, y1 = max.Y, z1 = max.Z;
            Quad(part, P(x0, y0, z0), P(x1, y0, z0), P(x1, y1, z0), P(x0, y1, z0));
            Quad(part, P(x0, y0, z1), P(x1, y0, z1), P(x1, y1, z1), P(x0, y1, z1));
            Quad(part, P(x0, y0, z0), P(x0, y0, z1), P(x0, y1, z1), P(x0, y1, z0));
            Quad(part, P(x1, y0, z0), P(x1, y0, z1), P(x1, y1, z1), P(x1, y1, z0));
            Quad(part, P(x0, y1, z0), P(x1, y1, z0), P(x1, y1, z1), P(x0, y1, z1));
            Quad(part, P(x0, y0, z0), P(x1, y0, z0), P(x1, y0, z1), P(x0, y0, z1));
        }

        /// <summary>
        /// A flat truss between two chords along x at height `top` and `top - depth`, side z: both chords, a
        /// vertical every panel, and a diagonal in each panel alternating direction (a Warren with verticals).
        /// </summary>
        public static void Truss(LessonPart part, double x0, double x1, double top, double depth, double z, double panel)
        {
            int panels = Math.Max(1, (int)Math.Round((x1 - x0) / panel));
            double bottom = top - depth;
            Line(part, new Vec3(x0, top, z), new Vec3(x1, top, z));
            Line(part, new Vec3(x0, bottom, z), new Vec3(x1, bottom, z));
            for (int i = 0; i <= panels; i++)
            {
                double x = x0 + (x1 - x0) * i / panels;
                Line(part, new Vec3(x, top, z), new Vec3(x, bottom, z));
                if (i == panels)
                    continue;
                double xn = x0 + (x1 - x0) * (i + 1) / panels;
                if (i % 2 == 0)
                    Line(part, new Vec3(x, top, z), new Vec3(xn, bottom, z));
                else
                    Line(part, new Vec3(x, bottom, z), new Vec3(xn, top, z));
            }
        }

        /// <summary>Vertical hangers every `spacing` from a cable (a to b, sag) down to the deck at deckY, for x in (from, to).</summary>
        public static void Hangers(LessonPart part, Vec3 a, Vec3 b, double sag, double deckY, double from, double to, double spacing)
        {
            for (double x = from + spacing; x < to - spacing / 2; x += spacing)
            {
                double y = CableY(a, b, sag, x);
                if (y > deckY + 0.5)
                    Line(part, new Vec3(x, deckY, a.Z), new Vec3(x, y, a.Z));
            }
        }
    }
}
