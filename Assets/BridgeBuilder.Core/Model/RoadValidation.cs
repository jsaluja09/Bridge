using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Road-chain validation (spec 2.4). Only fills that are flagged as road and still valid take part.
    /// The chain passes when one connected group of road fills contains a node of each marked surface
    /// and no road fill is steeper than the maximum slope. Two road fills are adjacent when they share
    /// two node ids. Y is up.
    /// </summary>
    public static class RoadValidation
    {
        private const int SharedNodeIdsForAdjacency = 2;

        private const double DegreesPerRadian = 180.0 / Math.PI;

        // Absorbs rounding in the slope computation, so a fill built at exactly the maximum passes.
        private const double SlopeRoundingToleranceDegrees = 1e-9;

        /// <summary>Validates the road chain against <see cref="Constants.Road.MaxSlopeDegrees"/>.</summary>
        public static RoadValidationResult Validate(IReadOnlyList<Node> nodes, IReadOnlyList<Fill> fills, int surfaceIdA, int surfaceIdB)
        {
            return Validate(nodes, fills, surfaceIdA, surfaceIdB, Constants.Road.MaxSlopeDegrees);
        }

        /// <summary>
        /// Validates the road chain. A fill whose slope equals <paramref name="maxSlopeDegrees"/> passes.
        /// The gap and slope checks are independent, so a result can report both.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// The surface ids are equal, a road fill references a missing node, has a non-finite node
        /// position, or has no area.
        /// </exception>
        public static RoadValidationResult Validate(IReadOnlyList<Node> nodes, IReadOnlyList<Fill> fills, int surfaceIdA, int surfaceIdB, double maxSlopeDegrees)
        {
            if (fills == null)
            {
                throw new ArgumentNullException(nameof(fills));
            }

            if (surfaceIdA == surfaceIdB)
            {
                throw new ArgumentException("The two marked surfaces need different ids.", nameof(surfaceIdB));
            }

            if (double.IsNaN(maxSlopeDegrees) || maxSlopeDegrees < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSlopeDegrees), maxSlopeDegrees, "The maximum slope must be zero or more degrees.");
            }

            Dictionary<int, Node> nodesById = IndexNodes(nodes);
            var roadFills = new List<Fill>();
            foreach (Fill fill in fills)
            {
                if (fill == null)
                {
                    throw new ArgumentException("Fills must not contain null.", nameof(fills));
                }

                if (fill.Road && fill.Valid)
                {
                    roadFills.Add(fill);
                }
            }

            var reports = new RoadFillReport[roadFills.Count];
            var tooSteepFillIds = new List<int>();
            for (int i = 0; i < roadFills.Count; i++)
            {
                Fill fill = roadFills[i];
                Vec3[] corners = Corners(fill, nodesById);
                Vec3 unitNormal = UnitNormal(fill, corners);
                double slope = RequireFinite(fill, SlopeDegrees(unitNormal), "slope");
                double width = RequireFinite(fill, ShortestInPlaneEdge(corners, unitNormal), "width");
                bool tooSteep = slope > maxSlopeDegrees + SlopeRoundingToleranceDegrees;
                reports[i] = new RoadFillReport(fill.Id, slope, width, tooSteep);
                if (tooSteep)
                {
                    tooSteepFillIds.Add(fill.Id);
                }
            }

            RoadComponent[] components = Components(roadFills, nodesById, surfaceIdA, surfaceIdB);
            bool connected = false;
            foreach (RoadComponent component in components)
            {
                connected |= component.TouchesSurfaceA && component.TouchesSurfaceB;
            }

            return new RoadValidationResult(!connected, tooSteepFillIds.ToArray(), reports, components);
        }

        /// <summary>Angle between the plane of <paramref name="fill"/> and the horizontal, degrees, 0 to 90.</summary>
        /// <exception cref="ArgumentException">The fill references a missing node, has a non-finite node position, or has no area.</exception>
        public static double SlopeDegrees(Fill fill, IReadOnlyList<Node> nodes)
        {
            Vec3[] corners = Corners(RequireFill(fill), IndexNodes(nodes));
            return RequireFinite(fill, SlopeDegrees(UnitNormal(fill, corners)), "slope");
        }

        /// <summary>
        /// Road width of <paramref name="fill"/> in room units: its shortest edge measured in the fill plane.
        /// The spec says only that the width comes from the fill itself (section 2.4).
        /// </summary>
        /// <exception cref="ArgumentException">The fill references a missing node, has a non-finite node position, or has no area.</exception>
        public static double Width(Fill fill, IReadOnlyList<Node> nodes)
        {
            Vec3[] corners = Corners(RequireFill(fill), IndexNodes(nodes));
            return RequireFinite(fill, ShortestInPlaneEdge(corners, UnitNormal(fill, corners)), "width");
        }

        private static Fill RequireFill(Fill fill)
        {
            if (fill == null)
            {
                throw new ArgumentNullException(nameof(fill));
            }

            return fill;
        }

        private static Dictionary<int, Node> IndexNodes(IReadOnlyList<Node> nodes)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }

            var nodesById = new Dictionary<int, Node>(nodes.Count);
            foreach (Node node in nodes)
            {
                if (node == null)
                {
                    throw new ArgumentException("Nodes must not contain null.", nameof(nodes));
                }

                if (nodesById.ContainsKey(node.Id))
                {
                    throw new ArgumentException("Node id " + node.Id + " appears more than once.", nameof(nodes));
                }

                nodesById.Add(node.Id, node);
            }

            return nodesById;
        }

        private static Vec3[] Corners(Fill fill, Dictionary<int, Node> nodesById)
        {
            int[] nodeIds = fill.NodeIds ?? Array.Empty<int>();
            var corners = new Vec3[nodeIds.Length];
            for (int i = 0; i < nodeIds.Length; i++)
            {
                if (!nodesById.TryGetValue(nodeIds[i], out Node node))
                {
                    throw new ArgumentException("Fill " + fill.Id + " references missing node " + nodeIds[i] + ".");
                }

                Vec3 p = node.Position;
                if (double.IsInfinity(p.X) || double.IsInfinity(p.Y) || double.IsInfinity(p.Z))
                {
                    throw new ArgumentException("Fill " + fill.Id + " has node " + node.Id + " at a non-finite position.");
                }

                corners[i] = p;
            }

            return corners;
        }

        /// <summary>Unit normal of the loop's area vector, which also handles concave loops.</summary>
        private static Vec3 UnitNormal(Fill fill, Vec3[] corners)
        {
            Vec3 areaVector = Vec3.Zero;
            for (int i = 1; i + 1 < corners.Length; i++)
            {
                areaVector += Vec3.Cross(corners[i] - corners[0], corners[i + 1] - corners[0]);
            }

            double length = areaVector.Length;
            if (length == 0.0 || double.IsInfinity(length))
            {
                throw new ArgumentException("Fill " + fill.Id + " has no area, so it has no plane.");
            }

            return areaVector / length;
        }

        private static double SlopeDegrees(Vec3 unitNormal)
        {
            double horizontal = Math.Sqrt(unitNormal.X * unitNormal.X + unitNormal.Z * unitNormal.Z);
            return Math.Atan2(horizontal, Math.Abs(unitNormal.Y)) * DegreesPerRadian;
        }

        private static double ShortestInPlaneEdge(Vec3[] corners, Vec3 unitNormal)
        {
            double shortest = double.PositiveInfinity;
            for (int i = 0; i < corners.Length; i++)
            {
                Vec3 edge = corners[(i + 1) % corners.Length] - corners[i];
                double alongNormal = Vec3.Dot(edge, unitNormal);
                double inPlane = Math.Sqrt(Math.Max(0.0, edge.LengthSquared - alongNormal * alongNormal));
                shortest = Math.Min(shortest, inPlane);
            }

            return shortest;
        }

        private static double RequireFinite(Fill fill, double value, string quantity)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentException("Fill " + fill.Id + " produced a non-finite " + quantity + ".");
            }

            return value;
        }

        private static RoadComponent[] Components(List<Fill> roadFills, Dictionary<int, Node> nodesById, int surfaceIdA, int surfaceIdB)
        {
            var nodeIdSets = new HashSet<int>[roadFills.Count];
            for (int i = 0; i < roadFills.Count; i++)
            {
                nodeIdSets[i] = new HashSet<int>(roadFills[i].NodeIds ?? Array.Empty<int>());
            }

            var assigned = new bool[roadFills.Count];
            var components = new List<RoadComponent>();
            for (int start = 0; start < roadFills.Count; start++)
            {
                if (assigned[start])
                {
                    continue;
                }

                assigned[start] = true;
                var members = new List<int> { start };
                for (int k = 0; k < members.Count; k++)
                {
                    for (int other = 0; other < roadFills.Count; other++)
                    {
                        if (!assigned[other] && AreAdjacent(nodeIdSets[members[k]], nodeIdSets[other]))
                        {
                            assigned[other] = true;
                            members.Add(other);
                        }
                    }
                }

                members.Sort();
                var fillIds = new int[members.Count];
                bool touchesA = false;
                bool touchesB = false;
                for (int k = 0; k < members.Count; k++)
                {
                    fillIds[k] = roadFills[members[k]].Id;
                    foreach (int nodeId in nodeIdSets[members[k]])
                    {
                        int? surfaceId = nodesById[nodeId].SurfaceId;
                        touchesA |= surfaceId == surfaceIdA;
                        touchesB |= surfaceId == surfaceIdB;
                    }
                }

                components.Add(new RoadComponent(fillIds, touchesA, touchesB));
            }

            return components.ToArray();
        }

        private static bool AreAdjacent(HashSet<int> a, HashSet<int> b)
        {
            int shared = 0;
            foreach (int nodeId in a)
            {
                if (b.Contains(nodeId) && ++shared >= SharedNodeIdsForAdjacency)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Outcome of <see cref="RoadValidation.Validate(IReadOnlyList{Node}, IReadOnlyList{Fill}, int, int, double)"/>.</summary>
    public sealed class RoadValidationResult
    {
        internal RoadValidationResult(bool hasGap, int[] tooSteepFillIds, RoadFillReport[] fills, RoadComponent[] components)
        {
            HasGap = hasGap;
            TooSteepFillIds = tooSteepFillIds;
            Fills = fills;
            Components = components;
        }

        /// <summary>True when the chain connects both marked surfaces and no road fill is too steep.</summary>
        public bool IsValid => !HasGap && TooSteepFillIds.Length == 0;

        /// <summary>True when no connected group of road fills contains a node of each marked surface.</summary>
        public bool HasGap { get; }

        /// <summary>Ids of road fills steeper than the maximum slope, in input order.</summary>
        public int[] TooSteepFillIds { get; }

        /// <summary>Slope and width of every road fill, in input order.</summary>
        public RoadFillReport[] Fills { get; }

        /// <summary>Connected groups of road fills, ordered by their first fill in input order. Use them to locate a gap.</summary>
        public RoadComponent[] Components { get; }
    }

    /// <summary>Slope and width of one road fill.</summary>
    public sealed class RoadFillReport
    {
        internal RoadFillReport(int fillId, double slopeDegrees, double width, bool tooSteep)
        {
            FillId = fillId;
            SlopeDegrees = slopeDegrees;
            Width = width;
            TooSteep = tooSteep;
        }

        public int FillId { get; }

        /// <summary>Angle between the fill plane and the horizontal, degrees, 0 to 90.</summary>
        public double SlopeDegrees { get; }

        /// <summary>Shortest edge measured in the fill plane, room units.</summary>
        public double Width { get; }

        public bool TooSteep { get; }
    }

    /// <summary>A connected group of road fills, where neighbors share two node ids.</summary>
    public sealed class RoadComponent
    {
        internal RoadComponent(int[] fillIds, bool touchesSurfaceA, bool touchesSurfaceB)
        {
            FillIds = fillIds;
            TouchesSurfaceA = touchesSurfaceA;
            TouchesSurfaceB = touchesSurfaceB;
        }

        /// <summary>Fill ids in input order.</summary>
        public int[] FillIds { get; }

        /// <summary>True when a fill in the group has a node whose <see cref="Node.SurfaceId"/> is surface A.</summary>
        public bool TouchesSurfaceA { get; }

        /// <summary>True when a fill in the group has a node whose <see cref="Node.SurfaceId"/> is surface B.</summary>
        public bool TouchesSurfaceB { get; }
    }
}
