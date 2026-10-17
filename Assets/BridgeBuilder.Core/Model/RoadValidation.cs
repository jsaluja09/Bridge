using System;
using System.Collections.Generic;
using System.Linq;

namespace BridgeBuilder.Core
{
    public sealed class RoadReport
    {
        public bool Ok { get; set; }
        public bool Gap { get; set; }                                   // no road chain joins the two surfaces
        public List<int> TooSteepFillIds { get; } = new List<int>();
        public Dictionary<int, double> Width { get; } = new Dictionary<int, double>();   // per road fill, shortest in-plane edge

        public string Message =>
            Ok ? "Road: ready"
            : TooSteepFillIds.Count > 0 ? "Part of the road is too steep for the car"
            : "The road doesn't reach from one end to the other";
    }

    /// <summary>
    /// Road-chain validation (spec 2.4): road fills that share two nodes are adjacent; one connected group must
    /// contain a node of each surface; each fill's slope (fill plane vs horizontal) must be within the limit.
    /// </summary>
    public static class RoadValidation
    {
        public static RoadReport Validate(IReadOnlyList<Fill> fills, Func<int, Node> node, int surfaceA, int surfaceB) =>
            Validate(fills, node, surfaceA, surfaceB, Constants.Road.MaxSlopeDegrees);

        public static RoadReport Validate(IReadOnlyList<Fill> fills, Func<int, Node> node, int surfaceA, int surfaceB, double maxSlopeDegrees)
        {
            var report = new RoadReport();
            var roads = fills.Where(f => f.Road && f.Valid).ToList();

            foreach (var f in roads)
            {
                var pts = f.Loop.Select(id => node(id).Position).ToList();
                if (SlopeDegrees(pts) > maxSlopeDegrees + 1e-9)
                    report.TooSteepFillIds.Add(f.Id);
                report.Width[f.Id] = ShortestEdge(pts);
            }

            // Union road fills sharing two node ids; look for a group touching both surfaces.
            var group = new Dictionary<int, int>();
            int Find(int x) => group[x] == x ? x : group[x] = Find(group[x]);
            foreach (var f in roads)
                group[f.Id] = f.Id;
            for (int i = 0; i < roads.Count; i++)
            {
                for (int j = i + 1; j < roads.Count; j++)
                {
                    if (roads[i].Loop.Intersect(roads[j].Loop).Count() >= 2)
                        group[Find(roads[i].Id)] = Find(roads[j].Id);
                }
            }
            var touchesA = new HashSet<int>();
            var touchesB = new HashSet<int>();
            foreach (var f in roads)
            {
                foreach (int id in f.Loop)
                {
                    var n = node(id);
                    if (n.SurfaceId == surfaceA)
                        touchesA.Add(Find(f.Id));
                    if (n.SurfaceId == surfaceB)
                        touchesB.Add(Find(f.Id));
                }
            }
            report.Gap = !touchesA.Overlaps(touchesB);
            report.Ok = !report.Gap && report.TooSteepFillIds.Count == 0;
            return report;
        }

        /// <summary>Angle between the fill's plane and horizontal, degrees.</summary>
        public static double SlopeDegrees(IReadOnlyList<Vec3> loop)
        {
            Vec3 n = FillPlanarity.Normal(loop);
            return Math.Acos(Math.Clamp(Math.Abs(n.Y), 0, 1)) * 180 / Math.PI;
        }

        static double ShortestEdge(IReadOnlyList<Vec3> loop)
        {
            double shortest = double.MaxValue;
            for (int i = 0; i < loop.Count; i++)
                shortest = Math.Min(shortest, Vec3.Distance(loop[i], loop[(i + 1) % loop.Count]));
            return shortest;
        }
    }
}
