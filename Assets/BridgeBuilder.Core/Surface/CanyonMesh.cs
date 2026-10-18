using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>What a piece of the canyon is, so its color can change (snow, town lights) without rebuilding it.</summary>
    public enum CanyonSurface : byte
    {
        RockLight,
        RockDark,
        Clifftop,
        Road,
        Floor,
        River,
        Base,
        House,
        Roof,
        Window,
        Pole,
        HouseBlue,
        HouseYellow,
        Brick,
        RoofDark,       // slate
        Door,
        Pavement,       // sidewalks, the town square, the fountain
        Trunk,
        Leaves,
        FountainWater,
    }

    /// <summary>An 8-bit color (Core has no engine color type).</summary>
    public readonly struct Rgb
    {
        public readonly byte R, G, B;

        public Rgb(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        public static Rgb Lerp(Rgb a, Rgb b, double t) => new Rgb(
            (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    /// <summary>The canyon's colors, plain and under snow, and the town's windows dark or lit.</summary>
    public static class CanyonPalette
    {
        static readonly Rgb Snow = new Rgb(240, 244, 250);

        public static Rgb Color(CanyonSurface s, bool facesUp, bool snowy, bool lit)
        {
            var c = s switch
            {
                CanyonSurface.RockLight => new Rgb(196, 120, 72),
                CanyonSurface.RockDark => new Rgb(160, 90, 56),
                CanyonSurface.Clifftop => new Rgb(184, 154, 96),
                CanyonSurface.Road => new Rgb(70, 70, 76),
                CanyonSurface.Floor => new Rgb(214, 186, 128),
                CanyonSurface.River => new Rgb(52, 128, 196),
                CanyonSurface.Base => new Rgb(92, 64, 44),
                CanyonSurface.House => new Rgb(236, 226, 206),
                CanyonSurface.Roof => new Rgb(176, 64, 48),
                CanyonSurface.Window => lit ? new Rgb(255, 214, 96) : new Rgb(64, 84, 104),
                CanyonSurface.HouseBlue => new Rgb(150, 186, 214),
                CanyonSurface.HouseYellow => new Rgb(236, 206, 120),
                CanyonSurface.Brick => new Rgb(168, 92, 70),
                CanyonSurface.RoofDark => new Rgb(84, 92, 108),
                CanyonSurface.Door => new Rgb(96, 64, 40),
                CanyonSurface.Pavement => new Rgb(176, 172, 164),
                CanyonSurface.Trunk => new Rgb(110, 76, 48),
                CanyonSurface.Leaves => new Rgb(62, 128, 72),
                CanyonSurface.FountainWater => new Rgb(70, 150, 214),
                _ => new Rgb(200, 200, 205),
            };
            if (snowy && facesUp && s != CanyonSurface.River && s != CanyonSurface.FountainWater && s != CanyonSurface.Window)
                return s == CanyonSurface.Road ? Rgb.Lerp(c, Snow, 0.6) : Snow;
            return c;
        }
    }

    /// <summary>
    /// The canyon diorama as a flat-shaded triangle mesh (room units): two clifftops with the road running onto each
    /// end, rock walls in layers dropping to a sandy floor with a river along the canyon, a base slab, and a small
    /// town on the far clifftop. Every triangle has its own three vertices and face normal (the low-poly look), and
    /// every vertex records its surface so the colors can be redone without rebuilding. Deterministic.
    /// </summary>
    public sealed class CanyonMesh
    {
        const double Cell = 0.05;              // clifftop and wall grid
        const double WallJag = 0.02;           // walls jag up to this far into the cliff, never into the canyon
        const double TopInset = 0.003;         // clifftops sit just under the ends so the pads drawn on them don't flicker
        const double RiverSink = 0.008;
        const double LayerHeight = 0.05;       // rock strata

        public readonly List<Vec3> Positions = new List<Vec3>();
        public readonly List<Vec3> Normals = new List<Vec3>();
        public readonly List<CanyonSurface> Surfaces = new List<CanyonSurface>();
        public readonly List<int> Triangles = new List<int>();

        /// <summary>The top of the town's flagpole, where the flag is drawn.</summary>
        public Vec3 FlagTop { get; private set; }

        public int TriangleCount => Triangles.Count / 3;

        /// <summary>Per-vertex colors for the current weather and lights.</summary>
        public void Colors(bool snowy, bool lit, Rgb[] into)
        {
            for (int i = 0; i < Positions.Count; i++)
                into[i] = CanyonPalette.Color(Surfaces[i], Normals[i].Y > 0.7, snowy, lit);
        }

        public static CanyonMesh Build(CanyonLayout layout)
        {
            var mesh = new CanyonMesh();
            new Builder(mesh, layout).Build();
            return mesh;
        }

        /// <summary>Holds the layout while the pieces are added; CanyonTown adds the town through it.</summary>
        internal sealed class Builder
        {
            readonly CanyonMesh _m;
            readonly CanyonLayout _c;
            readonly double _w, _back, _bottom, _road;

            public Builder(CanyonMesh mesh, CanyonLayout layout)
            {
                _m = mesh;
                _c = layout;
                _w = layout.HalfWidth;
                _back = Constants.Canyon.ClifftopDepthMeters;
                _bottom = layout.FloorY - Constants.Canyon.BaseThicknessMeters;
                _road = Constants.Surface.PadWidthMeters / 2;
            }

            public void Build()
            {
                Clifftop(-_back, 0, _c.NearTop - TopInset);
                Clifftop(_c.Span, _c.Span + _back, _c.FarTop - TopInset);
                Wall(0, _c.NearTop, -1);
                Wall(_c.Span, _c.FarTop, +1);
                Floor();
                Skirt();
                CanyonTown.Build(this, _c.FarTop - TopInset, _c.NearTop - TopInset);
            }

            internal CanyonLayout Layout => _c;
            internal double RoadHalfWidth => _road;
            internal void SetFlagTop(Vec3 top) => _m.FlagTop = top;

            internal Vec3 P(double along, double across, double y) => _c.Point(along, across, y);

            /// <summary>A flat-shaded triangle: its own three vertices, all with the face normal.</summary>
            internal void Tri(Vec3 a, Vec3 b, Vec3 c, CanyonSurface s)
            {
                Vec3 n = Vec3.Cross(b - a, c - a);
                double len = n.Length;
                if (!(len > 1e-12))
                    return;
                n /= len;
                if (n.Y < 0)
                    n = -n;                  // the shader lights both sides; an upward normal lets snow find the tops
                int start = _m.Positions.Count;
                _m.Positions.Add(a);
                _m.Positions.Add(b);
                _m.Positions.Add(c);
                for (int i = 0; i < 3; i++)
                {
                    _m.Normals.Add(n);
                    _m.Surfaces.Add(s);
                    _m.Triangles.Add(start + i);
                }
            }

            internal void Quad(Vec3 a, Vec3 b, Vec3 c, Vec3 d, CanyonSurface s)
            {
                Tri(a, b, c, s);
                Tri(a, c, d, s);
            }

            /// <summary>A clifftop between two along values, with the road strip down its middle.</summary>
            void Clifftop(double from, double to, double y)
            {
                int alongCells = Math.Max(1, (int)Math.Round((to - from) / Cell));
                int acrossCells = Math.Max(1, (int)Math.Round(2 * _w / Cell));
                for (int i = 0; i < alongCells; i++)
                {
                    double a0 = from + (to - from) * i / alongCells, a1 = from + (to - from) * (i + 1) / alongCells;
                    for (int j = 0; j < acrossCells; j++)
                    {
                        double c0 = -_w + 2 * _w * j / acrossCells, c1 = -_w + 2 * _w * (j + 1) / acrossCells;
                        var s = Math.Abs((c0 + c1) / 2) <= _road ? CanyonSurface.Road : CanyonSurface.Clifftop;
                        Quad(P(a0, c0, y), P(a1, c0, y), P(a1, c1, y), P(a0, c1, y), s);
                    }
                }
            }

            /// <summary>
            /// A rock wall from the clifftop edge down to the floor at `along`, jagged into the cliff (`into` is the
            /// along direction pointing into the rock), in alternating strata.
            /// </summary>
            void Wall(double along, double top, int into)
            {
                double y0 = top - TopInset, y1 = _c.FloorY;
                int rows = Math.Max(1, (int)Math.Round((y0 - y1) / Cell));
                int cols = Math.Max(1, (int)Math.Round(2 * _w / Cell));
                Vec3 Corner(int row, int col)
                {
                    double y = y0 + (y1 - y0) * row / rows;
                    double c = -_w + 2 * _w * col / cols;
                    bool edge = row == 0 || row == rows || col == 0 || col == cols;
                    double jag = edge ? 0 : WallJag * Hash(row, col, into);
                    return P(along + into * jag, c, y);
                }
                for (int r = 0; r < rows; r++)
                {
                    double mid = y0 + (y1 - y0) * (r + 0.5) / rows;
                    var s = ((int)Math.Floor((top - mid) / LayerHeight)) % 2 == 0 ? CanyonSurface.RockLight : CanyonSurface.RockDark;
                    for (int k = 0; k < cols; k++)
                        Quad(Corner(r, k), Corner(r, k + 1), Corner(r + 1, k + 1), Corner(r + 1, k), s);
                }
            }

            /// <summary>The sandy floor between the walls, the river sunk a little in its middle, and the river banks.</summary>
            void Floor()
            {
                var (r0, r1) = _c.River();
                double y = _c.FloorY, ry = y - RiverSink;
                Quad(P(0, -_w, y), P(r0, -_w, y), P(r0, _w, y), P(0, _w, y), CanyonSurface.Floor);
                Quad(P(r1, -_w, y), P(_c.Span, -_w, y), P(_c.Span, _w, y), P(r1, _w, y), CanyonSurface.Floor);
                Quad(P(r0, -_w, ry), P(r1, -_w, ry), P(r1, _w, ry), P(r0, _w, ry), CanyonSurface.River);
                Quad(P(r0, -_w, y), P(r0, _w, y), P(r0, _w, ry), P(r0, -_w, ry), CanyonSurface.Floor);
                Quad(P(r1, -_w, y), P(r1, _w, y), P(r1, _w, ry), P(r1, -_w, ry), CanyonSurface.Floor);
            }

            /// <summary>The diorama's outside: both sides follow the clifftops and floor down to the base; the backs close it.</summary>
            void Skirt()
            {
                double[] along = { -_back, 0, 0, _c.Span, _c.Span, _c.Span + _back };
                double[] top = { _c.NearTop - TopInset, _c.NearTop - TopInset, _c.FloorY, _c.FloorY, _c.FarTop - TopInset, _c.FarTop - TopInset };
                foreach (double side in new[] { -_w, _w })
                {
                    for (int i = 0; i + 1 < along.Length; i++)
                    {
                        if (along[i + 1] - along[i] <= 0)
                            continue;
                        Quad(P(along[i], side, top[i]), P(along[i + 1], side, top[i + 1]), P(along[i + 1], side, _bottom), P(along[i], side, _bottom), CanyonSurface.Base);
                    }
                }
                Quad(P(-_back, -_w, top[0]), P(-_back, _w, top[0]), P(-_back, _w, _bottom), P(-_back, -_w, _bottom), CanyonSurface.Base);
                double far = _c.Span + _back;
                Quad(P(far, -_w, top[5]), P(far, _w, top[5]), P(far, _w, _bottom), P(far, -_w, _bottom), CanyonSurface.Base);
                Quad(P(0, -_w, _c.FloorY), P(0, _w, _c.FloorY), P(0, _w, _bottom), P(0, -_w, _bottom), CanyonSurface.Base);
                Quad(P(_c.Span, -_w, _c.FloorY), P(_c.Span, _w, _c.FloorY), P(_c.Span, _w, _bottom), P(_c.Span, -_w, _bottom), CanyonSurface.Base);
            }

            /// <summary>An upright box standing on `foot` (its bottom center), sides along the canyon frame.</summary>
            internal void Box(Vec3 foot, double alongSize, double acrossSize, double height, CanyonSurface s)
            {
                Vec3 fa = _c.Forward * (alongSize / 2), ac = _c.Across * (acrossSize / 2), up = Vec3.UnitY * height;
                Vec3 b0 = foot - fa - ac, b1 = foot + fa - ac, b2 = foot + fa + ac, b3 = foot - fa + ac;
                Quad(b0, b1, b1 + up, b0 + up, s);
                Quad(b1, b2, b2 + up, b1 + up, s);
                Quad(b2, b3, b3 + up, b2 + up, s);
                Quad(b3, b0, b0 + up, b3 + up, s);
                Quad(b0 + up, b1 + up, b2 + up, b3 + up, s);
            }

            /// <summary>A four-sided pyramid on a base centered at `foot` (a spire, a tree's crown).</summary>
            internal void Pyramid(Vec3 foot, double alongSize, double acrossSize, double height, CanyonSurface s)
            {
                Vec3 fa = _c.Forward * (alongSize / 2), ac = _c.Across * (acrossSize / 2), tip = foot + Vec3.UnitY * height;
                Vec3 b0 = foot - fa - ac, b1 = foot + fa - ac, b2 = foot + fa + ac, b3 = foot - fa + ac;
                Tri(b0, b1, tip, s);
                Tri(b1, b2, tip, s);
                Tri(b2, b3, tip, s);
                Tri(b3, b0, tip, s);
            }

            /// <summary>A fixed pseudo-random number in [0, 1) per wall grid point.</summary>
            static double Hash(int row, int col, int salt)
            {
                unchecked
                {
                    uint h = (uint)row * 73856093u ^ (uint)col * 19349663u ^ (uint)(salt + 2) * 83492791u;
                    h ^= h >> 13;
                    h *= 1274126177u;
                    h ^= h >> 16;
                    return (h & 0xFFFF) / 65536.0;
                }
            }
        }
    }
}
