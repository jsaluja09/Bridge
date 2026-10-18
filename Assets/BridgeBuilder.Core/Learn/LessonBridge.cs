using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>What a part of a real bridge does with its load, shown by its color.</summary>
    public enum PartRole
    {
        Pull,       // tension: cables, hangers, stays, ties
        Push,       // compression: towers, piers, arches
        Bend,       // bending: decks and stiffening trusses
        Hold,       // anchors the rest: anchorages, foundations, the ground
        Scenery,    // water and shores: not structure
    }

    /// <summary>A straight piece of a part, real meters.</summary>
    public readonly struct Segment
    {
        public readonly Vec3 A, B;

        public Segment(Vec3 a, Vec3 b)
        {
            A = a;
            B = b;
        }
    }

    /// <summary>A flat triangle of a part (a deck surface, an anchorage block, the water), real meters.</summary>
    public readonly struct Face
    {
        public readonly Vec3 A, B, C;

        public Face(Vec3 a, Vec3 b, Vec3 c)
        {
            A = a;
            B = b;
            C = c;
        }
    }

    /// <summary>
    /// One named piece of a lesson bridge: what it is, what it does (its role), when it is built (its group), and
    /// its geometry as segments drawn as tubes of Radius plus faces. Flexible parts move in the load demos.
    /// </summary>
    public sealed class LessonPart
    {
        public string Id = "";
        public string Name = "";
        public PartRole Role;
        public int Group;
        public double Radius = 1;            // real meters, for the tubes
        public bool Flexible;                // moves with the deck in load demos
        public readonly List<Segment> Segments = new List<Segment>();
        public readonly List<Face> Faces = new List<Face>();

        /// <summary>Where a label for this part points: the middle of its geometry.</summary>
        public Vec3 Anchor()
        {
            Vec3 sum = Vec3.Zero;
            int n = 0;
            foreach (var s in Segments)
            {
                sum += s.A + s.B;
                n += 2;
            }
            foreach (var f in Faces)
            {
                sum += f.A + f.B + f.C;
                n += 3;
            }
            return n > 0 ? sum / n : Vec3.Zero;
        }
    }

    /// <summary>
    /// A famous bridge as lesson geometry, in real meters: x along the bridge, y up from the water, z across. The
    /// main span (MainSpanStart to MainSpanEnd) is the part that sags and sways in the load demos.
    /// </summary>
    public sealed class LessonBridge
    {
        public string Name = "";
        public double MainSpanStart;
        public double MainSpanEnd;
        public double DeckY;
        /// <summary>How far the middle of the main span drops under full traffic, real meters.</summary>
        public double LiveLoadSag;
        /// <summary>How far the middle of the main span swings sideways in a strong wind, real meters.</summary>
        public double WindSway;
        public readonly List<LessonPart> Parts = new List<LessonPart>();

        public double MainSpan => MainSpanEnd - MainSpanStart;

        public LessonPart Part(string id)
        {
            foreach (var p in Parts)
            {
                if (p.Id == id)
                    return p;
            }
            throw new ArgumentException($"No part '{id}' on {Name}");
        }

        public LessonPart Add(string id, string name, PartRole role, int group, double radius, bool flexible = false)
        {
            var part = new LessonPart { Id = id, Name = name, Role = role, Group = group, Radius = radius, Flexible = flexible };
            Parts.Add(part);
            return part;
        }

        /// <summary>The smallest box around every part, real meters.</summary>
        public (Vec3 min, Vec3 max) Bounds()
        {
            var min = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
            var max = new Vec3(double.MinValue, double.MinValue, double.MinValue);
            void Grow(Vec3 p)
            {
                min = new Vec3(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                max = new Vec3(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
            }
            foreach (var part in Parts)
            {
                foreach (var s in part.Segments)
                {
                    Grow(s.A);
                    Grow(s.B);
                }
                foreach (var f in part.Faces)
                {
                    Grow(f.A);
                    Grow(f.B);
                    Grow(f.C);
                }
            }
            return (min, max);
        }

        /// <summary>The scale that fits the bridge's length onto a table of the given length (room meters per real meter).</summary>
        public double FitScale(double tableLength)
        {
            var (min, max) = Bounds();
            return tableLength / (max.X - min.X);
        }
    }
}
