using System;
using System.Collections.Generic;
using System.Numerics;

namespace BridgeBuilder.Core
{
    public enum ElementKind
    {
        Line,       // part of a drawn line
        Bracing,    // hidden fill bracing
    }

    /// <summary>A road-or-not fill compiled to solver nodes. TriEdges: element per triangle edge, -1 = surface edge.</summary>
    public sealed class FillGeometry
    {
        public int DesignId;
        public bool IsRoad;
        public int[] Tris;
        public int[] TriEdges;
    }

    public struct SurfaceGeometry
    {
        public Vector3 EdgeA;     // bridge world
        public Vector3 EdgeB;
        public Vector3 Back;
        public float Height;
        public float Depth;
        public Vector3 Mid => (EdgeA + EdgeB) * 0.5f;

        /// <summary>True if p (bridge world) is over this surface's top.</summary>
        public bool Contains(Vector3 p)
        {
            Vector3 edge = EdgeB - EdgeA;
            float len = edge.Length();
            float along = Vector3.Dot(p - EdgeA, edge / len);
            float back = Vector3.Dot(p - EdgeA, Back);
            return along >= -0.5f && along <= len + 0.5f && back >= -0.05f && back <= Depth;
        }
    }

    /// <summary>
    /// A design compiled for simulation: bridge-world positions (room minus Origin, times Scale), elements,
    /// members for buckling length, lumped masses, fills and static loads. Immutable once built, so the
    /// worker thread and the renderer can share it.
    /// </summary>
    public sealed class BridgeStructure
    {
        public BridgeSettings Settings;
        public Challenge Challenge;
        public Vector3 Origin;
        public float Scale;
        public int DesignVersion;

        // Nodes
        public int NodeCount;
        public int[] DesignNode;
        public Vector3[] Rest;               // bridge world
        public bool[] Fixed;
        public float[] Mass;
        public float[] RotaryInertia;
        public Vector3[] StaticLoad;         // gravity + wind
        public int[] NodeElementStart;       // CSR adjacency: elements at node i are NodeElements[start[i]..start[i+1])
        public int[] NodeElements;

        // Elements
        public int ElementCount;
        public int[] ElemA;
        public int[] ElemB;
        public ElementKind[] Kind;
        public int[] ElemLine;               // design line id, -1 for bracing
        public float[] Length;
        public float[] BucklingLength;       // length of the member (run between junctions) it belongs to

        // Section (one constant section in the MVP)
        public double Area, Inertia, Torsion, SectionModulus, YieldForce, YieldMoment;

        public readonly List<FillGeometry> Fills = new List<FillGeometry>();
        public readonly SurfaceGeometry[] Surfaces = new SurfaceGeometry[2];
        public float GroundY;
        public int StartSurface;
        public Vector3 WindDirection;        // unit, horizontal
        public Vector3 GapDirection;         // unit, horizontal, start surface toward far surface

        public Vector3 ToRoom(Vector3 world) => Origin + world / Scale;
        public Vector3 ToWorld(Vector3 room) => (room - Origin) * Scale;

        public double EulerLoad(int element)
        {
            double l = BucklingLength[element];
            return Math.PI * Math.PI * Settings.YoungsModulus * Inertia / (l * l);
        }

        public static BridgeStructure Compile(BridgeDesign d, Challenge challenge)
        {
            var s = d.Settings;
            var st = new BridgeStructure
            {
                Settings = s,
                Challenge = challenge ?? new Challenge(),
                Origin = d.Surfaces[0].Mid,
                Scale = s.Scale,
                DesignVersion = d.Version,
                StartSurface = d.StartSurface,
            };

            // Section: a round steel tube.
            double D = s.TubeDiameter, t = s.TubeWall, di = D - 2 * t;
            st.Area = Math.PI * (D * D - di * di) / 4;
            st.Inertia = Math.PI * (Math.Pow(D, 4) - Math.Pow(di, 4)) / 64;
            st.Torsion = 2 * st.Inertia;
            st.SectionModulus = st.Inertia / (D / 2);
            st.YieldForce = s.YieldStrength * st.Area;
            st.YieldMoment = s.YieldStrength * st.SectionModulus;

            // Elements from lines, then fill bracing.
            var elems = new List<(int a, int b, ElementKind kind, int line)>();
            foreach (var line in d.Lines)
            {
                for (int i = 0; i + 1 < line.Nodes.Count; i++)
                    elems.Add((line.Nodes[i], line.Nodes[i + 1], ElementKind.Line, line.Id));
            }
            foreach (var fill in d.Fills)
            {
                foreach (var (a, b) in fill.Bracing)
                    elems.Add((a, b, ElementKind.Bracing, -1));
            }
            elems.RemoveAll(e => d.GetNode(e.a).IsFixed && d.GetNode(e.b).IsFixed);

            // Nodes: only those something uses.
            var index = new Dictionary<int, int>();
            var designIds = new List<int>();
            int Use(int id)
            {
                if (!index.TryGetValue(id, out int i))
                {
                    i = designIds.Count;
                    index[id] = i;
                    designIds.Add(id);
                }
                return i;
            }
            foreach (var e in elems)
            {
                Use(e.a);
                Use(e.b);
            }
            foreach (var fill in d.Fills)
            {
                foreach (int id in fill.Boundary)
                    Use(id);
            }

            int n = designIds.Count, m = elems.Count;
            st.NodeCount = n;
            st.DesignNode = designIds.ToArray();
            st.Rest = new Vector3[n];
            st.Fixed = new bool[n];
            st.Mass = new float[n];
            st.RotaryInertia = new float[n];
            st.StaticLoad = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var node = d.GetNode(designIds[i]);
                st.Rest[i] = st.ToWorld(node.Position);
                st.Fixed[i] = node.IsFixed;
            }

            st.ElementCount = m;
            st.ElemA = new int[m];
            st.ElemB = new int[m];
            st.Kind = new ElementKind[m];
            st.ElemLine = new int[m];
            st.Length = new float[m];
            st.BucklingLength = new float[m];
            var pairToElement = new Dictionary<(int, int), int>();
            for (int k = 0; k < m; k++)
            {
                var e = elems[k];
                int a = index[e.a], b = index[e.b];
                st.ElemA[k] = a;
                st.ElemB[k] = b;
                st.Kind[k] = e.kind;
                st.ElemLine[k] = e.line;
                st.Length[k] = Math.Max(1e-3f, Vector3.Distance(st.Rest[a], st.Rest[b]));
                pairToElement[a < b ? (a, b) : (b, a)] = k;

                float mass = (float)(s.Density * st.Area * st.Length[k]);
                st.Mass[a] += mass * 0.5f;
                st.Mass[b] += mass * 0.5f;
                float rot = mass * 0.5f * st.Length[k] * st.Length[k] / 12f;
                st.RotaryInertia[a] += rot;
                st.RotaryInertia[b] += rot;
            }

            st.BuildAdjacency();
            st.ComputeBucklingLengths(d, index);
            st.CompileFills(d, index, pairToElement);
            st.CompileSurfaces(d);

            for (int i = 0; i < n; i++)
            {
                st.Mass[i] = Math.Max(st.Mass[i], 1f);
                st.RotaryInertia[i] = Math.Max(st.RotaryInertia[i], 1f);
            }

            var gap = d.Frame.U;
            st.GapDirection = d.StartSurface == 0 ? gap : -gap;
            float windAngle = st.Challenge.WindDirectionDeg * MathF.PI / 180f;
            st.WindDirection = Vector3.Normalize(gap * MathF.Cos(windAngle) + d.Frame.Side * MathF.Sin(windAngle));
            st.ComputeStaticLoads();
            return st;
        }

        void BuildAdjacency()
        {
            NodeElementStart = new int[NodeCount + 1];
            for (int k = 0; k < ElementCount; k++)
            {
                NodeElementStart[ElemA[k] + 1]++;
                NodeElementStart[ElemB[k] + 1]++;
            }
            for (int i = 0; i < NodeCount; i++)
                NodeElementStart[i + 1] += NodeElementStart[i];
            NodeElements = new int[NodeElementStart[NodeCount]];
            var fill = (int[])NodeElementStart.Clone();
            for (int k = 0; k < ElementCount; k++)
            {
                NodeElements[fill[ElemA[k]]++] = k;
                NodeElements[fill[ElemB[k]]++] = k;
            }
        }

        public int Degree(int node) => NodeElementStart[node + 1] - NodeElementStart[node];

        /// <summary>
        /// A member is a run of a line's elements between junctions (supports, line ends, or nodes where
        /// three or more elements meet). Buckling uses the member's length, not the 10 cm element's.
        /// </summary>
        void ComputeBucklingLengths(BridgeDesign d, Dictionary<int, int> index)
        {
            for (int k = 0; k < ElementCount; k++)
                BucklingLength[k] = Length[k];

            var ks = new List<int>();
            foreach (var line in d.Lines)
            {
                // This line's elements in order (support-to-support pieces were dropped, so check continuity).
                ks.Clear();
                for (int k = 0; k < ElementCount; k++)
                {
                    if (ElemLine[k] == line.Id)
                        ks.Add(k);
                }
                int runStart = 0;
                for (int i = 0; i < ks.Count; i++)
                {
                    int k = ks[i];
                    bool endHere = i == ks.Count - 1 || IsJunction(ElemB[k]) || ElemB[k] != ElemA[ks[i + 1]];
                    if (!endHere)
                        continue;
                    float length = 0f;
                    for (int j = runStart; j <= i; j++)
                        length += Length[ks[j]];
                    for (int j = runStart; j <= i; j++)
                        BucklingLength[ks[j]] = length;
                    runStart = i + 1;
                }
            }
        }

        bool IsJunction(int node) => Fixed[node] || Degree(node) >= 3;

        void CompileFills(BridgeDesign d, Dictionary<int, int> index, Dictionary<(int, int), int> pairToElement)
        {
            float deck = Settings.DeckMassPerArea;
            foreach (var fill in d.Fills)
            {
                var g = new FillGeometry
                {
                    DesignId = fill.Id,
                    IsRoad = fill.IsRoad,
                    Tris = new int[fill.Triangles.Count],
                    TriEdges = new int[fill.Triangles.Count],
                };
                for (int i = 0; i < fill.Triangles.Count; i++)
                    g.Tris[i] = index[fill.Triangles[i]];
                for (int t = 0; t < g.Tris.Length; t += 3)
                {
                    for (int e = 0; e < 3; e++)
                    {
                        int a = g.Tris[t + e], b = g.Tris[t + (e + 1) % 3];
                        g.TriEdges[t + e] = pairToElement.TryGetValue(a < b ? (a, b) : (b, a), out int k) ? k : -1;
                    }

                    // Deck mass, a third of each triangle to each corner.
                    Vector3 pa = Rest[g.Tris[t]], pb = Rest[g.Tris[t + 1]], pc = Rest[g.Tris[t + 2]];
                    float area = Vector3.Cross(pb - pa, pc - pa).Length() * 0.5f;
                    for (int c = 0; c < 3; c++)
                        Mass[g.Tris[t + c]] += area * deck / 3f;
                }
                Fills.Add(g);
            }
        }

        void CompileSurfaces(BridgeDesign d)
        {
            for (int i = 0; i < 2; i++)
            {
                var s = d.Surfaces[i];
                Surfaces[i] = new SurfaceGeometry
                {
                    EdgeA = ToWorld(s.EdgeA),
                    EdgeB = ToWorld(s.EdgeB),
                    Back = s.Back,
                    Height = (s.Height - Origin.Y) * Scale,
                    Depth = Settings.SurfaceDepth * Scale,
                };
            }
            GroundY = (d.GroundY - Origin.Y) * Scale;
        }

        void ComputeStaticLoads()
        {
            var s = Settings;
            for (int i = 0; i < NodeCount; i++)
                StaticLoad[i] = new Vector3(0f, -Mass[i] * s.Gravity, 0f);

            float v = Challenge.WindSpeed;
            if (v <= 0f)
                return;
            float q = 0.5f * s.AirDensity * v * v;
            Vector3 w = WindDirection;

            for (int k = 0; k < ElementCount; k++)
            {
                Vector3 axis = Vector3.Normalize(Rest[ElemB[k]] - Rest[ElemA[k]]);
                Vector3 perp = w - Vector3.Dot(w, axis) * axis;          // only the cross-flow part pushes
                Vector3 f = perp * (q * s.MemberDragCoefficient * s.TubeDiameter * Length[k]);
                StaticLoad[ElemA[k]] += f * 0.5f;
                StaticLoad[ElemB[k]] += f * 0.5f;
            }
            foreach (var g in Fills)
            {
                for (int t = 0; t < g.Tris.Length; t += 3)
                {
                    Vector3 pa = Rest[g.Tris[t]], pb = Rest[g.Tris[t + 1]], pc = Rest[g.Tris[t + 2]];
                    Vector3 cross = Vector3.Cross(pb - pa, pc - pa);
                    float area = cross.Length() * 0.5f;
                    if (area < 1e-6f)
                        continue;
                    Vector3 normal = cross / (2f * area);
                    Vector3 f = normal * (q * s.FillDragCoefficient * area * Vector3.Dot(w, normal));
                    for (int c = 0; c < 3; c++)
                        StaticLoad[g.Tris[t + c]] += f / 3f;
                }
            }
        }
    }
}
