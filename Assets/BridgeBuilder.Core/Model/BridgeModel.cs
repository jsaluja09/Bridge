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

    /// <summary>Prices per material, the material budget and the hidden node cap (spec 2.9).</summary>
    public sealed class BudgetPolicy
    {
        public double[] CostPerMeter { get; set; }          // indexed by section id
        public double[] CostPerSquareMeter { get; set; }    // indexed by section id (fills and decks)
        public double MaterialBudget { get; set; }
        public int NodeCap { get; set; }

        /// <summary>Price of one room meter of line in this section.</summary>
        public double LineCost(int sectionId) => Price(CostPerMeter, sectionId);

        /// <summary>Price of one room square meter of fill or deck in this section.</summary>
        public double AreaCost(int sectionId) => Price(CostPerSquareMeter, sectionId);

        /// <summary>True when both a line and a fill in this section have a price.</summary>
        internal bool Prices(int sectionId) => Has(CostPerMeter, sectionId) && Has(CostPerSquareMeter, sectionId);

        public static BudgetPolicy Default => ForMaterials(Constants.Budget.MaterialBudget, Constants.Human.NodeCap);

        /// <summary>The game's prices: each material's line and deck price (Materials).</summary>
        public static BudgetPolicy ForMaterials(double budget, int nodeCap) => new BudgetPolicy
        {
            CostPerMeter = Array.ConvertAll(Materials.All, Materials.CostPerMeter),
            CostPerSquareMeter = Array.ConvertAll(Materials.All, Materials.DeckCostPerSquareMeter),
            MaterialBudget = budget,
            NodeCap = nodeCap,
        };

        /// <summary>The same price for every material, for tests that are not about materials.</summary>
        public static BudgetPolicy Uniform(double perMeter, double perSquareMeter, double budget, int nodeCap) => new BudgetPolicy
        {
            CostPerMeter = Array.ConvertAll(Materials.All, _ => perMeter),
            CostPerSquareMeter = Array.ConvertAll(Materials.All, _ => perSquareMeter),
            MaterialBudget = budget,
            NodeCap = nodeCap,
        };

        static bool Has(double[] prices, int sectionId) => prices != null && sectionId >= 0 && sectionId < prices.Length;

        static double Price(double[] prices, int sectionId) =>
            Has(prices, sectionId) ? prices[sectionId] : throw new ArgumentOutOfRangeException(nameof(sectionId), sectionId, "No price for this section");
    }

    /// <summary>A line vertex: an existing node, or a new point in room units.</summary>
    public readonly struct LineVertex
    {
        public readonly int ExistingId;      // -1 for a new point
        public readonly Vec3 Position;

        LineVertex(int id, Vec3 p)
        {
            ExistingId = id;
            Position = p;
        }

        public static LineVertex Existing(int id) => new LineVertex(id, default);
        public static LineVertex New(Vec3 position) => new LineVertex(-1, position);
        public bool IsExisting => ExistingId >= 0;
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
    /// cap, one undo stack and delete rules. A line or fill is priced by its section (material); deleting it
    /// refunds that same price. Every successful mutation pushes one snapshot. No member is ever split; tests
    /// run on Copy().
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
            public double Spent;
            public int NextId = 1;

            public State Clone() => new State
            {
                Nodes = Nodes.Select(n => new Node { Id = n.Id, Position = n.Position, Fixed = n.Fixed, SurfaceId = n.SurfaceId, GroundAnchor = n.GroundAnchor }).ToList(),
                Members = Members.Select(m => new Member { Id = m.Id, NodeA = m.NodeA, NodeB = m.NodeB, SectionId = m.SectionId, LineId = m.LineId, HiddenBrace = m.HiddenBrace }).ToList(),
                Lines = Lines.Select(l => new Line { Id = l.Id, Members = new List<int>(l.Members) }).ToList(),
                Fills = Fills.Select(f => new Fill { Id = f.Id, Loop = new List<int>(f.Loop), Road = f.Road, Valid = f.Valid, SectionId = f.SectionId }).ToList(),
                Creator = new Dictionary<int, int>(Creator),
                Spent = Spent,
                NextId = NextId,
            };
        }

        State _s = new State();
        readonly Stack<State> _undo = new Stack<State>();
        int _groupDepth;
        bool _groupSaved;          // the open group has already saved its undo snapshot

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

        /// <summary>The section id (material) of a line's members, or -1 if there is no such line.</summary>
        public int LineSection(int lineId)
        {
            var line = GetLine(lineId);
            return line == null || line.Members.Count == 0 ? -1 : GetMember(line.Members[0]).SectionId;
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
        /// A line through the given vertices in one section (material), charged by length at that section's
        /// price; the first vertex must be an existing node (spec 2.3). A section with no price is an invalid line.
        /// </summary>
        public EditResult TryAddLine(IReadOnlyList<LineVertex> vertices, int sectionId = 0) => AddLine(vertices, sectionId, false);

        /// <summary>
        /// A free-drawn line (spec 11.1): like TryAddLine, but the first vertex may be a new point.
        /// </summary>
        public EditResult TryAddFreeLine(IReadOnlyList<LineVertex> vertices, int sectionId = 0) => AddLine(vertices, sectionId, true);

        EditResult AddLine(IReadOnlyList<LineVertex> vertices, int sectionId, bool freeStart)
        {
            if (vertices == null || vertices.Count < 2 || !Policy.Prices(sectionId))
                return EditResult.Fail(EditReason.InvalidLine);
            if (!vertices[0].IsExisting && !freeStart)
                return EditResult.Fail(EditReason.MissingStartNode);
            if (vertices[0].IsExisting && GetNode(vertices[0].ExistingId) == null)
                return EditResult.Fail(EditReason.MissingStartNode);

            var points = new List<Vec3>();
            int newNodes = 0;
            for (int i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i];
                if (v.IsExisting)
                {
                    var n = GetNode(v.ExistingId);
                    if (n == null)
                        return EditResult.Fail(EditReason.UnknownNode);
                    points.Add(n.Position);
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
            double cost = length * Policy.LineCost(sectionId);
            if (_s.Nodes.Count + newNodes > Policy.NodeCap)
                return EditResult.Fail(EditReason.BudgetExceeded, BudgetCause.NodeCap);
            if (_s.Spent + cost > Policy.MaterialBudget + 1e-9)
                return EditResult.Fail(EditReason.BudgetExceeded, BudgetCause.MaterialBudget);

            Push();
            var line = new Line { Id = _s.NextId++ };
            var ids = new int[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
            {
                if (vertices[i].IsExisting)
                {
                    ids[i] = vertices[i].ExistingId;
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

        /// <summary>
        /// A fill over a loop of existing nodes in one section (material), charged by area at that section's price
        /// (spec 2.9). A section with no price is invalid. Planarity is checked elsewhere.
        /// </summary>
        public EditResult TryAddFill(IReadOnlyList<int> loop, bool road, int sectionId = 0)
        {
            if (loop == null || loop.Count < 3 || loop.Any(id => GetNode(id) == null))
                return EditResult.Fail(EditReason.UnknownNode);
            if (!Policy.Prices(sectionId))
                return EditResult.Fail(EditReason.InvalidLine);
            double cost = NewellArea(loop.Select(id => GetNode(id).Position).ToList()) * Policy.AreaCost(sectionId);
            if (_s.Spent + cost > Policy.MaterialBudget + 1e-9)
                return EditResult.Fail(EditReason.BudgetExceeded, BudgetCause.MaterialBudget);
            Push();
            var fill = new Fill { Id = _s.NextId++, Loop = new List<int>(loop), Road = road, Valid = true, SectionId = sectionId };
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
                _s.Spent -= FillArea(fill) * Policy.AreaCost(fill.SectionId);
                result.InvalidatedFillIds.Add(fill.Id);
            }
            _s.Spent -= LineLength(lineId) * Policy.LineCost(LineSection(lineId));
            _s.Members.RemoveAll(m => m.LineId == lineId);
            _s.Members.RemoveAll(m => m.HiddenBrace && (created.Contains(m.NodeA) || created.Contains(m.NodeB)));
            _s.Nodes.RemoveAll(n => created.Contains(n.Id));
            foreach (int id in created)
                _s.Creator.Remove(id);
            _s.Lines.Remove(line);
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
                _s.Spent = Math.Max(0, _s.Spent - FillArea(fill) * Policy.AreaCost(fill.SectionId));
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
            _s.Spent = 0;
        }

        public bool Undo()
        {
            if (_undo.Count == 0)
                return false;
            _s = _undo.Pop();
            _groupSaved = false;
            Version++;
            return true;
        }

        /// <summary>
        /// Starts a group of edits that one Undo takes back together (a stroke and its mirror copy). Groups nest;
        /// only the outermost counts. Always pair with EndGroup.
        /// </summary>
        public void BeginGroup()
        {
            if (_groupDepth++ == 0)
                _groupSaved = false;
        }

        public void EndGroup()
        {
            if (_groupDepth > 0)
                _groupDepth--;
        }

        /// <summary>An independent copy with its own empty undo stack (tests run on a copy, spec 2.11).</summary>
        public BridgeModel Copy()
        {
            var copy = new BridgeModel(Policy) { _s = _s.Clone() };
            return copy;
        }

        void Push()
        {
            if (_groupDepth == 0 || !_groupSaved)
                _undo.Push(_s.Clone());
            _groupSaved = _groupDepth > 0;
            Version++;
        }
    }
}
