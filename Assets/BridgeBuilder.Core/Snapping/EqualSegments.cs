using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Equal-segment snapping (spec section 2.3). A straight run is divided into the nearest whole
    /// number of equal segments at the target spacing, and every division point is a solver node.
    /// Positions and lengths are in room units.
    /// </summary>
    public static class EqualSegments
    {
        // Points returns one more entry than the segment count, so the count must leave room in an int.
        private const int MaxSegmentCount = int.MaxValue - 1;

        /// <summary>Segment count for <paramref name="length"/> at <see cref="Constants.Snapping.TargetSpacingMeters"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is not positive and finite.</exception>
        public static int Count(double length)
        {
            return Count(length, Constants.Snapping.TargetSpacingMeters);
        }

        /// <summary>
        /// The nearest whole number of segments of about <paramref name="spacing"/> that make up
        /// <paramref name="length"/>. A half step rounds away from zero, and any positive length gets
        /// at least one segment.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="length"/> or <paramref name="spacing"/> is not positive and finite, or the count does not fit an int.
        /// </exception>
        public static int Count(double length, double spacing)
        {
            RequirePositiveFinite(length, nameof(length));
            RequirePositiveFinite(spacing, nameof(spacing));

            decimal segments;
            try
            {
                // Decimal keeps a ratio such as 1.5 exact; the double quotient can land just below the half step.
                decimal spacingValue = (decimal)spacing;
                if (spacingValue == 0m)
                {
                    throw new ArgumentOutOfRangeException(nameof(spacing), spacing, "Spacing is too small to count segments.");
                }

                segments = Math.Round((decimal)length / spacingValue, MidpointRounding.AwayFromZero);
            }
            catch (OverflowException exception)
            {
                throw new ArgumentOutOfRangeException("The ratio of length to spacing is too large to count.", exception);
            }

            if (segments > MaxSegmentCount)
            {
                throw new ArgumentOutOfRangeException(nameof(length), length, "Too many segments at this spacing.");
            }

            return segments < 1m ? 1 : (int)segments;
        }

        /// <summary>Division points from <paramref name="start"/> to <paramref name="end"/> at <see cref="Constants.Snapping.TargetSpacingMeters"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The two points coincide.</exception>
        public static Vec3[] Points(Vec3 start, Vec3 end)
        {
            return Points(start, end, Constants.Snapping.TargetSpacingMeters);
        }

        /// <summary>
        /// The <see cref="Count(double, double)"/> + 1 equally spaced points from <paramref name="start"/>
        /// to <paramref name="end"/>, in order. The first entry is exactly <paramref name="start"/> and the
        /// last is exactly <paramref name="end"/>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The two points coincide or are infinitely far apart, or <paramref name="spacing"/> is not positive and finite.
        /// </exception>
        public static Vec3[] Points(Vec3 start, Vec3 end, double spacing)
        {
            int count = Count(Vec3.Distance(start, end), spacing);
            Vec3 chord = end - start;
            var points = new Vec3[count + 1];
            points[0] = start;
            for (int i = 1; i < count; i++)
            {
                points[i] = start + chord * ((double)i / count);
            }

            points[count] = end;
            return points;
        }

        /// <summary>
        /// Snaps a polyline at <see cref="Constants.Snapping.TargetSpacingMeters"/>.
        /// See <see cref="SnapPolyline(IReadOnlyList{Node}, IReadOnlyList{Member}, IReadOnlyList{Vec3}, double)"/>.
        /// </summary>
        public static SnappedPolyline SnapPolyline(IReadOnlyList<Node> existingNodes, IReadOnlyList<Member> existingMembers, IReadOnlyList<Vec3> vertices)
        {
            return SnapPolyline(existingNodes, existingMembers, vertices, Constants.Snapping.TargetSpacingMeters);
        }

        /// <summary>
        /// Turns a drawn polyline into a chain of nodes and members without changing the existing bridge.
        /// The first vertex must be the exact position of an existing node. Each straight leg between
        /// consecutive vertices is divided on its own, so every vertex becomes a node. A point at the exact
        /// position of an existing node reuses that node, and a leg whose two end nodes are already joined by
        /// a member reuses that member instead of adding points along it. Existing nodes and members are
        /// never modified or replaced, so no member is ever split. New node ids continue after the largest
        /// existing node id, and new member ids after the largest existing member id.
        /// </summary>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentException">
        /// There are fewer than two vertices, a list holds a null entry, the first vertex is not the position
        /// of an existing node, or a segment is too short to give two distinct nodes.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Two consecutive vertices coincide, or <paramref name="spacing"/> is not positive and finite.
        /// </exception>
        public static SnappedPolyline SnapPolyline(IReadOnlyList<Node> existingNodes, IReadOnlyList<Member> existingMembers, IReadOnlyList<Vec3> vertices, double spacing)
        {
            if (existingNodes == null)
            {
                throw new ArgumentNullException(nameof(existingNodes));
            }

            if (existingMembers == null)
            {
                throw new ArgumentNullException(nameof(existingMembers));
            }

            if (vertices == null)
            {
                throw new ArgumentNullException(nameof(vertices));
            }

            if (vertices.Count < 2)
            {
                throw new ArgumentException("A polyline needs at least two vertices.", nameof(vertices));
            }

            RequirePositiveFinite(spacing, nameof(spacing));

            var nodes = new List<Node>(existingNodes.Count);
            int nextNodeId = 0;
            foreach (Node node in existingNodes)
            {
                if (node == null)
                {
                    throw new ArgumentException("The node list holds a null entry.", nameof(existingNodes));
                }

                nodes.Add(node);
                if (node.Id >= nextNodeId)
                {
                    nextNodeId = checked(node.Id + 1);
                }
            }

            var members = new List<Member>(existingMembers.Count);
            int nextMemberId = 0;
            foreach (Member member in existingMembers)
            {
                if (member == null)
                {
                    throw new ArgumentException("The member list holds a null entry.", nameof(existingMembers));
                }

                members.Add(member);
                if (member.Id >= nextMemberId)
                {
                    nextMemberId = checked(member.Id + 1);
                }
            }

            int? startId = FindNodeAt(nodes, vertices[0]);
            if (!startId.HasValue)
            {
                throw new ArgumentException("A polyline must start at the position of an existing node.", nameof(vertices));
            }

            var nodeIds = new List<int> { startId.Value };
            var memberIds = new List<int>();
            var newNodes = new List<Node>();
            var newMembers = new List<Member>();
            int current = startId.Value;

            for (int leg = 1; leg < vertices.Count; leg++)
            {
                int? endId = FindNodeAt(nodes, vertices[leg]);
                Member joining = endId.HasValue && endId.Value != current ? FindMember(members, current, endId.Value) : null;
                if (joining != null)
                {
                    memberIds.Add(joining.Id);
                    nodeIds.Add(endId.Value);
                    current = endId.Value;
                    continue;
                }

                Vec3[] points = Points(vertices[leg - 1], vertices[leg], spacing);
                for (int i = 1; i < points.Length; i++)
                {
                    int next;
                    int? found = FindNodeAt(nodes, points[i]);
                    if (found.HasValue)
                    {
                        next = found.Value;
                    }
                    else
                    {
                        var node = new Node { Id = nextNodeId, Position = points[i] };
                        nextNodeId = checked(nextNodeId + 1);
                        nodes.Add(node);
                        newNodes.Add(node);
                        next = node.Id;
                    }

                    if (next == current)
                    {
                        throw new ArgumentException("A segment is too short to give two distinct nodes.", nameof(vertices));
                    }

                    Member member = FindMember(members, current, next);
                    if (member == null)
                    {
                        member = new Member { Id = nextMemberId, NodeA = current, NodeB = next };
                        nextMemberId = checked(nextMemberId + 1);
                        members.Add(member);
                        newMembers.Add(member);
                    }

                    memberIds.Add(member.Id);
                    nodeIds.Add(next);
                    current = next;
                }
            }

            return new SnappedPolyline
            {
                NodeIds = nodeIds.ToArray(),
                MemberIds = memberIds.ToArray(),
                NewNodes = newNodes.ToArray(),
                NewMembers = newMembers.ToArray(),
            };
        }

        private static int? FindNodeAt(List<Node> nodes, Vec3 position)
        {
            foreach (Node node in nodes)
            {
                if (node.Position == position)
                {
                    return node.Id;
                }
            }

            return null;
        }

        private static Member FindMember(List<Member> members, int nodeA, int nodeB)
        {
            foreach (Member member in members)
            {
                if ((member.NodeA == nodeA && member.NodeB == nodeB) || (member.NodeA == nodeB && member.NodeB == nodeA))
                {
                    return member;
                }
            }

            return null;
        }

        private static void RequirePositiveFinite(double value, string paramName)
        {
            if (!(value > 0.0) || double.IsPositiveInfinity(value))
            {
                throw new ArgumentOutOfRangeException(paramName, value, "Must be positive and finite.");
            }
        }
    }

    /// <summary>The chain <see cref="EqualSegments.SnapPolyline(IReadOnlyList{Node}, IReadOnlyList{Member}, IReadOnlyList{Vec3}, double)"/> builds for one polyline.</summary>
    public class SnappedPolyline
    {
        /// <summary>Node ids along the chain from the start node to the end, existing or new.</summary>
        public int[] NodeIds { get; set; } = Array.Empty<int>();

        /// <summary>Member ids in chain order, existing or new. <c>MemberIds[k]</c> joins <c>NodeIds[k]</c> and <c>NodeIds[k + 1]</c>.</summary>
        public int[] MemberIds { get; set; } = Array.Empty<int>();

        /// <summary>Nodes the chain adds, in chain order. They are not fixed, not on a surface, and not ground anchors.</summary>
        public Node[] NewNodes { get; set; } = Array.Empty<Node>();

        /// <summary>
        /// Members the chain adds, in chain order, each from its earlier chain node (NodeA) to its later one (NodeB).
        /// SectionId and LineId are left 0 for the caller to set when it records the line.
        /// </summary>
        public Member[] NewMembers { get; set; } = Array.Empty<Member>();
    }
}
