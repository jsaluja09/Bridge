using System;
using System.Collections.Generic;
using System.Globalization;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// What <see cref="SupportPartition.Split"/> found in one input. Node and member ids are listed in
    /// the order of the input's <see cref="SolverInput.Nodes"/> and <see cref="SolverInput.Members"/>.
    /// </summary>
    public sealed class SupportPartitionResult
    {
        internal SupportPartitionResult(
            SolverInput supportedInput,
            int[] supportedNodeIndices,
            int[] supportedMemberIndices,
            int[] unsupportedNodeIds,
            int[] unsupportedMemberIds,
            int[][] unsupportedPieces)
        {
            SupportedInput = supportedInput;
            SupportedNodeIndices = supportedNodeIndices;
            SupportedMemberIndices = supportedMemberIndices;
            UnsupportedNodeIds = unsupportedNodeIds;
            UnsupportedMemberIds = unsupportedMemberIds;
            UnsupportedPieces = unsupportedPieces;
        }

        /// <summary>
        /// The supported nodes and members in their original relative order, with the input's own node and
        /// member objects, <see cref="SolverInput.Sections"/> array, and <see cref="SolverInput.ScaleFactor"/>.
        /// <see cref="SolverInput.Fills"/> keeps, in order, only the fills whose nodes are all supported, so
        /// every fill names nodes in this input; it is the input's own array when no fill is dropped.
        /// </summary>
        public SolverInput SupportedInput { get; }

        /// <summary><c>SupportedNodeIndices[i]</c> is the index in the full input's nodes of <c>SupportedInput.Nodes[i]</c>.</summary>
        public int[] SupportedNodeIndices { get; }

        /// <summary><c>SupportedMemberIndices[k]</c> is the index in the full input's members of <c>SupportedInput.Members[k]</c>.</summary>
        public int[] SupportedMemberIndices { get; }

        /// <summary>Ids of the nodes that reach no fixed node, including free nodes with no member.</summary>
        public int[] UnsupportedNodeIds { get; }

        /// <summary>Ids of the members that reach no fixed node.</summary>
        public int[] UnsupportedMemberIds { get; }

        /// <summary>
        /// The unsupported members grouped into pieces that share nodes. Pieces are ordered by their first
        /// member, and every member id appears in exactly one piece.
        /// </summary>
        public int[][] UnsupportedPieces { get; }

        /// <summary>True when at least one member reaches a fixed node.</summary>
        public bool HasSupportedMembers => SupportedMemberIndices.Length > 0;
    }

    /// <summary>
    /// Splits a frame into what is supported and what is not (spec 2.5 and 11.1). A node is supported
    /// when it is fixed or joined to a fixed node through a chain of members, and a member is supported
    /// when its nodes are. A connected rigid-jointed frame with a fixed node is never a mechanism, so
    /// the supported part can be solved even when free-drawn lines touch nothing.
    /// </summary>
    public static class SupportPartition
    {
        /// <summary>
        /// Splits <paramref name="input"/> into its supported part and its unsupported pieces. The input
        /// is not modified. Sections, positions, and the fills that are kept are not checked here.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="input"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// The node or member array is null or holds a null entry, a node or member id is repeated, or a
        /// member names a missing node.
        /// </exception>
        public static SupportPartitionResult Split(SolverInput input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            Node[] nodes = input.Nodes ?? throw new ArgumentException("The input has no node array.", nameof(input));
            Member[] members = input.Members ?? throw new ArgumentException("The input has no member array.", nameof(input));
            Dictionary<int, int> nodeIndexById = IndexNodes(nodes);

            var root = new int[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                root[i] = i;
            }

            var nodeA = new int[members.Length];
            var nodeB = new int[members.Length];
            var memberIds = new HashSet<int>();
            for (int m = 0; m < members.Length; m++)
            {
                Member member = members[m] ?? throw new ArgumentException("The member array holds a null entry.", nameof(input));
                if (!memberIds.Add(member.Id))
                {
                    throw new ArgumentException("Member id " + Format(member.Id) + " is repeated.", nameof(input));
                }

                nodeA[m] = RequireNode(nodeIndexById, member.NodeA, member);
                nodeB[m] = RequireNode(nodeIndexById, member.NodeB, member);
                Union(root, nodeA[m], nodeB[m]);
            }

            var groundedRoot = new bool[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].Fixed)
                {
                    groundedRoot[Find(root, i)] = true;
                }
            }

            var supportedNodes = new List<Node>();
            var supportedNodeIndices = new List<int>();
            var supportedNodeIds = new HashSet<int>();
            var unsupportedNodeIds = new List<int>();
            for (int i = 0; i < nodes.Length; i++)
            {
                if (groundedRoot[Find(root, i)])
                {
                    supportedNodes.Add(nodes[i]);
                    supportedNodeIndices.Add(i);
                    supportedNodeIds.Add(nodes[i].Id);
                }
                else
                {
                    unsupportedNodeIds.Add(nodes[i].Id);
                }
            }

            var supportedMembers = new List<Member>();
            var supportedMemberIndices = new List<int>();
            var unsupportedMemberIds = new List<int>();
            var pieceOfRoot = new Dictionary<int, List<int>>();
            var pieces = new List<List<int>>();
            for (int m = 0; m < members.Length; m++)
            {
                int memberRoot = Find(root, nodeA[m]);
                if (groundedRoot[memberRoot])
                {
                    supportedMembers.Add(members[m]);
                    supportedMemberIndices.Add(m);
                    continue;
                }

                unsupportedMemberIds.Add(members[m].Id);
                if (!pieceOfRoot.TryGetValue(memberRoot, out List<int> piece))
                {
                    piece = new List<int>();
                    pieceOfRoot.Add(memberRoot, piece);
                    pieces.Add(piece);
                }

                piece.Add(members[m].Id);
            }

            var supportedInput = new SolverInput
            {
                Nodes = supportedNodes.ToArray(),
                Members = supportedMembers.ToArray(),
                Fills = SupportedFills(input.Fills, supportedNodeIds),
                Sections = input.Sections,
                ScaleFactor = input.ScaleFactor,
            };
            var pieceArrays = new int[pieces.Count][];
            for (int p = 0; p < pieces.Count; p++)
            {
                pieceArrays[p] = pieces[p].ToArray();
            }

            return new SupportPartitionResult(
                supportedInput,
                supportedNodeIndices.ToArray(),
                supportedMemberIndices.ToArray(),
                unsupportedNodeIds.ToArray(),
                unsupportedMemberIds.ToArray(),
                pieceArrays);
        }

        /// <summary>
        /// The fills whose node ids are all supported, in order, or <paramref name="fills"/> itself when none
        /// is dropped. A null fill or node id array is kept unchecked, for whoever reads the fills to reject.
        /// </summary>
        private static Fill[] SupportedFills(Fill[] fills, HashSet<int> supportedNodeIds)
        {
            if (fills == null)
            {
                return null;
            }

            var kept = new List<Fill>(fills.Length);
            foreach (Fill fill in fills)
            {
                if (fill?.NodeIds == null || Array.TrueForAll(fill.NodeIds, supportedNodeIds.Contains))
                {
                    kept.Add(fill);
                }
            }

            return kept.Count == fills.Length ? fills : kept.ToArray();
        }

        private static int Find(int[] root, int node)
        {
            while (root[node] != node)
            {
                root[node] = root[root[node]];
                node = root[node];
            }

            return node;
        }

        private static void Union(int[] root, int a, int b)
        {
            int rootA = Find(root, a);
            int rootB = Find(root, b);
            if (rootA != rootB)
            {
                root[rootB] = rootA;
            }
        }

        private static Dictionary<int, int> IndexNodes(Node[] nodes)
        {
            var indexById = new Dictionary<int, int>(nodes.Length);
            for (int i = 0; i < nodes.Length; i++)
            {
                Node node = nodes[i] ?? throw new ArgumentException("The node array holds a null entry.", "input");
                if (indexById.ContainsKey(node.Id))
                {
                    throw new ArgumentException("Node id " + Format(node.Id) + " is repeated.", "input");
                }

                indexById.Add(node.Id, i);
            }

            return indexById;
        }

        private static int RequireNode(Dictionary<int, int> nodeIndexById, int nodeId, Member member)
        {
            if (!nodeIndexById.TryGetValue(nodeId, out int index))
            {
                throw new ArgumentException("Member " + Format(member.Id) + " references missing node " + Format(nodeId) + ".", "input");
            }

            return index;
        }

        private static string Format(int id)
        {
            return id.ToString(CultureInfo.InvariantCulture);
        }
    }
}
