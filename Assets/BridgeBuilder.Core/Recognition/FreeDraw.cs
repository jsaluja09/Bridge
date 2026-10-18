using System;
using System.Collections.Generic;
using System.Linq;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// A vertex of a drawn line: an existing node, a new node on an existing member (split there), a new free
    /// node, or a new support on a pad or the floor.
    /// </summary>
    public readonly struct DrawVertex
    {
        public readonly int ExistingId;          // >= 0 for an existing node
        public readonly Vec3 Position;
        public readonly bool NewSupport;         // a new fixed node (pad top or ground dot)
        public readonly int? SurfaceId;          // which pad, for a new support on a pad
        public readonly bool Ground;
        public readonly int MemberId;            // >= 0 for a new node that splits this existing member

        public DrawVertex(int existingId, Vec3 position, bool newSupport = false, int? surfaceId = null, bool ground = false, int memberId = -1)
        {
            ExistingId = existingId;
            Position = position;
            NewSupport = newSupport;
            SurfaceId = surfaceId;
            Ground = ground;
            MemberId = memberId;
        }

        public bool IsExisting => ExistingId >= 0;
        public bool IsOnMember => !IsExisting && MemberId >= 0;

        /// <summary>True if the vertex joins something: a dot, a line or a support.</summary>
        public bool Joins => IsExisting || IsOnMember || NewSupport;
    }

    /// <summary>
    /// Free drawing (spec 11.1, 11.5): a held-trigger stroke anywhere in space becomes a line. The stroke is
    /// smoothed and given a clean shape: straight when it barely bows, a circular arc through its ends when it
    /// follows a circle, straight sides between sharp corners, or else the smoothed curve; squiggles are rejected.
    /// The shape is fused with every existing dot or line it starts at, ends at or passes near (a line is split
    /// where it is joined between its dots), and divided into equal ~10 cm segments. Ends touching a pad top or a
    /// floor dot become new supports. Returns one or more vertex chains (a chain breaks where it would retrace an
    /// existing member).
    /// </summary>
    public static class FreeDraw
    {
        public sealed class Context
        {
            public IReadOnlyList<Node> Nodes;
            public IReadOnlyList<Member> Members = Array.Empty<Member>();
            public Func<int, int, bool> Joined;            // are two nodes already joined by a member?
            public IReadOnlyList<Pad> Pads = Array.Empty<Pad>();
            public IReadOnlyList<Vec3> GroundDots = Array.Empty<Vec3>();
            public double SnapRadius = Constants.Draw.SnapRadiusMeters;
            public double Spacing = Constants.Snapping.TargetSpacingMeters;
        }

        /// <summary>The existing bridge as snapping sees it: node positions and the drawn members.</summary>
        sealed class Scene
        {
            public readonly Context Ctx;
            public readonly Dictionary<int, Vec3> Position = new Dictionary<int, Vec3>();
            public readonly List<Member> Members = new List<Member>();
            public readonly Dictionary<int, Member> ById = new Dictionary<int, Member>();

            public Scene(Context ctx)
            {
                Ctx = ctx;
                foreach (var n in ctx.Nodes)
                    Position[n.Id] = n.Position;
                foreach (var m in ctx.Members ?? Array.Empty<Member>())
                {
                    if (m.HiddenBrace || m.LineId < 0 || !Position.ContainsKey(m.NodeA) || !Position.ContainsKey(m.NodeB))
                        continue;
                    Members.Add(m);
                    ById[m.Id] = m;
                }
            }

            public bool Joined(int a, int b) => Ctx.Joined != null
                ? Ctx.Joined(a, b)
                : Members.Any(m => (m.NodeA == a && m.NodeB == b) || (m.NodeA == b && m.NodeB == a));

            /// <summary>True if member m ends at vertex v, or v lies on m.</summary>
            public bool Touches(Member m, DrawVertex v) =>
                v.IsExisting ? m.NodeA == v.ExistingId || m.NodeB == v.ExistingId : v.IsOnMember && m.Id == v.MemberId;

            /// <summary>The lines vertex v sits on.</summary>
            public HashSet<int> LinesAt(DrawVertex v) => new HashSet<int>(Members.Where(m => Touches(m, v)).Select(m => m.LineId));
        }

        /// <summary>An end of the stroke that joins an existing dot or line, with the lines it sits on.</summary>
        readonly struct Anchor
        {
            public readonly DrawVertex Vertex;
            public readonly HashSet<int> Lines;
            public Anchor(DrawVertex vertex, HashSet<int> lines)
            {
                Vertex = vertex;
                Lines = lines;
            }
        }

        const double SampleMeters = 0.01;        // resampling step before smoothing
        const double ArcSampleMeters = 0.002;    // spacing of the dense points that trace a clean arc

        public static List<List<DrawVertex>> Build(IReadOnlyList<Vec3> raw, Context ctx, out StrokeKind kind) =>
            Build(raw, ctx, out kind, out _);

        /// <summary>
        /// As above. When no chain comes back, <paramref name="reason"/> says why: Degenerate for a stroke too short
        /// to place, Unrecognized for a squiggle (kind is then Rejected).
        /// </summary>
        public static List<List<DrawVertex>> Build(IReadOnlyList<Vec3> raw, Context ctx, out StrokeKind kind, out RejectionReason reason)
        {
            kind = StrokeKind.Rejected;
            reason = RejectionReason.Degenerate;
            var chains = new List<List<DrawVertex>>();
            if (raw == null || raw.Count < 2)
                return chains;

            var path = Smooth(Geometry.Resample(raw, SampleMeters), Constants.Draw.SmoothingWindow);
            if (Geometry.PathLength(path) < Constants.Draw.MinLineMeters)
                return chains;

            var scene = new Scene(ctx);
            var start = Snap(path[0], scene, null);
            var end = Snap(path[path.Count - 1], scene, start);
            path = Shape(path, start.Position, end.Position, out kind);
            if (path == null)
            {
                reason = RejectionReason.Unrecognized;
                return chains;
            }
            reason = RejectionReason.None;

            // Existing dots and lines the stroke passes near become joints along it (sorted by distance along the
            // path), except near an end the stroke shares with a line it is only leaving (spec 11.5).
            var anchors = new List<Anchor>();
            foreach (var v in new[] { start, end })
            {
                if (v.IsExisting || v.IsOnMember)
                    anchors.Add(new Anchor(v, scene.LinesAt(v)));
            }
            var stops = new List<(double s, DrawVertex v)> { (0, start) };
            double total = Geometry.PathLength(path);
            foreach (var n in ctx.Nodes)
            {
                if (n.Id == start.ExistingId || n.Id == end.ExistingId)
                    continue;
                if (Closest(path, n.Position, out double s, out Vec3 q) > ctx.SnapRadius || s <= ctx.SnapRadius || s >= total - ctx.SnapRadius)
                    continue;
                var dot = new DrawVertex(n.Id, n.Position);
                if (!Leaves(anchors, n.Position, q, scene.LinesAt(dot)))
                    stops.Add((s, dot));
            }
            stops.AddRange(Crossings(path, total, scene, anchors));
            stops.Add((total, end));
            stops.Sort((x, y) => x.s.CompareTo(y.s));
            for (int i = stops.Count - 2; i > 0; i--)
            {
                if (stops[i + 1].s - stops[i].s >= ctx.Spacing * 0.5)
                    continue;
                bool dotAfterSplit = stops[i].v.IsOnMember && stops[i + 1].v.IsExisting;
                if (i + 1 < stops.Count - 1 && !dotAfterSplit)
                    stops.RemoveAt(i + 1);
                else if (stops[i].v.IsOnMember)
                    stops.RemoveAt(i);
            }

            // The corners of a zigzag or triangle are joints too, unless a joint already sits that close.
            if (kind == StrokeKind.Polyline)
            {
                double at = 0;
                for (int i = 1; i + 1 < path.Count; i++)
                {
                    at += Vec3.Distance(path[i - 1], path[i]);
                    double s = at;
                    if (stops.All(x => Math.Abs(x.s - s) >= ctx.Spacing * 0.5))
                        stops.Add((s, new DrawVertex(-1, path[i])));
                }
                stops.Sort((x, y) => x.s.CompareTo(y.s));
            }

            // Equal segments between consecutive stops, following the shape.
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
                bool samePoint = Vec3.Distance(prev.Position, v.Position) < 1e-6;
                if (Retraces(scene, prev, v) || samePoint)
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

        /// <summary>
        /// What a stroke end at p would join (spec 11.1, 11.5); see Snap. Pass the stroke's other end as
        /// <paramref name="other"/> so the end never joins that end's own dot or lines.
        /// </summary>
        public static DrawVertex Target(Vec3 p, Context ctx, DrawVertex? other = null) => Snap(p, new Scene(ctx), other);

        /// <summary>
        /// The nearest dot within the snap radius; else the nearest line within it (its dot, if the point on the
        /// line is that close to one, else a split there); else a pad top or floor dot as a new support; else free.
        /// </summary>
        static DrawVertex Snap(Vec3 p, Scene scene, DrawVertex? other)
        {
            var ctx = scene.Ctx;
            int exclude = other.HasValue && other.Value.IsExisting ? other.Value.ExistingId : -1;
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
                return new DrawVertex(best, scene.Position[best]);

            Member line = null;
            Vec3 onLine = default;
            bestDist = ctx.SnapRadius;
            foreach (var m in scene.Members)
            {
                if (other.HasValue && scene.Touches(m, other.Value))
                    continue;
                Vec3 q = Geometry.ClosestOnSegment(p, scene.Position[m.NodeA], scene.Position[m.NodeB], out _);
                double d = Vec3.Distance(p, q);
                if (d <= bestDist)
                {
                    line = m;
                    onLine = q;
                    bestDist = d;
                }
            }
            if (line != null)
            {
                Vec3 a = scene.Position[line.NodeA], b = scene.Position[line.NodeB];
                double da = Vec3.Distance(onLine, a), db = Vec3.Distance(onLine, b);
                if (Math.Min(da, db) > ctx.SnapRadius)
                    return new DrawVertex(-1, onLine, memberId: line.Id);
                int dot = da <= db ? line.NodeA : line.NodeB;
                return new DrawVertex(dot, scene.Position[dot]);
            }

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

        /// <summary>
        /// Where the stroke passes a line between its dots: for each member, the closest point of each pass within
        /// the snap radius. Kept only if that point is farther than the snap radius from both dots (else the dot
        /// joins, with no runt pieces) and the stroke crosses the member rather than running along it.
        /// </summary>
        static IEnumerable<(double s, DrawVertex v)> Crossings(List<Vec3> path, double total, Scene scene, List<Anchor> anchors)
        {
            double r = scene.Ctx.SnapRadius;
            var probe = Geometry.Resample(path, 0.005);
            var run = new double[probe.Count];
            for (int i = 1; i < probe.Count; i++)
                run[i] = run[i - 1] + Vec3.Distance(probe[i - 1], probe[i]);

            foreach (var m in scene.Members)
            {
                if (anchors.Any(x => scene.Touches(m, x.Vertex)))
                    continue;
                Vec3 a = scene.Position[m.NodeA], b = scene.Position[m.NodeB];
                if (Vec3.Distance(a, b) <= 2 * r)
                    continue;

                int best = -1;
                double bestDist = r;
                Vec3 bestPoint = default;
                for (int i = 0; i <= probe.Count; i++)
                {
                    if (i < probe.Count && run[i] > r && run[i] < total - r)
                    {
                        Vec3 q = Geometry.ClosestOnSegment(probe[i], a, b, out _);
                        double d = Vec3.Distance(probe[i], q);
                        if (d <= r)
                        {
                            if (d <= bestDist)
                            {
                                best = i;
                                bestDist = d;
                                bestPoint = q;
                            }
                            continue;
                        }
                    }
                    if (best < 0)
                        continue;

                    Vec3 tangent = probe[Math.Min(best + 1, probe.Count - 1)] - probe[Math.Max(best - 1, 0)];
                    bool clearOfDots = Vec3.Distance(bestPoint, a) > r && Vec3.Distance(bestPoint, b) > r;
                    bool crosses = AngleDegrees(tangent, b - a, true) > Constants.Draw.RetraceAngleDegrees;
                    if (clearOfDots && crosses && !Leaves(anchors, bestPoint, probe[best], new HashSet<int> { m.LineId }))
                    {
                        Closest(path, probe[best], out double s, out _);
                        yield return (s, new DrawVertex(-1, bestPoint, memberId: m.Id));
                    }
                    best = -1;
                    bestDist = r;
                }
            }
        }

        /// <summary>
        /// True if a point on an existing line lies near the stroke only because the stroke leaves (or reaches) an
        /// end it shares with that line (spec 11.5). Seen from that end, the stroke passes the point at more than
        /// the retrace angle: it is heading its own way, not tracing over the line.
        /// </summary>
        static bool Leaves(List<Anchor> anchors, Vec3 point, Vec3 nearestOnStroke, HashSet<int> pointLines)
        {
            Vec3? from = null;
            double nearest = double.MaxValue;
            foreach (var anchor in anchors)
            {
                double d = Vec3.Distance(anchor.Vertex.Position, point);
                if (anchor.Lines.Overlaps(pointLines) && d < nearest)
                {
                    from = anchor.Vertex.Position;
                    nearest = d;
                }
            }
            if (!from.HasValue)
                return false;
            Vec3 toPoint = point - from.Value, toStroke = nearestOnStroke - from.Value;
            if (toPoint.Length < 1e-9 || toStroke.Length < 1e-9)
                return false;
            return AngleDegrees(toPoint, toStroke, false) > Constants.Draw.RetraceAngleDegrees;
        }

        /// <summary>True if a member from p to v would lie along an existing member.</summary>
        static bool Retraces(Scene scene, DrawVertex p, DrawVertex v)
        {
            if (p.IsExisting && v.IsExisting)
                return p.ExistingId == v.ExistingId || scene.Joined(p.ExistingId, v.ExistingId);
            if (p.IsOnMember && v.IsOnMember)
                return p.MemberId == v.MemberId;
            if (p.IsOnMember && v.IsExisting)
                return scene.Touches(scene.ById[p.MemberId], v);
            if (v.IsOnMember && p.IsExisting)
                return scene.Touches(scene.ById[v.MemberId], p);
            return false;
        }

        /// <summary>The angle between two directions in degrees; with <paramref name="unsigned"/>, between the lines they lie on (0 to 90).</summary>
        static double AngleDegrees(Vec3 a, Vec3 b, bool unsigned)
        {
            double la = a.Length, lb = b.Length;
            if (la < 1e-12 || lb < 1e-12)
                return 0;
            double cos = Vec3.Dot(a, b) / (la * lb);
            if (unsigned)
                cos = Math.Abs(cos);
            return Math.Acos(Math.Clamp(cos, -1, 1)) * 180 / Math.PI;
        }

        /// <summary>
        /// The shape the smoothed path is drawn as (spec 11.1), from snapped end p to snapped end q: straight sides
        /// between sharp corners where they were drawn (Polyline, the ends and corners), unless a circle fits the
        /// stroke more closely; else, with the snap shift spread along the path, its two ends when it bows less than
        /// StraightBowFraction of its span (Line); a clean circular arc through its ends, densely sampled (Arc); or
        /// the path itself for any other smooth curve (Curve). Null for a squiggle.
        /// </summary>
        static List<Vec3> Shape(List<Vec3> path, Vec3 p, Vec3 q, out StrokeKind kind)
        {
            var corners = Corners(path, out double sideFit);
            path = Warp(path, p, q);
            Vec3 a = path[0], b = path[path.Count - 1];
            double span = Vec3.Distance(a, b), bow = Bow(path, 0, path.Count - 1, a, b);
            var arc = Arc(path, span, bow, out double arcFit);
            if (corners != null && (arc == null || sideFit <= arcFit))
            {
                corners[0] = p;
                corners[corners.Count - 1] = q;
                kind = StrokeKind.Polyline;
                return corners;
            }
            if (span > 0 && bow <= Constants.Draw.StraightBowFraction * span)
            {
                kind = StrokeKind.Line;
                return new List<Vec3> { a, b };
            }
            if (arc != null)
            {
                kind = StrokeKind.Arc;
                return arc;
            }
            Bends(path, out double turned, out int reversals);
            if (reversals > Constants.Draw.MaxBendReversals || turned > Constants.Draw.MaxTurnDegrees)
            {
                kind = StrokeKind.Rejected;
                return null;
            }
            kind = StrokeKind.Curve;
            return path;
        }

        /// <summary>Moves the path's ends onto p and q, spreading the shift along its length so it keeps its shape.</summary>
        static List<Vec3> Warp(List<Vec3> path, Vec3 p, Vec3 q)
        {
            Vec3 dp = p - path[0], dq = q - path[path.Count - 1];
            double total = Geometry.PathLength(path), run = 0;
            var result = new List<Vec3>(path.Count);
            for (int i = 0; i < path.Count; i++)
            {
                if (i > 0)
                    run += Vec3.Distance(path[i - 1], path[i]);
                double t = total > 0 ? run / total : 0;
                result.Add(path[i] + dp * (1 - t) + dq * t);
            }
            result[0] = p;
            result[result.Count - 1] = q;
            return result;
        }

        /// <summary>The largest distance of path[from..to] from the segment ab.</summary>
        static double Bow(List<Vec3> path, int from, int to, Vec3 a, Vec3 b)
        {
            double max = 0;
            for (int i = from; i <= to; i++)
                max = Math.Max(max, Geometry.DistanceToSegment(path[i], a, b));
            return max;
        }

        /// <summary>
        /// The ends and corners of a zigzag, triangle or W: each turn of at least CornerAngleDegrees between straight
        /// runs just beyond the smoothing, complete within those runs (one arm farther out it has grown by less than
        /// CornerSpreadDegrees, unlike a smooth bend), when every side between corners (away from their rounding)
        /// bows less than SideBowFraction of its length. Null when the stroke has no such corner. fit is the path's
        /// largest distance from the sides, rounded corners included.
        /// </summary>
        static List<Vec3> Corners(List<Vec3> path, out double fit)
        {
            fit = 0;
            int skip = Constants.Draw.SmoothingWindow;
            int arm = Math.Max(1, (int)Math.Round(Constants.Draw.CornerArmMeters / SampleMeters));
            int reach = skip + arm, count = path.Count;
            var turn = new double[count];
            for (int i = reach; i + reach < count; i++)
            {
                double inner = AngleDegrees(path[i - skip] - path[i - reach], path[i + reach] - path[i + skip], false);
                double outer = AngleDegrees(path[i - reach] - path[Math.Max(0, i - reach - arm)], path[Math.Min(count - 1, i + reach + arm)] - path[i + reach], false);
                if (outer - inner < Constants.Draw.CornerSpreadDegrees)
                    turn[i] = inner;
            }

            var picks = new List<int> { 0 };
            for (int i = reach; i + reach < count; i++)
            {
                if (turn[i] < Constants.Draw.CornerAngleDegrees)
                    continue;
                bool peak = true;
                for (int j = i - reach; j <= i + reach && peak; j++)
                    peak = turn[j] < turn[i] || (turn[j] == turn[i] && j >= i);
                if (peak)
                    picks.Add(i);
            }
            if (picks.Count < 2)
                return null;
            picks.Add(count - 1);

            var corners = new List<Vec3> { path[0] };
            for (int c = 1; c + 1 < picks.Count; c++)
                corners.Add(Corner(path, picks[c], skip, reach));
            corners.Add(path[count - 1]);
            for (int c = 0; c + 1 < picks.Count; c++)
            {
                int from = c == 0 ? 0 : picks[c] + skip, to = c + 2 == picks.Count ? count - 1 : picks[c + 1] - skip;
                double side = Vec3.Distance(corners[c], corners[c + 1]);
                if (!(side > 0) || Bow(path, from, to, corners[c], corners[c + 1]) > Constants.Draw.SideBowFraction * side)
                    return null;
                fit = Math.Max(fit, Bow(path, picks[c], picks[c + 1], corners[c], corners[c + 1]));
            }
            return corners;
        }

        /// <summary>Where the straight runs into and out of the corner at sample i meet, undoing the smoothing's rounding.</summary>
        static Vec3 Corner(List<Vec3> path, int i, int skip, int reach)
        {
            Vec3 p = path[i - skip], d = (path[i - skip] - path[i - reach]).Normalized();
            Vec3 q = path[i + skip], e = (path[i + reach] - path[i + skip]).Normalized();
            Vec3 w = p - q;
            double c = Vec3.Dot(d, e), det = 1 - c * c;
            if (det < 1e-3)
                return path[i];
            double s = (c * Vec3.Dot(w, e) - Vec3.Dot(w, d)) / det, t = (Vec3.Dot(w, e) - c * Vec3.Dot(w, d)) / det;
            Vec3 corner = (p + d * s + q + e * t) * 0.5;
            return Vec3.Distance(corner, path[i]) <= reach * SampleMeters ? corner : path[i];
        }

        /// <summary>
        /// A clean circular arc through the path's ends, bulging the same way and about as far as the drawing, densely
        /// sampled so equal division puts every joint on the circle. Null unless the path stays within ArcFitFraction
        /// of its size (span or bow, whichever is larger) of that circle; fit is its largest distance from the circle.
        /// </summary>
        static List<Vec3> Arc(List<Vec3> path, double span, double bow, out double fit)
        {
            fit = double.MaxValue;
            if (span < Constants.Draw.MinLineMeters)
                return null;
            Vec3 a = path[0], b = path[path.Count - 1], mid = (a + b) * 0.5, x = (b - a) / span;
            Vec3 side = Vec3.Zero;
            foreach (var p in path)
                side += p - mid - x * Vec3.Dot(p - mid, x);
            if (side.Length < 1e-9)
                return null;
            Vec3 y = side.Normalized(), normal = Vec3.Cross(x, y);

            // The circle through both ends has its centre on their bisector at height k; x² + (y - k)² = (span/2)² + k²
            // is linear in k, so the algebraic least-squares fit is closed form.
            double half = span / 2, num = 0, den = 0;
            foreach (var p in path)
            {
                double px = Vec3.Dot(p - mid, x), py = Vec3.Dot(p - mid, y);
                num += py * (px * px + py * py - half * half);
                den += 2 * py * py;
            }
            if (den < 1e-12)
                return null;
            double k = num / den, r = Math.Sqrt(half * half + k * k);
            Vec3 center = mid + y * k;

            fit = 0;
            foreach (var p in path)
            {
                Vec3 d = p - center;
                double off = Vec3.Dot(d, normal), radial = (d - normal * off).Length - r;
                fit = Math.Max(fit, Math.Sqrt(radial * radial + off * off));
            }
            if (fit > Constants.Draw.ArcFitFraction * Math.Max(span, bow))
                return null;

            double halfSweep = Math.Acos(Math.Clamp(-k / r, -1, 1));
            int n = Math.Max(2, (int)Math.Ceiling(2 * halfSweep * r / ArcSampleMeters));
            var arc = new List<Vec3>(n + 1) { a };
            for (int i = 1; i < n; i++)
            {
                double t = halfSweep * (2.0 * i / n - 1);
                arc.Add(center + x * (r * Math.Sin(t)) + y * (r * Math.Cos(t)));
            }
            arc.Add(b);
            return arc;
        }

        /// <summary>
        /// How far a curve bends in all, in degrees, and how often it changes bend direction. The running turn is
        /// followed with hysteresis, so wobble under BendLobeMinDegrees neither counts as a bend nor adds to the total.
        /// </summary>
        static void Bends(List<Vec3> path, out double turned, out int reversals)
        {
            Geometry.FitPlane(path, out _, out Vec3 normal, out _);
            double min = Constants.Draw.BendLobeMinDegrees, theta = 0, high = 0, low = 0, from = 0;
            int dir = 0;
            turned = 0;
            reversals = 0;
            for (int i = 1; i + 1 < path.Count; i++)
            {
                Vec3 u = path[i] - path[i - 1], v = path[i + 1] - path[i];
                theta += Math.Atan2(Vec3.Dot(Vec3.Cross(u, v), normal), Vec3.Dot(u, v)) * 180 / Math.PI;
                high = Math.Max(high, theta);
                low = Math.Min(low, theta);
                if (dir <= 0 && theta - low >= min)
                {
                    if (dir < 0)
                    {
                        reversals++;
                        turned += from - low;
                    }
                    from = low;
                    high = theta;
                    dir = 1;
                }
                else if (dir >= 0 && high - theta >= min)
                {
                    if (dir > 0)
                    {
                        reversals++;
                        turned += high - from;
                    }
                    from = high;
                    low = theta;
                    dir = -1;
                }
            }
            turned += dir > 0 ? high - from : dir < 0 ? from - low : 0;
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

        /// <summary>Distance from p to the path, the arc length s of the closest point, and that point.</summary>
        static double Closest(List<Vec3> path, Vec3 p, out double s, out Vec3 closest)
        {
            double best = double.MaxValue, run = 0;
            s = 0;
            closest = path[0];
            for (int i = 0; i + 1 < path.Count; i++)
            {
                Vec3 q = Geometry.ClosestOnSegment(p, path[i], path[i + 1], out double t);
                double d = Vec3.Distance(p, q);
                double seg = Vec3.Distance(path[i], path[i + 1]);
                if (d < best)
                {
                    best = d;
                    s = run + t * seg;
                    closest = q;
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
