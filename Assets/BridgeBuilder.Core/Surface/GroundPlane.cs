using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Ground anchors (spec 2.5): a grid at the real floor height, limited to the footprint under the span
    /// (the XZ bounding rectangle of the two edges, origin at its minimum corner). Every node is a fixed
    /// ground anchor. A challenge's override replaces the floor height absolutely.
    /// </summary>
    public static class GroundPlane
    {
        public static List<Node> Grid(Vec3 a0, Vec3 a1, Vec3 b0, Vec3 b1, double floorHeight, double? heightOverride = null) =>
            Grid(a0, a1, b0, b1, floorHeight, heightOverride, Constants.Ground.GridSpacingMeters);

        public static List<Node> Grid(Vec3 a0, Vec3 a1, Vec3 b0, Vec3 b1, double floorHeight, double? heightOverride, double spacing)
        {
            if (!(spacing > 0))
                throw new ArgumentOutOfRangeException(nameof(spacing));
            double y = heightOverride ?? floorHeight;
            double minX = Math.Min(Math.Min(a0.X, a1.X), Math.Min(b0.X, b1.X));
            double maxX = Math.Max(Math.Max(a0.X, a1.X), Math.Max(b0.X, b1.X));
            double minZ = Math.Min(Math.Min(a0.Z, a1.Z), Math.Min(b0.Z, b1.Z));
            double maxZ = Math.Max(Math.Max(a0.Z, a1.Z), Math.Max(b0.Z, b1.Z));
            const double slack = 1e-9;

            var nodes = new List<Node>();
            for (int i = 0; minX + i * spacing <= maxX + slack; i++)
            {
                for (int j = 0; minZ + j * spacing <= maxZ + slack; j++)
                    nodes.Add(new Node { Id = nodes.Count, Position = new Vec3(minX + i * spacing, y, minZ + j * spacing), Fixed = true, GroundAnchor = true });
            }
            return nodes;
        }
    }
}
