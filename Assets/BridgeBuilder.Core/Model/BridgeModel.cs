using System;
using System.Collections.Generic;
using System.Linq;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Budget numbers for one bridge (spec 2.9): line cost per meter, fill cost per square meter,
    /// the material budget, and the hidden node cap. Lengths and areas are in room units.
    /// </summary>
    public sealed class BudgetPolicy
    {
        public BudgetPolicy(double costPerMeter, double costPerSquareMeter, double materialBudget, int nodeCap)
        {
            RequireFiniteNonNegative(costPerMeter, nameof(costPerMeter));
            RequireFiniteNonNegative(costPerSquareMeter, nameof(costPerSquareMeter));
            RequireFiniteNonNegative(materialBudget, nameof(materialBudget));
            if (nodeCap < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(nodeCap), nodeCap, "The node cap must not be negative.");
            }

            CostPerMeter = costPerMeter;
            CostPerSquareMeter = costPerSquareMeter;
            MaterialBudget = materialBudget;
            NodeCap = nodeCap;
        }

        /// <summary>The <see cref="Constants.Budget"/> values with <see cref="Constants.Human.NodeCap"/>.</summary>
        public static BudgetPolicy Default { get; } = new BudgetPolicy(
            Constants.Budget.CostPerMeter,
            Constants.Budget.CostPerSquareMeter,
            Constants.Budget.MaterialBudget,
            Constants.Human.NodeCap);

        public double CostPerMeter { get; }

        public double CostPerSquareMeter { get; }

        public double MaterialBudget { get; }

        /// <summary>Most nodes the model may hold, fixed nodes included. Going over it reads to the player as an exceeded budget.</summary>
        public int NodeCap { get; }

        private static void RequireFiniteNonNegative(double value, string paramName)
        {
            if (!double.IsFinite(value) || value < 0.0)
            {
                throw new ArgumentOutOfRangeException(paramName, value, "Budget values must be finite and not negative.");
            }
        }
    }

    /// <summary>Player-facing reason an edit was refused. The node cap has no reason of its own; it reads as <see cref="BudgetExceeded"/>.</summary>
    public enum EditRejectionReason
    {
        /// <summary>The edit was applied.</summary>
        None,

        /// <summary>The edit costs more than the remaining budget, or would put the node count over the cap. <see cref="BudgetCause"/> says which.</summary>
        BudgetExceeded,

        /// <summary>The line's first vertex is not an existing node (spec 2.3).</summary>
        MissingStartNode,

        /// <summary>A later line vertex, a fill corner, or a brace end names no existing node.</summary>
        UnknownNode,

        /// <summary>Too few vertices, a zero-length segment, a repeated or zero-area fill loop, or a non-finite value.</summary>
        InvalidGeometry,

        UnknownLine,

        UnknownFill,

        /// <summary>Members of other lines attach to nodes the line created (spec 2.11). <see cref="DeleteResult.DependentMemberIds"/> lists them.</summary>
        HasDependents,
    }

    /// <summary>Which limit refused an edit with <see cref="EditRejectionReason.BudgetExceeded"/>. Not shown to the player.</summary>
    public enum BudgetCause
    {
        None,
        MaterialBudget,
        NodeCap,
    }

    /// <summary>One vertex of a line to add: an existing node, or a position in room units for a new node.</summary>
    public readonly struct LineVertex
    {
        private LineVertex(bool isExistingNode, int nodeId, Vec3 position)
        {
            IsExistingNode = isExistingNode;
            NodeId = nodeId;
            Position = position;
        }

        public bool IsExistingNode { get; }

        /// <summary>The existing node, when <see cref="IsExistingNode"/> is true.</summary>
        public int NodeId { get; }

        /// <summary>Where the new node goes, when <see cref="IsExistingNode"/> is false.</summary>
        public Vec3 Position { get; }

        public static LineVertex AtNode(int nodeId)
        {
            return new LineVertex(true, nodeId, Vec3.Zero);
        }

        public static LineVertex AtPosition(Vec3 position)
        {
            return new LineVertex(false, 0, position);
        }
    }

    /// <summary>Outcome of adding a line, a fill, or a hidden brace.</summary>
    public sealed class EditResult
    {
        private EditResult(EditRejectionReason reason, BudgetCause cause, int id, int[] nodeIds, int[] memberIds, double cost)
        {
            Reason = reason;
            Cause = cause;
            Id = id;
            NodeIds = nodeIds;
            MemberIds = memberIds;
            Cost = cost;
        }

        public bool Succeeded => Reason == EditRejectionReason.None;

        public EditRejectionReason Reason { get; }

        /// <summary><see cref="BudgetCause.None"/> unless <see cref="Reason"/> is <see cref="EditRejectionReason.BudgetExceeded"/>.</summary>
        public BudgetCause Cause { get; }

        /// <summary>Id of the new line, fill, or hidden-brace member. 0 when rejected, because model ids start at 1.</summary>
        public int Id { get; }

        /// <summary>Nodes the edit created, in line order.</summary>
        public int[] NodeIds { get; }

        /// <summary>Members the edit created, in chain order.</summary>
        public int[] MemberIds { get; }

        /// <summary>Budget the edit spent. 0 for a hidden brace and for a rejection.</summary>
        public double Cost { get; }

        internal static EditResult Applied(int id, int[] nodeIds, int[] memberIds, double cost)
        {
            return new EditResult(EditRejectionReason.None, BudgetCause.None, id, nodeIds, memberIds, cost);
        }

        internal static EditResult Rejected(EditRejectionReason reason, BudgetCause cause = BudgetCause.None)
        {
            return new EditResult(reason, cause, 0, Array.Empty<int>(), Array.Empty<int>(), 0.0);
        }
    }

    /// <summary>Outcome of deleting a line or a fill (spec 2.11).</summary>
    public sealed class DeleteResult
    {
        private DeleteResult(EditRejectionReason reason, int[] dependentMemberIds, int[] invalidatedFillIds, double refund)
        {
            Reason = reason;
            DependentMemberIds = dependentMemberIds;
            InvalidatedFillIds = invalidatedFillIds;
            Refund = refund;
        }

        public bool Succeeded => Reason == EditRejectionReason.None;

        public EditRejectionReason Reason { get; }

        /// <summary>When the delete is blocked, the members of other lines that attach to nodes the line created, ascending. The game highlights them.</summary>
        public int[] DependentMemberIds { get; }

        /// <summary>Fills this delete made invalid, ascending. The game warns about them.</summary>
        public int[] InvalidatedFillIds { get; }

        /// <summary>Budget returned: the deleted item's cost plus the area cost of every fill it invalidated.</summary>
        public double Refund { get; }

        internal static DeleteResult Applied(int[] invalidatedFillIds, double refund)
        {
            return new DeleteResult(EditRejectionReason.None, Array.Empty<int>(), invalidatedFillIds, refund);
        }

        internal static DeleteResult Rejected(EditRejectionReason reason, int[] dependentMemberIds = null)
        {
            return new DeleteResult(reason, dependentMemberIds ?? Array.Empty<int>(), Array.Empty<int>(), 0.0);
        }
    }

    /// <summary>
    /// The player's bridge (spec sections 2.3, 2.9, and 2.11): nodes, members, lines, fills, the
    /// material budget, and one undo stack. Positions are room units. Ids start at 1 and are never
    /// reused, including after undo. Every successful edit pushes exactly one undo snapshot; a
    /// rejected edit changes nothing and pushes nothing.
    /// </summary>
    public sealed class BridgeModel
    {
        private readonly List<State> undoStack = new List<State>();
        private State state = new State();
        private int nextNodeId = 1;
        private int nextMemberId = 1;
        private int nextLineId = 1;
        private int nextFillId = 1;

        public BridgeModel()
            : this(BudgetPolicy.Default)
        {
        }

        public BridgeModel(BudgetPolicy policy)
        {
            Policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        public BudgetPolicy Policy { get; }

        /// <summary>Copies of the nodes in id order. Changing a copy does not change the model.</summary>
        public IReadOnlyList<Node> Nodes => state.Nodes.Select(CloneNode).ToArray();

        /// <summary>Copies of the members, hidden braces included, in id order.</summary>
        public IReadOnlyList<Member> Members => state.Members.Select(CloneMember).ToArray();

        /// <summary>Copies of the lines in id order.</summary>
        public IReadOnlyList<Line> Lines => state.Lines.Select(entry => CloneLine(entry.Line)).ToArray();

        /// <summary>Copies of the fills in id order, invalid fills included. An invalid fill may name nodes that no longer exist.</summary>
        public IReadOnlyList<Fill> Fills => state.Fills.Select(entry => CloneFill(entry.Fill)).ToArray();

        public int NodeCount => state.Nodes.Count;

        public int MemberCount => state.Members.Count;

        public int LineCount => state.Lines.Count;

        public int FillCount => state.Fills.Count;

        /// <summary>Cost of every line plus every valid fill.</summary>
        public double SpentBudget
        {
            get
            {
                double spent = 0.0;
                foreach (LineEntry line in state.Lines)
                {
                    spent += line.Cost;
                }

                foreach (FillEntry fill in state.Fills)
                {
                    if (fill.Fill.Valid)
                    {
                        spent += fill.Cost;
                    }
                }

                return spent;
            }
        }

        public double RemainingBudget => Policy.MaterialBudget - SpentBudget;

        public int UndoDepth => undoStack.Count;

        /// <summary>
        /// Adds a support: a surface snap dot or a ground-plane dot (spec 2.2 and 2.5). This is
        /// setup, not an edit. It is free, pushes no undo, survives undo and <see cref="Reset"/>,
        /// and counts toward the node cap without being refused by it.
        /// </summary>
        /// <returns>The new node id.</returns>
        public int AddFixedNode(Vec3 position, int? surfaceId = null, bool groundAnchor = false)
        {
            if (!IsFinite(position))
            {
                throw new ArgumentException("A fixed node needs a finite position.", nameof(position));
            }

            var node = new Node { Id = nextNodeId++, Position = position, Fixed = true, SurfaceId = surfaceId, GroundAnchor = groundAnchor };
            state.Nodes.Add(node);
            foreach (State snapshot in undoStack)
            {
                snapshot.Nodes.Add(CloneNode(node));
            }

            return node.Id;
        }

        /// <summary>
        /// Adds a line: a chain of members through <paramref name="vertices"/>. The first vertex must
        /// be an existing node (spec 2.3). Each later vertex is an existing node or a new free node.
        /// Existing members are never changed. The cost is the summed segment length times
        /// <see cref="BudgetPolicy.CostPerMeter"/>.
        /// </summary>
        public EditResult TryAddLine(IReadOnlyList<LineVertex> vertices, int sectionId)
        {
            if (vertices == null)
            {
                throw new ArgumentNullException(nameof(vertices));
            }

            if (vertices.Count == 0 || !vertices[0].IsExistingNode || FindNode(vertices[0].NodeId) == null)
            {
                return EditResult.Rejected(EditRejectionReason.MissingStartNode);
            }

            if (vertices.Count < 2)
            {
                return EditResult.Rejected(EditRejectionReason.InvalidGeometry);
            }

            var positions = new Vec3[vertices.Count];
            int newNodeCount = 0;
            for (int i = 0; i < vertices.Count; i++)
            {
                LineVertex vertex = vertices[i];
                if (vertex.IsExistingNode)
                {
                    Node node = FindNode(vertex.NodeId);
                    if (node == null)
                    {
                        return EditResult.Rejected(EditRejectionReason.UnknownNode);
                    }

                    positions[i] = node.Position;
                }
                else
                {
                    if (!IsFinite(vertex.Position))
                    {
                        return EditResult.Rejected(EditRejectionReason.InvalidGeometry);
                    }

                    positions[i] = vertex.Position;
                    newNodeCount++;
                }
            }

            double length = 0.0;
            for (int i = 1; i < positions.Length; i++)
            {
                double segment = Vec3.Distance(positions[i - 1], positions[i]);
                if (!(segment > 0.0))
                {
                    return EditResult.Rejected(EditRejectionReason.InvalidGeometry);
                }

                length += segment;
            }

            double cost = length * Policy.CostPerMeter;
            if (!double.IsFinite(length) || !double.IsFinite(cost))
            {
                return EditResult.Rejected(EditRejectionReason.InvalidGeometry);
            }

            if (cost > RemainingBudget)
            {
                return EditResult.Rejected(EditRejectionReason.BudgetExceeded, BudgetCause.MaterialBudget);
            }

            if (state.Nodes.Count + newNodeCount > Policy.NodeCap)
            {
                return EditResult.Rejected(EditRejectionReason.BudgetExceeded, BudgetCause.NodeCap);
            }

            PushUndo();
            int lineId = nextLineId++;
            var vertexIds = new int[vertices.Count];
            var createdNodeIds = new int[newNodeCount];
            int created = 0;
            for (int i = 0; i < vertices.Count; i++)
            {
                if (vertices[i].IsExistingNode)
                {
                    vertexIds[i] = vertices[i].NodeId;
                    continue;
                }

                var node = new Node { Id = nextNodeId++, Position = positions[i] };
                state.Nodes.Add(node);
                vertexIds[i] = node.Id;
                createdNodeIds[created++] = node.Id;
            }

            var memberIds = new int[vertices.Count - 1];
            for (int i = 0; i < memberIds.Length; i++)
            {
                var member = new Member
                {
                    Id = nextMemberId++,
                    NodeA = vertexIds[i],
                    NodeB = vertexIds[i + 1],
                    SectionId = sectionId,
                    LineId = lineId,
                };
                state.Members.Add(member);
                memberIds[i] = member.Id;
            }

            var line = new Line { Id = lineId, MemberIds = (int[])memberIds.Clone() };
            state.Lines.Add(new LineEntry(line, (int[])createdNodeIds.Clone(), cost));
            return EditResult.Applied(lineId, createdNodeIds, memberIds, cost);
        }

        /// <summary>
        /// Adds a valid fill over the closed loop <paramref name="nodeIds"/> (first id not repeated).
        /// Creates no nodes. The cost is the absolute Newell area times
        /// <see cref="BudgetPolicy.CostPerSquareMeter"/>. Planarity and bracing are checked elsewhere.
        /// </summary>
        public EditResult TryAddFill(IReadOnlyList<int> nodeIds, bool road)
        {
            if (nodeIds == null)
            {
                throw new ArgumentNullException(nameof(nodeIds));
            }

            if (nodeIds.Count < 3)
            {
                return EditResult.Rejected(EditRejectionReason.InvalidGeometry);
            }

            var loop = new Vec3[nodeIds.Count];
            var seen = new HashSet<int>();
            for (int i = 0; i < nodeIds.Count; i++)
            {
                Node node = FindNode(nodeIds[i]);
                if (node == null)
                {
                    return EditResult.Rejected(EditRejectionReason.UnknownNode);
                }

                if (!seen.Add(node.Id))
                {
                    return EditResult.Rejected(EditRejectionReason.InvalidGeometry);
                }

                loop[i] = node.Position;
            }

            double area = NewellArea(loop);
            double cost = area * Policy.CostPerSquareMeter;
            if (!(area > 0.0) || !double.IsFinite(area) || !double.IsFinite(cost))
            {
                return EditResult.Rejected(EditRejectionReason.InvalidGeometry);
            }

            if (cost > RemainingBudget)
            {
                return EditResult.Rejected(EditRejectionReason.BudgetExceeded, BudgetCause.MaterialBudget);
            }

            PushUndo();
            var fill = new Fill { Id = nextFillId++, NodeIds = nodeIds.ToArray(), Road = road, Valid = true };
            state.Fills.Add(new FillEntry(fill, cost));
            return EditResult.Applied(fill.Id, Array.Empty<int>(), Array.Empty<int>(), cost);
        }

        /// <summary>
        /// Adds a hidden fill-bracing member (spec 2.4) between two existing nodes. It belongs to no
        /// line (<see cref="Member.LineId"/> is 0), creates no nodes, and costs nothing.
        /// </summary>
        public EditResult TryAddHiddenBrace(int nodeA, int nodeB, int sectionId)
        {
            Node a = FindNode(nodeA);
            Node b = FindNode(nodeB);
            if (a == null || b == null)
            {
                return EditResult.Rejected(EditRejectionReason.UnknownNode);
            }

            double length = Vec3.Distance(a.Position, b.Position);
            if (!(length > 0.0) || !double.IsFinite(length))
            {
                return EditResult.Rejected(EditRejectionReason.InvalidGeometry);
            }

            PushUndo();
            var brace = new Member { Id = nextMemberId++, NodeA = nodeA, NodeB = nodeB, SectionId = sectionId, LineId = 0, HiddenBrace = true };
            state.Members.Add(brace);
            return EditResult.Applied(brace.Id, Array.Empty<int>(), new[] { brace.Id }, 0.0);
        }

        /// <summary>
        /// Deletes a line, its members, and the nodes it created, and refunds its cost (spec 2.11).
        /// Blocked when a member of another line uses a node this line created. A valid fill is
        /// invalidated, loses its road flag, and is refunded when one of the line's members matches
        /// one of its loop edges, closing edge included, or when it uses a node the delete removes.
        /// Hidden braces on removed nodes or on invalidated fills go too.
        /// </summary>
        public DeleteResult TryDeleteLine(int lineId)
        {
            LineEntry entry = state.Lines.Find(candidate => candidate.Line.Id == lineId);
            if (entry == null)
            {
                return DeleteResult.Rejected(EditRejectionReason.UnknownLine);
            }

            var removedNodes = new HashSet<int>(entry.CreatedNodeIds);
            int[] dependents = state.Members
                .Where(member => !member.HiddenBrace && member.LineId != lineId && (removedNodes.Contains(member.NodeA) || removedNodes.Contains(member.NodeB)))
                .Select(member => member.Id)
                .OrderBy(id => id)
                .ToArray();
            if (dependents.Length > 0)
            {
                return DeleteResult.Rejected(EditRejectionReason.HasDependents, dependents);
            }

            var lineMemberIds = new HashSet<int>(entry.Line.MemberIds);
            Member[] lineMembers = state.Members.Where(member => lineMemberIds.Contains(member.Id)).ToArray();
            FillEntry[] invalidated = state.Fills
                .Where(fill => fill.Fill.Valid && (Borders(lineMembers, fill.Fill.NodeIds) || fill.Fill.NodeIds.Any(removedNodes.Contains)))
                .ToArray();

            PushUndo();
            double refund = entry.Cost;
            state.Lines.Remove(entry);
            state.Members.RemoveAll(member => lineMemberIds.Contains(member.Id) || removedNodes.Contains(member.NodeA) || removedNodes.Contains(member.NodeB));
            state.Nodes.RemoveAll(node => removedNodes.Contains(node.Id));
            foreach (FillEntry fill in invalidated)
            {
                fill.Fill.Valid = false;
                fill.Fill.Road = false;
                refund += fill.Cost;
            }

            RemoveBracesOf(invalidated.Select(fill => fill.Fill));
            return DeleteResult.Applied(invalidated.Select(fill => fill.Fill.Id).OrderBy(id => id).ToArray(), refund);
        }

        /// <summary>
        /// Deletes a fill record and its hidden braces, leaving the frame lines in place. A valid
        /// fill refunds its area cost; an invalid one was already refunded.
        /// </summary>
        public DeleteResult TryDeleteFill(int fillId)
        {
            FillEntry entry = state.Fills.Find(candidate => candidate.Fill.Id == fillId);
            if (entry == null)
            {
                return DeleteResult.Rejected(EditRejectionReason.UnknownFill);
            }

            PushUndo();
            double refund = entry.Fill.Valid ? entry.Cost : 0.0;
            state.Fills.Remove(entry);
            RemoveBracesOf(new[] { entry.Fill });
            return DeleteResult.Applied(Array.Empty<int>(), refund);
        }

        /// <summary>
        /// Clears the bridge: drops every line, member, fill, and free node and keeps the fixed
        /// nodes. Undoable. Returns false, and pushes nothing, when there is nothing to clear.
        /// </summary>
        public bool Reset()
        {
            if (state.Lines.Count == 0 && state.Members.Count == 0 && state.Fills.Count == 0 && state.Nodes.TrueForAll(node => node.Fixed))
            {
                return false;
            }

            PushUndo();
            state.Lines.Clear();
            state.Members.Clear();
            state.Fills.Clear();
            state.Nodes.RemoveAll(node => !node.Fixed);
            return true;
        }

        /// <summary>Restores the state before the last successful edit. Returns false when there is nothing to undo.</summary>
        public bool Undo()
        {
            if (undoStack.Count == 0)
            {
                return false;
            }

            int last = undoStack.Count - 1;
            state = undoStack[last];
            undoStack.RemoveAt(last);
            return true;
        }

        /// <summary>
        /// An independent deep copy with the same ids, policy, and spent budget and an empty undo
        /// stack. Tests run on a copy so the pre-test bridge comes back afterwards (spec 2.11).
        /// </summary>
        public BridgeModel Copy()
        {
            return new BridgeModel(Policy)
            {
                state = state.Clone(),
                nextNodeId = nextNodeId,
                nextMemberId = nextMemberId,
                nextLineId = nextLineId,
                nextFillId = nextFillId,
            };
        }

        /// <summary>
        /// Builds a solver input from copies of the nodes, the members (hidden braces included), and
        /// the valid fills, so the solver cannot change this model.
        /// </summary>
        public SolverInput ToSolverInput(SectionProperties[] sections, double scaleFactor)
        {
            if (sections == null)
            {
                throw new ArgumentNullException(nameof(sections));
            }

            if (!(scaleFactor > 0.0) || !double.IsFinite(scaleFactor))
            {
                throw new ArgumentOutOfRangeException(nameof(scaleFactor), scaleFactor, "The scale factor must be positive and finite.");
            }

            return new SolverInput
            {
                Nodes = state.Nodes.Select(CloneNode).ToArray(),
                Members = state.Members.Select(CloneMember).ToArray(),
                Fills = state.Fills.Where(entry => entry.Fill.Valid).Select(entry => CloneFill(entry.Fill)).ToArray(),
                Sections = (SectionProperties[])sections.Clone(),
                ScaleFactor = scaleFactor,
            };
        }

        private void PushUndo()
        {
            undoStack.Add(state.Clone());
        }

        private Node FindNode(int id)
        {
            return state.Nodes.Find(node => node.Id == id);
        }

        /// <summary>Removes hidden braces with both ends on a released fill's loop unless a remaining valid fill also spans them.</summary>
        private void RemoveBracesOf(IEnumerable<Fill> released)
        {
            Fill[] releasedFills = released.ToArray();
            Fill[] validFills = state.Fills.Where(entry => entry.Fill.Valid).Select(entry => entry.Fill).ToArray();
            state.Members.RemoveAll(member =>
                member.HiddenBrace
                && releasedFills.Any(fill => Spans(fill, member))
                && !validFills.Any(fill => Spans(fill, member)));
        }

        private static bool Spans(Fill fill, Member member)
        {
            return Array.IndexOf(fill.NodeIds, member.NodeA) >= 0 && Array.IndexOf(fill.NodeIds, member.NodeB) >= 0;
        }

        private static bool Borders(Member[] lineMembers, int[] loop)
        {
            for (int i = 0; i < loop.Length; i++)
            {
                int a = loop[i];
                int b = loop[(i + 1) % loop.Length];
                foreach (Member member in lineMembers)
                {
                    if ((member.NodeA == a && member.NodeB == b) || (member.NodeA == b && member.NodeB == a))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static double NewellArea(Vec3[] loop)
        {
            double x = 0.0;
            double y = 0.0;
            double z = 0.0;
            for (int i = 0; i < loop.Length; i++)
            {
                Vec3 current = loop[i];
                Vec3 next = loop[(i + 1) % loop.Length];
                x += (current.Y - next.Y) * (current.Z + next.Z);
                y += (current.Z - next.Z) * (current.X + next.X);
                z += (current.X - next.X) * (current.Y + next.Y);
            }

            return 0.5 * Math.Sqrt(x * x + y * y + z * z);
        }

        private static bool IsFinite(Vec3 v)
        {
            return double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
        }

        private static Node CloneNode(Node node)
        {
            return new Node { Id = node.Id, Position = node.Position, Fixed = node.Fixed, SurfaceId = node.SurfaceId, GroundAnchor = node.GroundAnchor };
        }

        private static Member CloneMember(Member member)
        {
            return new Member
            {
                Id = member.Id,
                NodeA = member.NodeA,
                NodeB = member.NodeB,
                SectionId = member.SectionId,
                LineId = member.LineId,
                HiddenBrace = member.HiddenBrace,
            };
        }

        private static Line CloneLine(Line line)
        {
            return new Line { Id = line.Id, MemberIds = (int[])line.MemberIds.Clone() };
        }

        private static Fill CloneFill(Fill fill)
        {
            return new Fill { Id = fill.Id, NodeIds = (int[])fill.NodeIds.Clone(), Road = fill.Road, Valid = fill.Valid };
        }

        private sealed class LineEntry
        {
            public LineEntry(Line line, int[] createdNodeIds, double cost)
            {
                Line = line;
                CreatedNodeIds = createdNodeIds;
                Cost = cost;
            }

            public Line Line { get; }

            public int[] CreatedNodeIds { get; }

            public double Cost { get; }
        }

        private sealed class FillEntry
        {
            public FillEntry(Fill fill, double cost)
            {
                Fill = fill;
                Cost = cost;
            }

            public Fill Fill { get; }

            public double Cost { get; }
        }

        private sealed class State
        {
            public List<Node> Nodes { get; } = new List<Node>();

            public List<Member> Members { get; } = new List<Member>();

            public List<LineEntry> Lines { get; } = new List<LineEntry>();

            public List<FillEntry> Fills { get; } = new List<FillEntry>();

            public State Clone()
            {
                var copy = new State();
                copy.Nodes.AddRange(Nodes.Select(CloneNode));
                copy.Members.AddRange(Members.Select(CloneMember));
                copy.Lines.AddRange(Lines.Select(entry => new LineEntry(CloneLine(entry.Line), (int[])entry.CreatedNodeIds.Clone(), entry.Cost)));
                copy.Fills.AddRange(Fills.Select(entry => new FillEntry(CloneFill(entry.Fill), entry.Cost)));
                return copy;
            }
        }
    }
}
