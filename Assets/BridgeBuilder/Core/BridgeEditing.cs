using System;
using System.Collections.Generic;
using System.Numerics;

namespace BridgeBuilder.Core
{
    /// <summary>One undoable change to a design (spec 2.11).</summary>
    public interface IEdit
    {
        void Apply(BridgeDesign d);
        void Revert(BridgeDesign d);
    }

    /// <summary>One undo stack for every action.</summary>
    public sealed class UndoStack
    {
        readonly List<IEdit> _edits = new List<IEdit>();

        public int Count => _edits.Count;

        public void Do(BridgeDesign d, IEdit edit)
        {
            edit.Apply(d);
            _edits.Add(edit);
        }

        /// <summary>Records an edit that was already applied (e.g. a line plus its mirror).</summary>
        public void Push(IEdit edit) => _edits.Add(edit);

        public bool Undo(BridgeDesign d)
        {
            if (_edits.Count == 0)
                return false;
            _edits[_edits.Count - 1].Revert(d);
            _edits.RemoveAt(_edits.Count - 1);
            return true;
        }

        public void Clear() => _edits.Clear();
    }

    public sealed class CompositeEdit : IEdit
    {
        readonly List<IEdit> _edits;
        public CompositeEdit(List<IEdit> edits) => _edits = edits;

        public void Apply(BridgeDesign d)
        {
            foreach (var e in _edits)
                e.Apply(d);
        }

        public void Revert(BridgeDesign d)
        {
            for (int i = _edits.Count - 1; i >= 0; i--)
                _edits[i].Revert(d);
        }
    }

    public sealed class AddLineEdit : IEdit
    {
        public readonly Line Line;
        readonly List<Node> _newNodes;

        public AddLineEdit(Line line, List<Node> newNodes)
        {
            Line = line;
            _newNodes = newNodes;
        }

        public void Apply(BridgeDesign d)
        {
            foreach (var n in _newNodes)
                d.AddNode(n);
            d.AddLine(Line);
        }

        public void Revert(BridgeDesign d)
        {
            d.RemoveLine(Line.Id);
            foreach (var n in _newNodes)
                d.RemoveNode(n.Id);
        }
    }

    public sealed class RemoveLineEdit : IEdit
    {
        readonly Line _line;
        readonly List<Node> _ownedNodes;
        readonly List<Fill> _fills;

        public RemoveLineEdit(Line line, List<Node> ownedNodes, List<Fill> fills)
        {
            _line = line;
            _ownedNodes = ownedNodes;
            _fills = fills;
        }

        public int RemovedFills => _fills.Count;

        public void Apply(BridgeDesign d)
        {
            foreach (var f in _fills)
                d.RemoveFill(f.Id);
            d.RemoveLine(_line.Id);
            foreach (var n in _ownedNodes)
                d.RemoveNode(n.Id);
        }

        public void Revert(BridgeDesign d)
        {
            foreach (var n in _ownedNodes)
                d.AddNode(n);
            d.AddLine(_line);
            foreach (var f in _fills)
                d.AddFill(f);
        }
    }

    public sealed class AddFillEdit : IEdit
    {
        readonly Fill _fill;
        public AddFillEdit(Fill fill) => _fill = fill;
        public void Apply(BridgeDesign d) => d.AddFill(_fill);
        public void Revert(BridgeDesign d) => d.RemoveFill(_fill.Id);
    }

    public sealed class RemoveFillEdit : IEdit
    {
        readonly Fill _fill;
        public RemoveFillEdit(Fill fill) => _fill = fill;
        public void Apply(BridgeDesign d) => d.RemoveFill(_fill.Id);
        public void Revert(BridgeDesign d) => d.AddFill(_fill);
    }

    public sealed class SetRoadEdit : IEdit
    {
        readonly Fill _fill;
        readonly bool _value;

        public SetRoadEdit(Fill fill, bool value)
        {
            _fill = fill;
            _value = value;
        }

        public void Apply(BridgeDesign d)
        {
            _fill.IsRoad = _value;
            d.Touch();
        }

        public void Revert(BridgeDesign d)
        {
            _fill.IsRoad = !_value;
            d.Touch();
        }
    }

    public sealed class SetStartSurfaceEdit : IEdit
    {
        readonly int _value;
        int _previous;

        public SetStartSurfaceEdit(int value) => _value = value;

        public void Apply(BridgeDesign d)
        {
            _previous = d.StartSurface;
            d.StartSurface = _value;
            d.Touch();
        }

        public void Revert(BridgeDesign d)
        {
            d.StartSurface = _previous;
            d.Touch();
        }
    }

    public sealed class ClearEdit : IEdit
    {
        List<Fill> _fills;
        List<Line> _lines;
        List<Node> _nodes;

        public void Apply(BridgeDesign d)
        {
            _fills = new List<Fill>(d.Fills);
            _lines = new List<Line>(d.Lines);
            _nodes = d.Nodes.FindAll(n => !n.IsFixed);
            foreach (var f in _fills)
                d.RemoveFill(f.Id);
            foreach (var l in _lines)
                d.RemoveLine(l.Id);
            foreach (var n in _nodes)
                d.RemoveNode(n.Id);
        }

        public void Revert(BridgeDesign d)
        {
            foreach (var n in _nodes)
                d.AddNode(n);
            foreach (var l in _lines)
                d.AddLine(l);
            foreach (var f in _fills)
                d.AddFill(f);
        }
    }

    public sealed class RoadReport
    {
        public bool Ok;
        public string Message = "";
        public readonly List<int> SteepFills = new List<int>();
        public bool HasRoad;
        public bool ReachesStart;
        public bool ReachesFar;
    }

    /// <summary>Builds edits from player actions, and the checks those actions need.</summary>
    public static class BridgeEditing
    {
        // ---------- Lines ----------

        public static AddLineEdit CreateLine(BridgeDesign d, RecognizedStroke stroke)
        {
            var line = new Line { Id = d.NewId(), Shape = stroke.Shape };
            var created = new List<Node>();
            for (int i = 0; i < stroke.Points.Count; i++)
            {
                int existing = stroke.ExistingNodes[i];
                if (existing >= 0)
                {
                    line.Nodes.Add(existing);
                    continue;
                }
                var node = new Node { Id = d.NewId(), Position = stroke.Points[i], Kind = NodeKind.Free, OwnerLine = line.Id };
                created.Add(node);
                line.Nodes.Add(node.Id);
            }
            return new AddLineEdit(line, created);
        }

        /// <summary>Cost and node count a stroke would add, for the budget check.</summary>
        public static void StrokeCost(BridgeDesign d, RecognizedStroke stroke, out float cost, out int newNodes)
        {
            float len = 0f;
            newNodes = 0;
            for (int i = 0; i < stroke.Points.Count; i++)
            {
                if (i > 0)
                    len += Vector3.Distance(stroke.Points[i - 1], stroke.Points[i]);
                if (stroke.ExistingNodes[i] < 0)
                    newNodes++;
            }
            cost = len * d.Settings.CostPerMeter;
        }

        /// <summary>
        /// The Symmetry tool: the same stroke reflected across the gap's centerline. Must be applied after
        /// the original so it can snap to the original's new nodes. False if it would land on itself or
        /// its start has no node to begin from.
        /// </summary>
        public static bool TryMirror(BridgeDesign d, RecognizedStroke stroke, out RecognizedStroke mirrored)
        {
            mirrored = null;
            var frame = d.Frame;
            bool selfMirror = true;
            var result = new RecognizedStroke { Shape = stroke.Shape };
            float radius = d.Settings.NodeSnapRadius * 0.5f;

            for (int i = 0; i < stroke.Points.Count; i++)
            {
                Vector3 p = frame.Mirror(stroke.Points[i]);
                if (Vector3.Distance(p, stroke.Points[i]) > 0.01f)
                    selfMirror = false;
                int node = d.FindNode(p, radius);
                if (i == 0 && node < 0)
                    return false;
                result.Points.Add(node >= 0 ? d.GetNode(node).Position : p);
                result.ExistingNodes.Add(node);
            }
            if (selfMirror)
                return false;
            mirrored = result;
            return true;
        }

        /// <summary>Other lines that start or end on nodes this line created. Deleting is blocked while any exist.</summary>
        public static List<int> LineDependents(BridgeDesign d, int lineId)
        {
            var owned = new HashSet<int>();
            foreach (var n in d.Nodes)
            {
                if (n.OwnerLine == lineId)
                    owned.Add(n.Id);
            }
            var dependents = new List<int>();
            foreach (var other in d.Lines)
            {
                if (other.Id == lineId)
                    continue;
                foreach (int id in other.Nodes)
                {
                    if (owned.Contains(id))
                    {
                        dependents.Add(other.Id);
                        break;
                    }
                }
            }
            return dependents;
        }

        /// <summary>Removes a line, its own nodes, and any fill bordering it. Null if blocked by dependents.</summary>
        public static RemoveLineEdit RemoveLine(BridgeDesign d, int lineId)
        {
            if (LineDependents(d, lineId).Count > 0)
                return null;
            var line = d.GetLine(lineId);
            var owned = d.Nodes.FindAll(n => n.OwnerLine == lineId);
            var fills = d.Fills.FindAll(f => FillUsesLine(f, line));
            return new RemoveLineEdit(line, owned, fills);
        }

        static bool FillUsesLine(Fill f, Line line)
        {
            for (int i = 0; i < f.Boundary.Count; i++)
            {
                int a = f.Boundary[i], b = f.Boundary[(i + 1) % f.Boundary.Count];
                for (int k = 0; k + 1 < line.Nodes.Count; k++)
                {
                    if ((line.Nodes[k] == a && line.Nodes[k + 1] == b) || (line.Nodes[k] == b && line.Nodes[k + 1] == a))
                        return true;
                }
            }
            return false;
        }

        // ---------- Fills ----------

        /// <summary>
        /// Builds a fill from tapped corners. Each consecutive pair must be joined by one line (or run along
        /// the same surface edge); the loop must be flat (spec 2.4).
        /// </summary>
        public static bool TryBuildFill(BridgeDesign d, IReadOnlyList<int> corners, out Fill fill, out string error)
        {
            fill = null;
            error = null;
            if (corners.Count < 3)
            {
                error = "Tap at least three corners";
                return false;
            }

            var boundary = new List<int>();
            for (int i = 0; i < corners.Count; i++)
            {
                int a = corners[i], b = corners[(i + 1) % corners.Count];
                if (!TryPath(d, a, b, out var path))
                {
                    error = $"Corners {i + 1} and {(i + 1) % corners.Count + 1} aren't joined by one line";
                    return false;
                }
                for (int k = 0; k < path.Count - 1; k++)
                    boundary.Add(path[k]);
            }

            if (new HashSet<int>(boundary).Count != boundary.Count)
            {
                error = "The outline crosses itself";
                return false;
            }

            var pts = boundary.ConvertAll(id => d.GetNode(id).Position);
            Geometry.FitPlane(pts, out Vector3 center, out Vector3 normal, out float flatness);
            if (flatness > d.Settings.PlaneTolerance)
            {
                error = "That outline isn't flat";
                return false;
            }

            Geometry.PlaneBasis(normal, out Vector3 u, out Vector3 v);
            var flat = pts.ConvertAll(p => new Vector2(Vector3.Dot(p - center, u), Vector3.Dot(p - center, v)));
            float area = Math.Abs(Geometry.SignedArea(flat));
            if (area < 1e-4f)
            {
                error = "That outline has no area";
                return false;
            }

            var tris = Geometry.Triangulate(flat);
            fill = new Fill { Id = d.NewId(), Normal = normal.Y < 0f ? -normal : normal, Area = area };
            fill.Corners.AddRange(corners);
            fill.Boundary.AddRange(boundary);
            foreach (int t in tris)
                fill.Triangles.Add(boundary[t]);

            // Hidden bracing: every triangulation edge that isn't already a boundary edge or a drawn element.
            var seen = new HashSet<(int, int)>();
            for (int i = 0; i < boundary.Count; i++)
                seen.Add(Key(boundary[i], boundary[(i + 1) % boundary.Count]));
            for (int t = 0; t < fill.Triangles.Count; t += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    int a = fill.Triangles[t + e], b = fill.Triangles[t + (e + 1) % 3];
                    var key = Key(a, b);
                    if (!seen.Add(key) || d.HasElement(a, b))
                        continue;
                    if (d.GetNode(a).IsFixed && d.GetNode(b).IsFixed)
                        continue;
                    fill.Bracing.Add(key);
                }
            }
            return true;
        }

        static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

        /// <summary>The run of nodes from a to b along one line, or along one surface edge.</summary>
        static bool TryPath(BridgeDesign d, int a, int b, out List<int> path)
        {
            path = null;
            foreach (var line in d.Lines)
            {
                int ia = line.Nodes.IndexOf(a), ib = line.Nodes.IndexOf(b);
                if (ia < 0 || ib < 0)
                    continue;
                path = Run(line.Nodes, ia, ib);
                return true;
            }

            var na = d.GetNode(a);
            var nb = d.GetNode(b);
            if (na.Kind == NodeKind.SurfaceAnchor && nb.Kind == NodeKind.SurfaceAnchor && na.Surface == nb.Surface)
            {
                var anchors = d.Surfaces[na.Surface].Anchors;
                path = Run(anchors, anchors.IndexOf(a), anchors.IndexOf(b));
                return true;
            }
            return false;
        }

        static List<int> Run(List<int> chain, int from, int to)
        {
            var run = new List<int>();
            int step = to >= from ? 1 : -1;
            for (int i = from; i != to + step; i += step)
                run.Add(chain[i]);
            return run;
        }

        // ---------- Roads ----------

        /// <summary>
        /// The flagged road fills must form a chain from the start surface to the far surface, each gentle
        /// enough to drive (spec 2.4).
        /// </summary>
        public static RoadReport ValidateRoads(BridgeDesign d)
        {
            var report = new RoadReport();
            var roads = d.Fills.FindAll(f => f.IsRoad);
            report.HasRoad = roads.Count > 0;
            if (!report.HasRoad)
            {
                report.Message = "No road yet. Fill a deck, then flag it as road.";
                return report;
            }

            foreach (var f in roads)
            {
                if (f.SlopeDeg > d.Settings.MaxRoadSlopeDeg)
                    report.SteepFills.Add(f.Id);
            }

            int start = d.StartSurface, far = 1 - d.StartSurface;
            var reached = new HashSet<int>();
            var queue = new Queue<Fill>();
            foreach (var f in roads)
            {
                if (TouchesSurface(d, f, start))
                {
                    reached.Add(f.Id);
                    queue.Enqueue(f);
                }
            }
            report.ReachesStart = reached.Count > 0;

            while (queue.Count > 0)
            {
                var f = queue.Dequeue();
                if (TouchesSurface(d, f, far))
                    report.ReachesFar = true;
                foreach (var g in roads)
                {
                    if (!reached.Contains(g.Id) && SharesEdge(f, g))
                    {
                        reached.Add(g.Id);
                        queue.Enqueue(g);
                    }
                }
            }

            if (!report.ReachesStart)
                report.Message = "The road doesn't start at the car's surface";
            else if (!report.ReachesFar)
                report.Message = "The road doesn't reach the far surface";
            else if (report.SteepFills.Count > 0)
                report.Message = "A road section is too steep to drive";
            else
                report.Ok = true;
            return report;
        }

        static bool TouchesSurface(BridgeDesign d, Fill f, int surface)
        {
            int count = 0;
            foreach (int id in f.Boundary)
            {
                var n = d.GetNode(id);
                if (n.Kind == NodeKind.SurfaceAnchor && n.Surface == surface)
                    count++;
            }
            return count >= 2;
        }

        static bool SharesEdge(Fill f, Fill g)
        {
            var edges = new HashSet<(int, int)>();
            for (int i = 0; i < f.Boundary.Count; i++)
                edges.Add(Key(f.Boundary[i], f.Boundary[(i + 1) % f.Boundary.Count]));
            for (int i = 0; i < g.Boundary.Count; i++)
            {
                if (edges.Contains(Key(g.Boundary[i], g.Boundary[(i + 1) % g.Boundary.Count])))
                    return true;
            }
            return false;
        }

        // ---------- Picking ----------

        /// <summary>The line closest to p within radius, or -1.</summary>
        public static int PickLine(BridgeDesign d, Vector3 p, float radius)
        {
            int best = -1;
            float bestDist = radius;
            foreach (var line in d.Lines)
            {
                for (int i = 0; i + 1 < line.Nodes.Count; i++)
                {
                    float dist = Geometry.DistanceToSegment(p, d.GetNode(line.Nodes[i]).Position, d.GetNode(line.Nodes[i + 1]).Position);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = line.Id;
                    }
                }
            }
            return best;
        }

        /// <summary>The fill p is touching (within radius of its surface and inside its outline), or -1.</summary>
        public static int PickFill(BridgeDesign d, Vector3 p, float radius)
        {
            int best = -1;
            float bestDist = radius;
            foreach (var f in d.Fills)
            {
                for (int t = 0; t < f.Triangles.Count; t += 3)
                {
                    Vector3 a = d.GetNode(f.Triangles[t]).Position;
                    Vector3 b = d.GetNode(f.Triangles[t + 1]).Position;
                    Vector3 c = d.GetNode(f.Triangles[t + 2]).Position;
                    float dist = Math.Abs(Vector3.Dot(p - a, f.Normal));
                    if (dist < bestDist && InTriangle(p, a, b, c, f.Normal))
                    {
                        bestDist = dist;
                        best = f.Id;
                    }
                }
            }
            return best;
        }

        static bool InTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c, Vector3 n)
        {
            float d1 = Vector3.Dot(Vector3.Cross(b - a, p - a), n);
            float d2 = Vector3.Dot(Vector3.Cross(c - b, p - b), n);
            float d3 = Vector3.Dot(Vector3.Cross(a - c, p - c), n);
            return (d1 >= 0 && d2 >= 0 && d3 >= 0) || (d1 <= 0 && d2 <= 0 && d3 <= 0);
        }
    }
}
