using System;
using System.Collections.Generic;
using System.Linq;

namespace BridgeBuilder.Core
{
    /// <summary>A resolved shape after the parallel assist: snapped to a partner line, or left as drawn.</summary>
    public sealed class ParallelResult
    {
        public ResolvedShape Shape;
        public int PartnerLineId = -1;                // -1: no partner
        public bool Snapped => PartnerLineId >= 0;
        /// <summary>
        /// From the partner to the snapped shape: perpendicular to the partner (lines), along its axis and radius
        /// (arcs), or along the normal of its plane (polylines).
        /// </summary>
        public Vec3 Offset;
    }

    /// <summary>
    /// Parallel rail assist (W4-02, spec 11.6). A newly resolved line that runs within ParallelAngleTolerance of an
    /// existing line, at a drivable road spacing (Constants.Road), becomes an exact copy of that line shifted sideways:
    /// same direction, same span, constant offset, so the two always make a deck. An arc snaps to an offset arc of a
    /// partner arc: same center axis, radius offset (or the same radius for side-by-side arches). A polyline (zigzag,
    /// triangle, W) drawn beside the same polyline in a parallel plane snaps to its corners shifted along that plane's
    /// normal, so the two sides of a bridge match.
    /// </summary>
    public static class ParallelAssist
    {
        public static ParallelResult Snap(ResolvedShape shape, BridgeModel model)
        {
            var none = new ParallelResult { Shape = shape };
            if (shape == null || !shape.Valid || model == null)
                return none;

            ParallelResult best = null;
            double bestScore = double.MaxValue;
            foreach (var line in model.Lines)
            {
                var points = model.LineNodes(line.Id).Select(id => model.GetNode(id).Position).ToList();
                if (points.Count < 2)
                    continue;
                var partner = ShapeResolver.Resolve(points);
                if (partner.Kind != shape.Kind)
                    continue;
                double score;
                var result = shape.Kind == ResolvedKind.Line ? SnapLine(shape, partner, out score)
                    : shape.Kind == ResolvedKind.Arc ? SnapArc(shape, partner, out score)
                    : SnapPolyline(shape, partner, out score);
                if (result != null && score < bestScore)
                {
                    result.PartnerLineId = line.Id;
                    best = result;
                    bestScore = score;
                }
            }
            return best ?? none;
        }

        static Vec3 Middle(ResolvedShape arc) => arc.Center + ShapeResolver.Rotate(arc.Points[0] - arc.Center, arc.Normal, arc.Sweep / 2);

        static double Angle(Vec3 a, Vec3 b) => Math.Acos(Math.Clamp(Math.Abs(Vec3.Dot(a.Normalized(), b.Normalized())), 0, 1)) * 180 / Math.PI;

        static bool Drivable(double spacing) =>
            spacing >= Constants.Road.MinDeckSpacingMeters && spacing <= Constants.Road.MaxDeckSpacingMeters;

        /// <summary>The partner line moved sideways to where the new line was drawn. Score: angle, then distance.</summary>
        static ParallelResult SnapLine(ResolvedShape drawn, ResolvedShape partner, out double score)
        {
            score = double.MaxValue;
            Vec3 a = partner.Points[0], b = partner.Points[1];
            Vec3 dir = (b - a).Normalized();
            Vec3 drawnDir = drawn.Points[1] - drawn.Points[0];
            double angle = Angle(drawnDir, dir);
            if (angle > Constants.Parallel.ParallelAngleTolerance)
                return null;

            Vec3 mid = (drawn.Points[0] + drawn.Points[1]) * 0.5;
            if (Geometry.DistanceToSegment(mid, a, b) > Constants.Parallel.ParallelSearchRadius)
                return null;
            Vec3 rel = mid - (a + b) * 0.5;
            Vec3 offset = rel - dir * Vec3.Dot(rel, dir);           // perpendicular part: constant along the rail
            Vec3 flat = new Vec3(dir.X, 0, dir.Z);
            if (flat.LengthSquared < 1e-12)
                return null;                                         // a vertical line is no rail
            Vec3 side = Vec3.Cross(Vec3.UnitY, flat.Normalized());
            if (!Drivable(Math.Abs(Vec3.Dot(offset, side))))         // spacing measured as AutoDeck does
                return null;

            if (Vec3.Dot(drawnDir, dir) < 0)
                (a, b) = (b, a);                                     // keep the direction it was drawn in
            var snapped = new ResolvedShape { Kind = ResolvedKind.Line, Points = { a + offset, b + offset }, Residual = drawn.Residual };
            score = angle + offset.Length;
            return new ParallelResult { Shape = snapped, Offset = offset };
        }

        /// <summary>The partner arc moved along its axis and grown or shrunk in radius to where the new arc was drawn.</summary>
        static ParallelResult SnapArc(ResolvedShape drawn, ResolvedShape partner, out double score)
        {
            score = double.MaxValue;
            double angle = Angle(drawn.Normal, partner.Normal);
            if (angle > Constants.Parallel.ParallelAngleTolerance)
                return null;
            Vec3 axis = partner.Normal.Normalized();
            Vec3 rel = drawn.Center - partner.Center;
            double along = Vec3.Dot(rel, axis);
            if ((rel - axis * along).Length > Constants.Parallel.ParallelArcRadiusTolerance)
                return null;                                         // not the same center axis
            double dr = drawn.Radius - partner.Radius;
            if (Math.Abs(dr) <= Constants.Parallel.ParallelArcRadiusTolerance)
                dr = 0;                                              // side-by-side arches: same radius
            if (!Drivable(Math.Sqrt(along * along + dr * dr)))
                return null;
            if (Vec3.Distance(Middle(drawn), Middle(partner)) > Constants.Parallel.ParallelSearchRadius)
                return null;

            Vec3 center = partner.Center + axis * along;
            double radius = partner.Radius + dr;
            Vec3 Move(Vec3 p)
            {
                Vec3 d = p - partner.Center;
                Vec3 radial = d - axis * Vec3.Dot(d, axis);
                return center + radial.Normalized() * radius;
            }
            Vec3 start = Move(partner.Points[0]), end = Move(partner.Points[1]);
            double sweep = partner.Sweep;
            Vec3 normal = partner.Normal;
            // Keep the direction it was drawn in: start nearest the drawn start.
            if (Vec3.Distance(end, drawn.Points[0]) < Vec3.Distance(start, drawn.Points[0]))
            {
                (start, end) = (end, start);
                sweep = -sweep;
            }
            var snapped = new ResolvedShape
            {
                Kind = ResolvedKind.Arc,
                Points = { start, end },
                Center = center,
                Normal = normal,
                Radius = radius,
                Sweep = sweep,
                Residual = drawn.Residual,
            };
            score = angle + Math.Abs(along) + Math.Abs(dr);
            return new ParallelResult { Shape = snapped, Offset = axis * along + (start - center).Normalized() * dr };
        }

        /// <summary>
        /// The partner polyline moved along its plane's normal to where the new one was drawn. Both need the same
        /// number of corners, and each corner and end of the new one must lie within ParallelCornerTolerance of the
        /// shifted partner's, read in either direction. Score: angle, then distance.
        /// </summary>
        static ParallelResult SnapPolyline(ResolvedShape drawn, ResolvedShape partner, out double score)
        {
            score = double.MaxValue;
            if (drawn.Points.Count != partner.Points.Count)
                return null;
            Geometry.FitPlane(partner.Points, out Vec3 partnerMid, out Vec3 normal, out _);
            Geometry.FitPlane(drawn.Points, out Vec3 drawnMid, out Vec3 drawnNormal, out _);
            double angle = Angle(drawnNormal, normal);
            if (angle > Constants.Parallel.ParallelAngleTolerance)
                return null;
            Vec3 rel = drawnMid - partnerMid;
            if (rel.Length > Constants.Parallel.ParallelSearchRadius)
                return null;
            double along = Vec3.Dot(rel, normal);
            if (!Drivable(Math.Abs(along)))
                return null;
            Vec3 offset = normal * along;

            List<Vec3> best = null;
            double bestWorst = Constants.Parallel.ParallelCornerTolerance;
            foreach (var order in new[] { partner.Points, Enumerable.Reverse(partner.Points).ToList() })
            {
                var copy = order.Select(p => p + offset).ToList();
                double worst = copy.Select((p, i) => Vec3.Distance(p, drawn.Points[i])).Max();
                if (worst <= bestWorst)
                {
                    best = copy;                                     // keeps the direction it was drawn in
                    bestWorst = worst;
                }
            }
            if (best == null)
                return null;                                         // a different shape, not the same one beside it

            var snapped = new ResolvedShape { Kind = ResolvedKind.Polyline, Points = best, Residual = drawn.Residual };
            score = angle + Math.Abs(along);
            return new ParallelResult { Shape = snapped, Offset = offset };
        }
    }
}
