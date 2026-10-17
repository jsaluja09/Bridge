using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BridgeBuilder.Core
{
    public enum NodeKind
    {
        Free,
        SurfaceAnchor,
        GroundAnchor,
    }

    public enum StrokeShape
    {
        Line,
        Arc,
        Polyline,
    }

    /// <summary>A snap point. Every node is a solver node (spec 2.3). Anchors are supports.</summary>
    public sealed class Node
    {
        public int Id;
        public Vector3 Position;          // room units
        public NodeKind Kind;
        public int Surface = -1;          // which surface an anchor belongs to
        public int OwnerLine = -1;        // the line that created this node, -1 for anchors

        public bool IsFixed => Kind != NodeKind.Free;
    }

    /// <summary>A drawn line: a chain of nodes; each consecutive pair is one frame element.</summary>
    public sealed class Line
    {
        public int Id;
        public StrokeShape Shape;
        public List<int> Nodes = new List<int>();
        public float Section = 1f;        // per-member section multiplier, constant in the MVP (spec 2.7)
    }

    /// <summary>A filled, planar, closed polygon: a visible surface plus hidden bracing (spec 2.4).</summary>
    public sealed class Fill
    {
        public int Id;
        public List<int> Corners = new List<int>();     // what the player tapped
        public List<int> Boundary = new List<int>();    // every node around the loop
        public List<int> Triangles = new List<int>();   // node id triples
        public List<(int a, int b)> Bracing = new List<(int, int)>();
        public Vector3 Normal;
        public float Area;                              // room m^2
        public bool IsRoad;

        public float SlopeDeg => MathF.Acos(Math.Clamp(MathF.Abs(Normal.Y), 0f, 1f)) * (180f / MathF.PI);
    }

    /// <summary>A marked real surface: the edge facing the gap and its anchor dots.</summary>
    public sealed class Surface
    {
        public int Index;
        public Vector3 EdgeA;
        public Vector3 EdgeB;
        public Vector3 Back;              // horizontal, pointing away from the gap
        public float Height;
        public readonly List<int> Anchors = new List<int>();   // ordered from EdgeA to EdgeB

        public Vector3 Mid => (EdgeA + EdgeB) * 0.5f;
        public float EdgeLength => Vector3.Distance(EdgeA, EdgeB);
    }

    /// <summary>
    /// The player's bridge, in room units. Holds nodes, lines, fills and the two surfaces.
    /// Change it through BridgeEditing so every action is undoable.
    /// </summary>
    public sealed class BridgeDesign
    {
        public readonly BridgeSettings Settings;
        public readonly List<Node> Nodes = new List<Node>();
        public readonly List<Line> Lines = new List<Line>();
        public readonly List<Fill> Fills = new List<Fill>();
        public readonly Surface[] Surfaces = { new Surface { Index = 0 }, new Surface { Index = 1 } };
        public readonly List<int> GroundAnchors = new List<int>();
        public float GroundY;
        public int StartSurface;
        public BridgeFrame Frame { get; private set; }

        readonly Dictionary<int, Node> _nodes = new Dictionary<int, Node>();
        readonly Dictionary<int, Line> _lines = new Dictionary<int, Line>();
        readonly Dictionary<int, Fill> _fills = new Dictionary<int, Fill>();
        int _nextId = 1;

        /// <summary>Bumped on every change, so views and previews know when to refresh.</summary>
        public int Version { get; private set; }

        public BridgeDesign(BridgeSettings settings) => Settings = settings;

        /// <summary>
        /// Four taps: both ends of each surface's edge facing the gap (spec 2.2). groundY is the real floor.
        /// </summary>
        public static BridgeDesign Create(BridgeSettings settings, Vector3 startA, Vector3 startB, Vector3 farA, Vector3 farB, float groundY)
        {
            var d = new BridgeDesign(settings) { GroundY = groundY };
            Vector3 mid0 = (startA + startB) * 0.5f, mid1 = (farA + farB) * 0.5f;
            d.Frame = new BridgeFrame(mid0, mid1 - mid0, Vector3.UnitY);

            d.SetupSurface(0, startA, startB, mid1, true);
            d.SetupSurface(1, farA, farB, mid0, true);
            d.SetupGround(new[] { startA, startB, farA, farB });
            return d;
        }

        /// <summary>Plain-data copy for JSON test bridges and saved designs (spec section 3). Not used in the hot path.</summary>
        public BridgeDesignData ToData()
        {
            var data = new BridgeDesignData
            {
                Taps = new[] { Surfaces[0].EdgeA, Surfaces[0].EdgeB, Surfaces[1].EdgeA, Surfaces[1].EdgeB }.SelectMany(XYZ).ToArray(),
                GroundY = GroundY,
                StartSurface = StartSurface,
                NextId = _nextId,
            };
            foreach (var n in Nodes)
                data.Nodes.Add(new BridgeDesignData.NodeData { Id = n.Id, Position = XYZ(n.Position), Kind = n.Kind, Surface = n.Surface, OwnerLine = n.OwnerLine });
            foreach (var l in Lines)
                data.Lines.Add(new BridgeDesignData.LineData { Id = l.Id, Shape = l.Shape, Nodes = new List<int>(l.Nodes), Section = l.Section });
            foreach (var f in Fills)
            {
                data.Fills.Add(new BridgeDesignData.FillData
                {
                    Id = f.Id, Corners = new List<int>(f.Corners), Boundary = new List<int>(f.Boundary), Triangles = new List<int>(f.Triangles),
                    Bracing = f.Bracing.SelectMany(p => new[] { p.a, p.b }).ToList(), Normal = XYZ(f.Normal), Area = f.Area, IsRoad = f.IsRoad,
                });
            }
            return data;
        }

        public static BridgeDesign FromData(BridgeSettings settings, BridgeDesignData data)
        {
            Vector3 Tap(int i) => new Vector3(data.Taps[i * 3], data.Taps[i * 3 + 1], data.Taps[i * 3 + 2]);
            var d = new BridgeDesign(settings) { GroundY = data.GroundY, StartSurface = data.StartSurface };
            Vector3 mid0 = (Tap(0) + Tap(1)) * 0.5f, mid1 = (Tap(2) + Tap(3)) * 0.5f;
            d.Frame = new BridgeFrame(mid0, mid1 - mid0, Vector3.UnitY);
            d.SetupSurface(0, Tap(0), Tap(1), mid1, false);
            d.SetupSurface(1, Tap(2), Tap(3), mid0, false);

            foreach (var n in data.Nodes)
            {
                d.AddNode(new Node { Id = n.Id, Position = V(n.Position), Kind = n.Kind, Surface = n.Surface, OwnerLine = n.OwnerLine });
                if (n.Kind == NodeKind.SurfaceAnchor)
                    d.Surfaces[n.Surface].Anchors.Add(n.Id);
                else if (n.Kind == NodeKind.GroundAnchor)
                    d.GroundAnchors.Add(n.Id);
            }
            foreach (var l in data.Lines)
                d.AddLine(new Line { Id = l.Id, Shape = l.Shape, Nodes = new List<int>(l.Nodes), Section = l.Section });
            foreach (var f in data.Fills)
            {
                var fill = new Fill { Id = f.Id, Normal = V(f.Normal), Area = f.Area, IsRoad = f.IsRoad };
                fill.Corners.AddRange(f.Corners);
                fill.Boundary.AddRange(f.Boundary);
                fill.Triangles.AddRange(f.Triangles);
                for (int i = 0; i + 1 < f.Bracing.Count; i += 2)
                    fill.Bracing.Add((f.Bracing[i], f.Bracing[i + 1]));
                d.AddFill(fill);
            }
            d._nextId = data.NextId;
            return d;
        }

        static float[] XYZ(Vector3 v) => new[] { v.X, v.Y, v.Z };
        static Vector3 V(float[] a) => new Vector3(a[0], a[1], a[2]);

        void SetupSurface(int index, Vector3 a, Vector3 b, Vector3 otherMid, bool createAnchors)
        {
            var s = Surfaces[index];
            s.EdgeA = a;
            s.EdgeB = b;
            s.Height = (a.Y + b.Y) * 0.5f;
            Vector3 edge = new Vector3(b.X - a.X, 0f, b.Z - a.Z);
            Vector3 back = edge.LengthSquared() > 1e-8f
                ? Vector3.Normalize(Vector3.Cross(Vector3.UnitY, edge))
                : -Frame.U;
            Vector3 toOther = otherMid - s.Mid;
            if (Vector3.Dot(back, toOther) > 0f)
                back = -back;
            s.Back = back;
            if (!createAnchors)
                return;

            int segments = Math.Max(1, (int)MathF.Round(s.EdgeLength / Settings.SnapSpacing));
            for (int i = 0; i <= segments; i++)
            {
                var node = new Node { Id = NewId(), Position = Vector3.Lerp(a, b, i / (float)segments), Kind = NodeKind.SurfaceAnchor, Surface = index };
                AddNode(node);
                s.Anchors.Add(node.Id);
            }
        }

        void SetupGround(Vector3[] taps)
        {
            float lowest = MathF.Min(Surfaces[0].Height, Surfaces[1].Height);
            if (GroundY > lowest - 0.1f)
                return;

            // Footprint under the span: between the two edges along U, across their extent along Side.
            float sMin = float.MaxValue, sMax = float.MinValue;
            foreach (var tap in taps)
            {
                float side = Frame.Local(tap).Y;
                sMin = Math.Min(sMin, side);
                sMax = Math.Max(sMax, side);
            }
            float uMin = Math.Max(Frame.Local(taps[0]).X, Frame.Local(taps[1]).X);
            float uMax = Math.Min(Frame.Local(taps[2]).X, Frame.Local(taps[3]).X);

            // An even grid: interior points along the gap, edge to edge across it.
            float g = Settings.GroundGridSpacing;
            int nu = Math.Max(2, (int)MathF.Round((uMax - uMin) / g));
            int ns = Math.Max(1, (int)MathF.Round((sMax - sMin) / g));
            for (int i = 1; i < nu; i++)
            {
                float u = uMin + (uMax - uMin) * i / nu;
                for (int j = 0; j <= ns; j++)
                {
                    float s = sMin + (sMax - sMin) * j / ns;
                    Vector3 p = Frame.World(u, s, 0f);
                    p.Y = GroundY;
                    var node = new Node { Id = NewId(), Position = p, Kind = NodeKind.GroundAnchor };
                    AddNode(node);
                    GroundAnchors.Add(node.Id);
                }
            }
        }

        // ---------- Lookup ----------

        public Node GetNode(int id) => _nodes[id];
        public bool TryGetNode(int id, out Node node) => _nodes.TryGetValue(id, out node);
        public Line GetLine(int id) => _lines[id];
        public bool TryGetLine(int id, out Line line) => _lines.TryGetValue(id, out line);
        public Fill GetFill(int id) => _fills[id];
        public bool TryGetFill(int id, out Fill fill) => _fills.TryGetValue(id, out fill);

        public int NewId() => _nextId++;

        /// <summary>Nearest node within radius, or -1.</summary>
        public int FindNode(Vector3 p, float radius, int exclude = -1)
        {
            int best = -1;
            float bestDist = radius;
            foreach (var n in Nodes)
            {
                if (n.Id == exclude)
                    continue;
                float d = Vector3.Distance(n.Position, p);
                if (d <= bestDist)
                {
                    bestDist = d;
                    best = n.Id;
                }
            }
            return best;
        }

        /// <summary>True if a and b are consecutive nodes of some line, i.e. joined by a frame element.</summary>
        public bool HasElement(int a, int b)
        {
            foreach (var line in Lines)
            {
                for (int i = 0; i + 1 < line.Nodes.Count; i++)
                {
                    int x = line.Nodes[i], y = line.Nodes[i + 1];
                    if ((x == a && y == b) || (x == b && y == a))
                        return true;
                }
            }
            return false;
        }

        public int FreeNodeCount
        {
            get
            {
                int count = 0;
                foreach (var n in Nodes)
                {
                    if (!n.IsFixed)
                        count++;
                }
                return count;
            }
        }

        public float LineLength(Line line)
        {
            float len = 0f;
            for (int i = 0; i + 1 < line.Nodes.Count; i++)
                len += Vector3.Distance(GetNode(line.Nodes[i]).Position, GetNode(line.Nodes[i + 1]).Position);
            return len;
        }

        /// <summary>Material used so far (spec 2.9): lines by length, fills by area.</summary>
        public float Cost
        {
            get
            {
                float cost = 0f;
                foreach (var line in Lines)
                    cost += LineLength(line) * Settings.CostPerMeter;
                foreach (var fill in Fills)
                    cost += fill.Area * Settings.CostPerSquareMeter;
                return cost;
            }
        }

        // ---------- Raw mutation (BridgeEditing wraps these in undoable edits) ----------

        internal void AddNode(Node n)
        {
            Nodes.Add(n);
            _nodes[n.Id] = n;
            Version++;
        }

        internal void RemoveNode(int id)
        {
            Nodes.Remove(_nodes[id]);
            _nodes.Remove(id);
            Version++;
        }

        internal void AddLine(Line l)
        {
            Lines.Add(l);
            _lines[l.Id] = l;
            Version++;
        }

        internal void RemoveLine(int id)
        {
            Lines.Remove(_lines[id]);
            _lines.Remove(id);
            Version++;
        }

        internal void AddFill(Fill f)
        {
            Fills.Add(f);
            _fills[f.Id] = f;
            Version++;
        }

        internal void RemoveFill(int id)
        {
            Fills.Remove(_fills[id]);
            _fills.Remove(id);
            Version++;
        }

        internal void Touch() => Version++;
    }
}
