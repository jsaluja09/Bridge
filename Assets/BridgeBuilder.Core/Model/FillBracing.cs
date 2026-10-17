using System;
using System.Collections.Generic;
using System.Linq;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Hidden fill bracing (spec 2.4): diagonal frame members between the fill's own boundary nodes, lying in
    /// the fill's plane. Pattern: Constants.Bracing.PatternId. Braces are free (no budget) and create no nodes.
    /// </summary>
    public static class FillBracing
    {
        /// <summary>Triangles over the loop (index triples into loop) and the braces they need (node-id pairs).</summary>
        public static void Plan(IReadOnlyList<int> loop, IReadOnlyList<Vec3> positions, out List<int> triangles, out List<(int a, int b)> braces)
        {
            Vec3 normal = FillPlanarity.Normal(positions);
            Geometry.PlaneBasis(normal, out Vec3 u, out Vec3 v);
            Vec3 origin = positions[0];
            var flat = positions.Select(p => new Vec2(Vec3.Dot(p - origin, u), Vec3.Dot(p - origin, v))).ToList();
            triangles = Geometry.Triangulate(flat);

            var boundary = new HashSet<(int, int)>();
            for (int i = 0; i < loop.Count; i++)
                boundary.Add(Key(loop[i], loop[(i + 1) % loop.Count]));
            var seen = new HashSet<(int, int)>();
            braces = new List<(int, int)>();
            for (int t = 0; t < triangles.Count; t += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    var key = Key(loop[triangles[t + e]], loop[triangles[t + (e + 1) % 3]]);
                    if (!boundary.Contains(key) && seen.Add(key))
                        braces.Add(key);
                }
            }
        }

        static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

        /// <summary>
        /// Adds the hidden braces for a fill already in the model. Throws if the fill is not planar (members
        /// unchanged). Skips pairs already joined by a member and pairs of two fixed nodes.
        /// </summary>
        public static List<int> Apply(BridgeModel model, int fillId, int sectionId)
        {
            var fill = model.GetFill(fillId) ?? throw new ArgumentException("Unknown fill");
            var positions = fill.Loop.Select(id => model.GetNode(id).Position).ToList();
            if (!FillPlanarity.IsPlanar(positions))
                throw new InvalidOperationException("Fill is not planar");

            Plan(fill.Loop, positions, out _, out var braces);
            var added = new List<int>();
            foreach (var (a, b) in braces)
            {
                if (model.GetNode(a).Fixed && model.GetNode(b).Fixed)
                    continue;
                if (model.Members.Any(m => (m.NodeA == a && m.NodeB == b) || (m.NodeA == b && m.NodeB == a)))
                    continue;
                var result = model.TryAddHiddenBrace(a, b, sectionId);
                if (result.Ok)
                    added.Add(result.Id);
            }
            return added;
        }
    }
}
