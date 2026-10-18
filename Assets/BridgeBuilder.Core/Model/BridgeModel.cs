using System;
using System.Collections.Generic;
using System.Linq;

namespace BridgeBuilder.Core
{
    public enum EditReason
    {
        None,
        BudgetExceeded,
        MissingStartNode,
        UnknownNode,
        InvalidLine,
        Blocked,
        UnknownId,
    }

    public enum BudgetCause
    {
        None,
        MaterialBudget,
        NodeCap,
    }

    /// <summary>The three budget numbers and the hidden node cap (spec 2.9).</summary>
    public sealed class BudgetPolicy
    {
        public double CostPerMeter { get; set; }
        public double CostPerSquareMeter { get; set; }
        public double MaterialBudget { get; set; }
        public int NodeCap { get; set; }

        public static BudgetPolicy Default => new BudgetPolicy
        {
            CostPerMeter = Constants.Budget.CostPerMeter,
            CostPerSquareMeter = Constants.Budget.CostPerSquareMeter,
            MaterialBudget = Constants.Budget.MaterialBudget,
            NodeCap = Constants.Human.NodeCap,
        };
    }

    /// <summary>A line vertex: an existing node, a new point in room units, or a new point on an existing member.</summary>
    public readonly struct LineVertex
    {
        public readonly int ExistingId;      // -1 for a new point
        public readonly int MemberId;        // >= 0: the new point splits this member (spec 11.5)
        public readonly Vec3 Position;

        LineVertex(int id, int memberId, Vec3 p)
        {
            ExistingId = id;
            MemberId = memberId;
            Position = p;
        }

        public static LineVertex Existing(int id) => new LineVertex(id, -1, default);
        public static LineVertex New(Vec3 position) => new LineVertex(-1, -1, position);
        public static LineVertex OnMember(int memberId, Vec3 position) => new LineVertex(-1, memberId, position);
        public bool IsExisting => ExistingId >= 0;
        public bool IsOnMember => !IsExisting && MemberId >= 0;
    }

    public sealed class EditResult
    {
        public bool Ok { get; set; }
        public EditReason Reason { get; set; }
        public BudgetCause Cause { get; set; }
        public int Id { get; set; } = -1;                                 // the line, member or fill created
        public List<int> BlockingMemberIds { get; } = new List<int>();     // dependents that block a delete
        public List<int> InvalidatedFillIds { get; } = new List<int>();    // fills a delete invalidated

        public static EditResult Success(int id = -1) => new EditResult { Ok = true, Id = id };
        public static EditResult Fail(EditReason reason, BudgetCause cause = BudgetCause.None) => new EditResult { Reason = reason, Cause = cause };
    }

    /// <summary>
    /// The player's bridge (spec 2.3, 2.4, 2.9, 2.11): nodes, members, lines and fills with budget, hidden node
    /// cap, one undo stack and delete rules. Every successful mutation pushes one snapshot. A line that joins
    /// another between its nodes splits that member (spec 11.5); tests run on Copy().
    /// </summary>
    public sealed class BridgeModel
    {
        sealed class State
        {
            public List<Node> Nodes = new List<Node>();
            public List<Member> Members = new List<Member>();
            public List<Line> Lines = new List<Line>();
            public List<Fill> Fills = new List<Fill>();
            public Dictionary<int, int> Creator = new Dictionary<int, int>();   // node id -> line that created it
            public HashSet<int> SplitNodes = new HashSet<int>();                // nodes added by splitting a member
            public double Spent;
            public int NextId = 1;

            public State Clone() => new State
            {
                Nodes = Nodes.Select(n => new Node { Id = n.Id, Position = n.Position, Fixed = n.Fixed, SurfaceId = n.SurfaceId, GroundAnchor = n.GroundAnchor }).ToList(),
                Members = Members.Select(m => new Member { Id = m.Id, NodeA = m.NodeA, NodeB = m.NodeB, SectionId = m.SectionId, LineId = m.LineId, HiddenBrace = m.HiddenBrace }).ToList(),
                Lines = Lines.Select(l => new Line { Id = l.Id, Members = new List<int>(l.Members) }).ToList(),
                Fills = Fills.Select(f => new Fill { Id = f.Id, Loop = new List<int>(f.Loop), Road = f.Road, Valid = f.Valid }).ToList(),
                Creator = new Dictionary<int, int>(Creator),
                SplitNodes = new HashSet<int>(SplitNodes),
                Spent = Spent,
                NextId = NextId,
            };
        }

        State _s = new State();
        readonly Stack<State> _undo = new Stack<State>();

        public BudgetPolicy Policy { get; }
        public IReadOnlyList<Node> Nodes => _s.Nodes;
        public IReadOnlyList<Member> Members => _s.Members;
        public IReadOnlyList<Line> Lines => _s.Lines;
        public IReadOnlyList<Fill> Fills => _s.Fills;
        public double Spent => _s.Spent;
        public double Remaining => Policy.MaterialBudget - _s.Spent;
        public int UndoDepth => _undo.Count;

        /// <summary>Bumped on every change, so views and previews know when to refresh.</summary>
        public int Version { get; private set; }

        public BridgeModel(BudgetPolicy policy = null) => Policy = policy ?? BudgetPolicy.Default;

        // ---------- Queries ----------

        public Node GetNode(int id) => _s.Nodes.FirstOrDefault(n => n.Id == id);
        public Member GetMember(int id) => _s.Members.FirstOrDefault(m => m.Id == id);
        public Line GetLine(int id) => _s.Lines.FirstOrDefault(l => l.Id == id);
        public Fill GetFill(int id) => _s.Fills.FirstOrDefault(f => f.Id == id);
        public int CreatorLine(int nodeId) => _s.Creator.TryGetValue(nodeId, out int line) ? line : -1;

        /// <summary>The nodes of a line in order.</summary>
        public List<int> LineNodes(int lineId)
        {
            var line = GetLine(lineId);
            var nodes = new List<int>();
            if (line == null)
                return nodes;
            foreach (int mid in line.Members)
            {
                var m = GetMember(mid);
                if (nodes.Count == 0)
                    nodes.Add(m.NodeA);
                nodes.Add(m.NodeB);
            }
            return nodes;
        }

        public double LineLength(int lineId)
        {
            var nodes = LineNodes(lineId);
            double len = 0;
            for (int i = 0; i + 1 < nodes.Count; i++)
                len += Vec3.Distance(GetNode(nodes[i]).Position, GetNode(nodes[i + 1]).Position);
            return len;
        }

        public double FillArea(Fill fill) => NewellArea(fill.Loop.Select(id => GetNode(id).Position).ToList());

        /// <summary>The absolute area of a planar polygon by Newell's method.</summary>
        public static double NewellArea(IReadOnlyList<Vec3> loop)
        {
            double nx = 0, ny = 0, nz = 0;
            for (int i = 0; i < loop.Count; i++)
            {
                Vec3 a = loop[i], b = loop[(i + 1) % loop.Count];
                nx += (a.Y - b.Y) * (a.Z + b.Z);
                ny += (a.Z - b.Z) * (a.X + b.X);
                nz += (a.X - b.X) * (a.Y + b.Y);
            }
            return 0.5 * Math.Sqrt(nx * nx + ny * ny + nz * nz);
        }

        // ---------- Edits ----------

        /// <summary>Supports and other seed nodes. Not an undoable edit: seeds exist before the player builds.</summary>
        public int AddFixedNode(Vec3 position, int? surfaceId = null, bool groundAnchor = false)
        {
            int id = _s.NextId++;
            _s.Nodes.Add(new Node { Id = id, Position = position, Fixed = true, SurfaceId = surfaceId, GroundAnchor = groundAnchor });
            Version++;
            return id;
        }

        /// <summary>
        /// A line through the given vertices; the first must be an existing node or a point on an existing
        /// member (spec 2.3). Points on members split them, in the same edit (spec 11.5).
        /// </summary>
        public EditResult TryAddLine(IReadOnlyList<LineVertex> vertices, int sectionId = 0) => AddLine(vertices, sectionId, false);

        /// <summary>
        /// A free-drawn line (spec 11.1): like TryAddLine, but the first vertex may be a new point.
        /// </summary>
        public EditResult TryAddFreeLine(IReadOnlyList<LineVertex> vertices, int sectionId = 0) => AddLine(vertices, sectionId, true);

        sealed class Split
        {
            public Member Member;
            public double T;
            public Vec3 Position;
            public int NodeId;
        }

        const double OnMemberTolerance = 1e-6;     // room meters

        EditResult AddLine(IReadOnlyList<LineVertex> vertices, int sectionId, bool freeStart)
        {
            if (vertices == null || vertices.Count < 2)
                return EditResult.Fail(EditReason.InvalidLine);
            if (!vertices[0].IsExisting && !vertices[0].IsOnMember && !freeStart)
                return EditResult.Fail(EditReason.MissingStartNode);
            if (vertices[0].IsExisting && GetNode(vertices[0].ExistingId) == null)
                return EditResult.Fail(EditReason.MissingStartNode);

            var points = new List<Vec3>();
            var existing = new int[vertices.Count];      // node id, or -1 for a new node
            var onSplit = new int[vertices.Count];       // index into splits, or -1
            var splits = new List<Split>();
            int newNodes = 0;
            for (int i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i];
                existing[i] = -1;
                onSplit[i] = -1;
                if (v.IsExisting)
                {
                    var n = GetNode(v.ExistingId);
                    if (n == null)
                        return EditResult.Fail(EditReason.UnknownNode);
                    existing[i] = n.Id;
                    points.Add(n.Position);
                }
                else if (v.IsOnMember)
                {
                    if (!v.Position.IsFinite || !TryLocate(v.MemberId, v.Position, out var piece, out double t, out int nodeId))
                        return EditResult.Fail(EditReason.InvalidLine);
                    if (nodeId >= 0)
                    {
                        existing[i] = nodeId;
                        points.Add(GetNode(nodeId).Position);
                    }
                    else
                    {
                        Vec3 p = Vec3.Lerp(GetNode(piece.NodeA).Position, GetNode(piece.NodeB).Position, t);
                        int k = splits.FindIndex(s => s.Member == piece && Vec3.Distance(s.Position, p) <= OnMemberTolerance);
                        if (k < 0)
                        {
                            splits.Add(new Split { Member = piece, T = t, Position = p });
                            k = splits.Count - 1;
                            newNodes++;
                        }
                        onSplit[i] = k;
                        points.Add(p);
                    }
                }
                else
                {
                    if (!v.Position.IsFinite)
                        return EditResult.Fail(EditReason.InvalidLine);
                    points.Add(v.Position);
                    newNodes++;
                }
                if (i > 0 && Vec3.Distance(points[i], points[i - 1]) <= 1e-9)
                    return EditResult.Fail(EditReason.InvalidLine);
            }

            double length = 0;
            for (int i = 1; i < points.Count; i++)
                length += Vec3.Distance(points[i - 1], points[i]);
            double cost = length * Policy.CostPerMeter;
            if (_s.Nodes.Count + newNodes > Policy.NodeCap)
                return EditResult.Fail(EditReason.BudgetExceeded, BudgetCause.NodeCap);
            if (_s.Spent + cost > Policy.MaterialBudget + 1e-9)
                return EditResult.Fail(EditReason.BudgetExceeded, BudgetCause.MaterialBudget);

            Push();
            var line = new Line { Id = _s.NextId++ };
            foreach (var group in splits.GroupBy(s => s.Member))
                SplitMember(group.Key, group.OrderBy(s => s.T).ToList());
            var ids = new int[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
            {
                if (existing[i] >= 0 || onSplit[i] >= 0)
                {
                    ids[i] = existing[i] >= 0 ? existing[i] : splits[onSplit[i]].NodeId;
                    continue;
                }
                int id = _s.NextId++;
                _s.Nodes.Add(new Node { Id = id, Position = vertices[i].Position });
                _s.Creator[id] = line.Id;
                ids[i] = id;
            }
            for (int i = 0; i + 1 < ids.Length; i++)
            {
                var m = new Member { Id = _s.NextId++, NodeA = ids[i], NodeB = ids[i + 1], SectionId = sectionId, LineId = line.Id };
                _s.Members.Add(m);
                line.Members.Add(m.Id);
            }
            _s.Lines.Add(line);
            _s.Spent += cost;
            return EditResult.Success(line.Id);
        }

        /// <summary>
        /// Where p lies on the line that memberId belongs to (an earlier split may have moved it onto another
        /// piece of that line): the piece and the fraction t along it, or the node p sits on.
        /// </summary>
        bool TryLocate(int memberId, Vec3 p, out Member piece, out double t, out int nodeId)
        {
            piece = null;
            t = 0;
            nodeId = -1;
            var member = GetMember(memberId);
            if (member == null || member.LineId < 0)
                return false;
            double best = OnMemberTolerance;
            foreach (var m in _s.Members)
            {
                if (m.LineId != member.LineId)
                    continue;
                Vec3 q = Geometry.ClosestOnSegment(p, GetNode(m.NodeA).Position, GetNode(m.NodeB).Position, out double u);
                double d = Vec3.Distance(p, q);
                if (d <= best)
                {
                    best = d;
                    piece = m;
                    t = u;
                }
            }
            if (piece == null)
                return false;
            double length = Vec3.Distance(GetNode(piece.NodeA).Position, GetNode(piece.NodeB).Position);
            if (t * length <= OnMemberTolerance)
                nodeId = piece.NodeA;
            else if ((1 - t) * length <= OnMemberTolerance)
                nodeId = piece.NodeB;
            return true;
        }

        /// <summary>
        /// Splits a member at points sorted along it (spec 11.5). The new nodes belong to the member's line, and
        /// fills along the member get them in their loops.
        /// </summary>
        void SplitMember(Member member, List<Split> points)
        {
            var line = GetLine(member.LineId);
            int start = member.NodeA, end = member.NodeB;
            var ids = new List<int>();
            foreach (var s in points)
            {
                s.NodeId = _s.NextId++;
                _s.Nodes.Add(new Node { Id = s.NodeId, Position = s.Position });
                _s.Creator[s.NodeId] = member.LineId;
                _s.SplitNodes.Add(s.NodeId);
                ids.Add(s.NodeId);
            }

            member.NodeB = ids[0];
            int at = _s.Members.IndexOf(member), inLine = line.Members.IndexOf(member.Id);
            for (int k = 0; k < ids.Count; k++)
            {
                var piece = new Member
                {
                    Id = _s.NextId++, NodeA = ids[k], NodeB = k + 1 < ids.Count ? ids[k + 1] : end,
                    SectionId = member.SectionId, LineId = member.LineId,
                };
                _s.Members.Insert(at + 1 + k, piece);
                line.Members.Insert(inLine + 1 + k, piece.Id);
            }

            foreach (var fill in _s.Fills)
            {
                for (int i = 0; i < fill.Loop.Count; i++)
                {
                    int a = fill.Loop[i], b = fill.Loop[(i + 1) % fill.Loop.Count];
                    if ((a == start && b == end) || (a == end && b == start))
                    {
                        fill.Loop.InsertRange(i + 1, a == start ? ids : Enumerable.Reverse(ids));
                        break;
                    }
                }
            }
        }

        /// <summary>Rejoins the two pieces of a split member once nothing else uses the split node (spec 11.5).</summary>
        void MergeSplits()
        {
            foreach (int id in _s.SplitNodes.ToList())
            {
                var at = _s.Members.Where(m => m.NodeA == id || m.NodeB == id).ToList();
                if (at.Count != 2 || at[0].LineId != at[1].LineId)
                    continue;
                var line = GetLine(at[0].LineId);
                if (line == null)
                    continue;
                bool inOrder = line.Members.IndexOf(at[0].Id) < line.Members.IndexOf(at[1].Id);
                Member first = inOrder ? at[0] : at[1], second = inOrder ? at[1] : at[0];
                if (first.NodeB != id || second.NodeA != id || FillCorner(id, first.NodeA, second.NodeB))
                    continue;

                first.NodeB = second.NodeB;
                _s.Members.Remove(second);
                line.Members.Remove(second.Id);
                _s.Nodes.RemoveAll(n => n.Id == id);
                _s.Creator.Remove(id);
                _s.SplitNodes.Remove(id);
                foreach (var fill in _s.Fills)
                    fill.Loop.Remove(id);
            }
        }

        /// <summary>True if a fill turns a corner at the node, rather than passing straight through it from a to b.</summary>
        bool FillCorner(int id, int a, int b) => _s.Fills.Any(f =>
        {
            int i = f.Loop.IndexOf(id);
            if (i < 0)
                return false;
            int p = f.Loop[(i + f.Loop.Count - 1) % f.Loop.Count], q = f.Loop[(i + 1) % f.Loop.Count];
            return !((p == a && q == b) || (p == b && q == a));
        });

        /// <summary>Fill bracing (spec 2.4): a hidden member between existing nodes. Free; never creates nodes.</summary>
        public EditResult TryAddHiddenBrace(int nodeA, int nodeB, int sectionId)
        {
            if (GetNode(nodeA) == null || GetNode(nodeB) == null || nodeA == nodeB)
                return EditResult.Fail(EditReason.UnknownNode);
            Push();
            var m = new Member { Id = _s.NextId++, NodeA = nodeA, NodeB = nodeB, SectionId = sectionId, HiddenBrace = true };
            _s.Members.Add(m);
            return EditResult.Success(m.Id);
        }

        /// <summary>A fill over a loop of existing nodes, charged by area (spec 2.9). Planarity is checked elsewhere.</summary>
        public EditResult TryAddFill(IReadOnlyList<int> loop, bool road)
        {
            if (loop == null || loop.Count < 3 || loop.Any(id => GetNode(id) == null))
                return EditResult.Fail(EditReason.UnknownNode);
            double cost = NewellArea(loop.Select(id => GetNode(id).Position).ToList()) * Policy.CostPerSquareMeter;
            if (_s.Spent + cost > Policy.MaterialBudget + 1e-9)
                return EditResult.Fail(EditReason.BudgetExceeded, BudgetCause.MaterialBudget);
            Push();
            var fill = new Fill { Id = _s.NextId++, Loop = new List<int>(loop), Road = road, Valid = true };
            _s.Fills.Add(fill);
            _s.Spent += cost;
            return EditResult.Success(fill.Id);
        }

        /// <summary>
        /// Deletes a line with a refund (spec 2.11). Blocked while another line uses a node this one created;
        /// the result lists those members. Fills bordering the line become invalid (road cleared, area refunded).
        /// </summary>
        public EditResult TryDeleteLine(int lineId)
        {
            var line = GetLine(lineId);
            if (line == null)
                return EditResult.Fail(EditReason.UnknownId);

            var created = new HashSet<int>(_s.Creator.Where(kv => kv.Value == lineId).Select(kv => kv.Key));
            var result = new EditResult();
            foreach (var m in _s.Members)
            {
                if (m.LineId != lineId && (created.Contains(m.NodeA) || created.Contains(m.NodeB)))
                    result.BlockingMemberIds.Add(m.Id);
            }
            if (result.BlockingMemberIds.Count > 0)
            {
                result.Reason = EditReason.Blocked;
                return result;
            }

            Push();
            var lineMembers = _s.Members.Where(m => m.LineId == lineId).ToList();
            foreach (var fill in _s.Fills)
            {
                if (!fill.Valid || !Borders(fill, lineMembers))
                    continue;
                fill.Valid = false;
                fill.Road = false;
                _s.Spent -= FillArea(fill) * Policy.CostPerSquareMeter;
                result.InvalidatedFillIds.Add(fill.Id);
            }
            _s.Spent -= LineLength(lineId) * Policy.CostPerMeter;
            _s.Members.RemoveAll(m => m.LineId == lineId);
            _s.Members.RemoveAll(m => m.HiddenBrace && (created.Contains(m.NodeA) || created.Contains(m.NodeB)));
            _s.Nodes.RemoveAll(n => created.Contains(n.Id));
            foreach (int id in created)
            {
                _s.Creator.Remove(id);
                _s.SplitNodes.Remove(id);
            }
            _s.Lines.Remove(line);
            MergeSplits();
            _s.Spent = Math.Max(0, _s.Spent);
            result.Ok = true;
            return result;
        }

        static bool Borders(Fill fill, List<Member> members)
        {
            for (int i = 0; i < fill.Loop.Count; i++)
            {
                int a = fill.Loop[i], b = fill.Loop[(i + 1) % fill.Loop.Count];
                foreach (var m in members)
                {
                    if ((m.NodeA == a && m.NodeB == b) || (m.NodeA == b && m.NodeB == a))
                        return true;
                }
            }
            return false;
        }

        public EditResult TryDeleteFill(int fillId)
        {
            var fill = GetFill(fillId);
            if (fill == null)
                return EditResult.Fail(EditReason.UnknownId);
            Push();
            if (fill.Valid)
                _s.Spent = Math.Max(0, _s.Spent - FillArea(fill) * Policy.CostPerSquareMeter);
            _s.Fills.Remove(fill);
            return EditResult.Success(fillId);
        }

        /// <summary>Clears the bridge, keeping fixed nodes. Undoable.</summary>
        public void Reset()
        {
            Push();
            _s.Lines.Clear();
            _s.Members.Clear();
            _s.Fills.Clear();
            _s.Nodes.RemoveAll(n => !n.Fixed);
            _s.Creator.Clear();
            _s.SplitNodes.Clear();
            _s.Spent = 0;
        }

        public bool Undo()
        {
            if (_undo.Count == 0)
                return false;
            _s = _undo.Pop();
            Version++;
            return true;
        }

        /// <summary>An independent copy with its own empty undo stack (tests run on a copy, spec 2.11).</summary>
        public BridgeModel Copy()
        {
            var copy = new BridgeModel(Policy) { _s = _s.Clone() };
            return copy;
        }

        void Push()
        {
            _undo.Push(_s.Clone());
            Version++;
        }
    }
}
