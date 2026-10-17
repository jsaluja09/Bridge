using System;
using System.Collections.Generic;
using System.Linq;

namespace BridgeBuilder.Core
{
    /// <summary>A vertex of a drawn line: an existing node, a new free node, or a new support on a pad or the floor.</summary>
    public readonly struct DrawVertex
    {
        public readonly int ExistingId;          // >= 0 for an existing node
        public readonly Vec3 Position;
        public readonly bool NewSupport;         // a new fixed node (pad top or ground dot)
        public readonly int? SurfaceId;          // which pad, for a new support on a pad
        public readonly bool Ground;

        public DrawVertex(int existingId, Vec3 position, bool newSupport = false, int? surfaceId = null, bool ground = false)
        {
            ExistingId = existingId;
            Position = position;
            NewSupport = newSupport;
            SurfaceId = surfaceId;
            Ground = ground;
        }

        public bool IsExisting => ExistingId >= 0;
    }

    /// <summary>
    /// Free drawing (spec 11.1): a held-trigger stroke anywhere in space becomes a line. The stroke is smoothed,
    /// straightened when the recognizer reads it as a line, fused with every existing dot it starts at, ends
    /// at or passes near, and divided into equal ~10 cm segments. Ends touching a pad top or a floor dot become
    /// new supports. Returns one or more vertex chains (a chain breaks where it would retrace an existing member).
    /// </summary>
    public static class FreeDraw
    {
        public sealed class Context
        {
            public IReadOnlyList<Node> Nodes;
            public Func<int, int, bool> Joined;            // are two nodes already joined by a member?
            public IReadOnlyList<Pad> Pads = Array.Empty<Pad>();
            public IReadOnlyList<Vec3> GroundDots = Array.Empty<Vec3>();
            public double SnapRadius = Constants.Draw.SnapRadiusMeters;
            public double Spacing = Constants.Snapping.TargetSpacingMeters;
        }

        public static List<List<DrawVertex>> Build(IReadOnlyList<Vec3> raw, Context ctx, out StrokeKind kind)
        {
            kind = StrokeKind.Rejected;
            var chains = new List<List<DrawVertex>>();
            if (raw == null || raw.Count < 2)
                return chains;

            var path = Smooth(Geometry.Resample(raw, 0.01), Constants.Draw.SmoothingWindow);
            if (Geometry.PathLength(path) < Constants.Draw.MinLineMeters)
                return chains;

            var start = Snap(path[0], ctx, -1);
            var end = Snap(path[path.Count - 1], ctx, start.ExistingId);
            path[0] = start.Position;
            path[path.Count - 1] = end.Position;

            var recognition = new StrokeRecognizer().Recognize(path, new[] { path[0] });
            kind = recognition.Kind;
            if (kind == StrokeKind.Line)
                path = new List<Vec3> { start.Position, end.Position };
            else if (kind == StrokeKind.Polyline)
                path = recognition.Corners.ToList();

            // Existing dots the stroke passes near become joints along it (sorted by distance along the path).
            var stops = new List<(double s, DrawVertex v)> { (0, start) };
            double total = Geometry.PathLength(path);
            foreach (var n in ctx.Nodes)
            {
                if (n.Id == start.ExistingId || n.Id == end.ExistingId)
                    continue;
                if (Closest(path, n.Position, out double s) <= ctx.SnapRadius && s > ctx.SnapRadius && s < total - ctx.SnapRadius)
                    stops.Add((s, new DrawVertex(n.Id, n.Position)));
            }
            stops.Add((total, end));
            stops.Sort((x, y) => x.s.CompareTo(y.s));
            for (int i = stops.Count - 2; i > 0; i--)
            {
                if (stops[i + 1].s - stops[i].s < ctx.Spacing * 0.5 && i + 1 < stops.Count - 1)
                    stops.RemoveAt(i + 1);
            }

            // Equal segments between consecutive stops, following the (smoothed) path.
            var chain = new List<DrawVertex> { stops[0].v };
            for (int k = 0; k + 1 < stops.Count; k++)
            {
                var a = stops[k];
                var b = stops[k + 1];
                var piece = SubPath(path, a.s, b.s);
                piece[0] = a.v.Position;
                piece[piece.Count - 1] = b.v.Position;
                double len = Geometry.PathLength(piece);
                if (len < 1e-6)
                    continue;
                int n = EqualSegments.Count(len, ctx.Spacing);
                for (int i = 1; i < n; i++)
                    chain.Add(new DrawVertex(-1, PointAt(piece, len * i / n)));
                chain.Add(b.v);
            }

            // Break the chain wherever it would duplicate an existing member.
            var current = new List<DrawVertex> { chain[0] };
            for (int i = 1; i < chain.Count; i++)
            {
                var prev = chain[i - 1];
                var v = chain[i];
                bool duplicate = prev.IsExisting && v.IsExisting && (prev.ExistingId == v.ExistingId || ctx.Joined(prev.ExistingId, v.ExistingId));
                bool samePoint = Vec3.Distance(prev.Position, v.Position) < 1e-6;
                if (duplicate || samePoint)
                {
                    if (current.Count >= 2)
                        chains.Add(current);
                    current = new List<DrawVertex> { v };
                    continue;
                }
                current.Add(v);
            }
            if (current.Count >= 2)
                chains.Add(current);
            return chains;
        }

        /// <summary>The nearest dot within the snap radius; else a pad top or floor dot as a new support; else free.</summary>
        static DrawVertex Snap(Vec3 p, Context ctx, int exclude)
        {
            int best = -1;
            double bestDist = ctx.SnapRadius;
            foreach (var n in ctx.Nodes)
            {
                double d = Vec3.Distance(n.Position, p);
                if (n.Id != exclude && d <= bestDist)
                {
                    best = n.Id;
                    bestDist = d;
                }
            }
            if (best >= 0)
                return new DrawVertex(best, ctx.Nodes.First(n => n.Id == best).Position);

            foreach (var pad in ctx.Pads)
            {
                if (pad.TrySupportPoint(p, ctx.SnapRadius, out Vec3 support))
                    return new DrawVertex(-1, support, true, pad.Id);
            }
            foreach (var g in ctx.GroundDots)
            {
                if (Vec3.Distance(g, p) <= ctx.SnapRadius)
                    return new DrawVertex(-1, g, true, null, true);
            }
            return new DrawVertex(-1, p);
        }

        static List<Vec3> Smooth(List<Vec3> pts, int window)
        {
            if (pts.Count < 3 || window < 1)
                return pts;
            var result = new List<Vec3>(pts.Count) { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
            {
                int lo = Math.Max(0, i - window), hi = Math.Min(pts.Count - 1, i + window);
                double x = 0, y = 0, z = 0;
                for (int j = lo; j <= hi; j++)
                {
                    x += pts[j].X;
                    y += pts[j].Y;
                    z += pts[j].Z;
                }
                int c = hi - lo + 1;
                result.Add(new Vec3(x / c, y / c, z / c));
            }
            result.Add(pts[pts.Count - 1]);
            return result;
        }

        /// <summary>Distance from p to the path, and the arc length s of the closest point.</summary>
        static double Closest(List<Vec3> path, Vec3 p, out double s)
        {
            double best = double.MaxValue, run = 0;
            s = 0;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                Vec3 q = Geometry.ClosestOnSegment(p, path[i], path[i + 1], out double t);
                double d = Vec3.Distance(p, q);
                double seg = Vec3.Distance(path[i], path[i + 1]);
                if (d < best)
                {
                    best = d;
                    s = run + t * seg;
                }
                run += seg;
            }
            return best;
        }

        static Vec3 PointAt(List<Vec3> path, double s)
        {
            double run = 0;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                double seg = Vec3.Distance(path[i], path[i + 1]);
                if (run + seg >= s || i + 2 == path.Count)
                    return seg > 0 ? Vec3.Lerp(path[i], path[i + 1], Math.Clamp((s - run) / seg, 0, 1)) : path[i];
                run += seg;
            }
            return path[path.Count - 1];
        }

        static List<Vec3> SubPath(List<Vec3> path, double from, double to)
        {
            var result = new List<Vec3> { PointAt(path, from) };
            double run = 0;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                run += Vec3.Distance(path[i], path[i + 1]);
                if (run > from && run < to)
                    result.Add(path[i + 1]);
            }
            result.Add(PointAt(path, to));
            return result;
        }
    }
}
