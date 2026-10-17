using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Hidden automatic bracing for a fill (spec 2.4), pattern <see cref="Constants.Bracing.PatternId"/>
    /// from HG-bracing: triangulate the fill with its own loop nodes, shortest valid diagonal first. A
    /// simple loop of n nodes gets n - 3 braces and a triangle gets none. A diagonal that passes another
    /// loop node is dropped, so no brace runs along a straight side. Positions are in room units.
    /// </summary>
    public static class FillBracing
    {
        /// <summary>
        /// Adds the hidden braces of fill <paramref name="fillId"/> with
        /// <see cref="BridgeModel.TryAddHiddenBrace"/>, so they cost nothing and create no nodes. A loop
        /// pair that a member already joins is not braced again.
        /// </summary>
        /// <returns>The new brace member ids, in the order they were added.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// No fill has the id, the fill is invalid, <see cref="FillPlanarity"/> rejects its loop, or two
        /// non-adjacent loop edges cross or touch. Nothing is added.
        /// </exception>
        public static int[] AddBraces(BridgeModel model, int fillId, int sectionId)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            Fill fill = FindValidFill(model, fillId);
            int[] nodeIds = fill.NodeIds;
            Vec3[] loop = LoopPositions(model, fill);
            if (!FillPlanarity.IsPlanar(loop))
            {
                throw new ArgumentException("Fill " + fillId + " is not planar, so it cannot be braced.", nameof(fillId));
            }

            Point2[] points = ProjectOntoPlane(loop, fillId);
            if (IntersectsItself(points))
            {
                throw new ArgumentException("Fill " + fillId + " intersects itself, so it cannot be braced.", nameof(fillId));
            }

            List<Candidate> kept = KeepDiagonals(points, nodeIds, JoinedPairs(model));
            var braceIds = new int[kept.Count];
            for (int k = 0; k < kept.Count; k++)
            {
                int nodeA = nodeIds[kept[k].I];
                int nodeB = nodeIds[kept[k].J];
                EditResult brace = model.TryAddHiddenBrace(nodeA, nodeB, sectionId);
                if (!brace.Succeeded)
                {
                    throw new InvalidOperationException("The model refused the brace from node " + nodeA + " to node " + nodeB + ": " + brace.Reason + ".");
                }

                braceIds[k] = brace.Id;
            }

            return braceIds;
        }

        private static Fill FindValidFill(BridgeModel model, int fillId)
        {
            foreach (Fill fill in model.Fills)
            {
                if (fill.Id != fillId)
                {
                    continue;
                }

                if (!fill.Valid)
                {
                    throw new ArgumentException("Fill " + fillId + " is invalid, so it cannot be braced.", nameof(fillId));
                }

                return fill;
            }

            throw new ArgumentException("No fill has id " + fillId + ".", nameof(fillId));
        }

        private static Vec3[] LoopPositions(BridgeModel model, Fill fill)
        {
            var positions = new Dictionary<int, Vec3>();
            foreach (Node node in model.Nodes)
            {
                positions.Add(node.Id, node.Position);
            }

            var loop = new Vec3[fill.NodeIds.Length];
            for (int k = 0; k < loop.Length; k++)
            {
                if (!positions.TryGetValue(fill.NodeIds[k], out loop[k]))
                {
                    throw new ArgumentException("Fill " + fill.Id + " references missing node " + fill.NodeIds[k] + ".", nameof(fill));
                }
            }

            return loop;
        }

        private static HashSet<(int, int)> JoinedPairs(BridgeModel model)
        {
            var joined = new HashSet<(int, int)>();
            foreach (Member member in model.Members)
            {
                joined.Add(PairKey(member.NodeA, member.NodeB));
            }

            return joined;
        }

        private static (int, int) PairKey(int nodeA, int nodeB)
        {
            return nodeA < nodeB ? (nodeA, nodeB) : (nodeB, nodeA);
        }

        /// <summary>Loop coordinates in an orthonormal basis of the plane normal to the Newell area vector.</summary>
        private static Point2[] ProjectOntoPlane(Vec3[] loop, int fillId)
        {
            double x = 0.0;
            double y = 0.0;
            double z = 0.0;
            for (int k = 0; k < loop.Length; k++)
            {
                Vec3 current = loop[k];
                Vec3 next = loop[(k + 1) % loop.Length];
                x += (current.Y - next.Y) * (current.Z + next.Z);
                y += (current.Z - next.Z) * (current.X + next.X);
                z += (current.X - next.X) * (current.Y + next.Y);
            }

            var areaVector = new Vec3(x, y, z);
            double length = areaVector.Length;
            if (!(length > 0.0) || double.IsInfinity(length))
            {
                throw new ArgumentException("Fill " + fillId + " has no area, so it has no plane.", nameof(fillId));
            }

            Vec3 normal = areaVector / length;
            double absX = Math.Abs(normal.X);
            double absY = Math.Abs(normal.Y);
            double absZ = Math.Abs(normal.Z);
            Vec3 helper = absX <= absY && absX <= absZ ? Vec3.UnitX : (absY <= absZ ? Vec3.UnitY : Vec3.UnitZ);
            Vec3 u = Vec3.Cross(helper, normal).Normalized();
            Vec3 v = Vec3.Cross(normal, u);

            var points = new Point2[loop.Length];
            for (int k = 0; k < loop.Length; k++)
            {
                Vec3 offset = loop[k] - loop[0];
                points[k] = new Point2(Vec3.Dot(offset, u), Vec3.Dot(offset, v));
            }

            return points;
        }

        private static bool IntersectsItself(Point2[] points)
        {
            int count = points.Length;
            for (int e = 0; e < count; e++)
            {
                for (int f = e + 2; f < count; f++)
                {
                    if (e == 0 && f == count - 1)
                    {
                        continue;
                    }

                    if (SegmentsMeet(points[e], points[e + 1], points[f], points[(f + 1) % count]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static List<Candidate> KeepDiagonals(Point2[] points, int[] nodeIds, HashSet<(int, int)> joined)
        {
            int count = points.Length;
            var candidates = new List<Candidate>();
            for (int i = 0; i < count; i++)
            {
                for (int j = i + 2; j < count; j++)
                {
                    if ((i == 0 && j == count - 1) || joined.Contains(PairKey(nodeIds[i], nodeIds[j])))
                    {
                        continue;
                    }

                    double length = Distance(points[i], points[j]);
                    double roundedLength = Math.Round(length / Constants.Bracing.LengthTieToleranceMeters, MidpointRounding.AwayFromZero);
                    candidates.Add(new Candidate(i, j, roundedLength));
                }
            }

            candidates.Sort(CompareCandidates);

            var kept = new List<Candidate>();
            foreach (Candidate candidate in candidates)
            {
                if (PassesOtherNode(points, candidate)
                    || CrossesLoopEdge(points, candidate)
                    || CrossesKeptBrace(points, kept, candidate)
                    || !Contains(points, Midpoint(points[candidate.I], points[candidate.J])))
                {
                    continue;
                }

                kept.Add(candidate);
            }

            return kept;
        }

        private static int CompareCandidates(Candidate first, Candidate second)
        {
            int byLength = first.RoundedLength.CompareTo(second.RoundedLength);
            if (byLength != 0)
            {
                return byLength;
            }

            int byI = first.I.CompareTo(second.I);
            return byI != 0 ? byI : first.J.CompareTo(second.J);
        }

        private static bool PassesOtherNode(Point2[] points, Candidate candidate)
        {
            for (int k = 0; k < points.Length; k++)
            {
                if (k != candidate.I && k != candidate.J
                    && DistanceToSegment(points[k], points[candidate.I], points[candidate.J]) <= Constants.Bracing.VertexClearanceMeters)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CrossesLoopEdge(Point2[] points, Candidate candidate)
        {
            for (int k = 0; k < points.Length; k++)
            {
                if (CrossesAwayFromSharedEnd(points, candidate.I, candidate.J, k, (k + 1) % points.Length))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CrossesKeptBrace(Point2[] points, List<Candidate> kept, Candidate candidate)
        {
            foreach (Candidate brace in kept)
            {
                if (CrossesAwayFromSharedEnd(points, candidate.I, candidate.J, brace.I, brace.J))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when segment (i, j) and segment (k, l) meet anywhere other than an end node they share.</summary>
        private static bool CrossesAwayFromSharedEnd(Point2[] points, int i, int j, int k, int l)
        {
            bool sharesI = i == k || i == l;
            bool sharesJ = j == k || j == l;
            if (!sharesI && !sharesJ)
            {
                return SegmentsMeet(points[i], points[j], points[k], points[l]);
            }

            // Two segments from one node meet again only when they overlap, which puts a far end on the other segment.
            int farOfFirst = sharesI ? j : i;
            int farOfSecond = (k == i || k == j) ? l : k;
            double clearance = Constants.Bracing.VertexClearanceMeters;
            return DistanceToSegment(points[farOfFirst], points[k], points[l]) <= clearance
                || DistanceToSegment(points[farOfSecond], points[i], points[j]) <= clearance;
        }

        /// <summary>True when the segments cross, or come within <see cref="Constants.Bracing.VertexClearanceMeters"/> of each other.</summary>
        private static bool SegmentsMeet(Point2 a, Point2 b, Point2 c, Point2 d)
        {
            if (Straddles(Orientation(a, b, c), Orientation(a, b, d)) && Straddles(Orientation(c, d, a), Orientation(c, d, b)))
            {
                return true;
            }

            double clearance = Constants.Bracing.VertexClearanceMeters;
            return DistanceToSegment(a, c, d) <= clearance
                || DistanceToSegment(b, c, d) <= clearance
                || DistanceToSegment(c, a, b) <= clearance
                || DistanceToSegment(d, a, b) <= clearance;
        }

        private static bool Straddles(double first, double second)
        {
            return (first > 0.0 && second < 0.0) || (first < 0.0 && second > 0.0);
        }

        private static double Orientation(Point2 a, Point2 b, Point2 c)
        {
            return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        }

        private static double DistanceToSegment(Point2 p, Point2 a, Point2 b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double lengthSquared = dx * dx + dy * dy;
            double t = lengthSquared > 0.0 ? ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared : 0.0;
            t = Math.Max(0.0, Math.Min(1.0, t));
            return Distance(p, new Point2(a.X + t * dx, a.Y + t * dy));
        }

        private static double Distance(Point2 a, Point2 b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static Point2 Midpoint(Point2 a, Point2 b)
        {
            return new Point2(0.5 * (a.X + b.X), 0.5 * (a.Y + b.Y));
        }

        /// <summary>Even-odd test, so it does not depend on the loop's winding.</summary>
        private static bool Contains(Point2[] polygon, Point2 point)
        {
            bool inside = false;
            for (int k = 0, previous = polygon.Length - 1; k < polygon.Length; previous = k++)
            {
                Point2 a = polygon[k];
                Point2 b = polygon[previous];
                if ((a.Y > point.Y) != (b.Y > point.Y))
                {
                    double crossingX = a.X + (point.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y);
                    if (point.X < crossingX)
                    {
                        inside = !inside;
                    }
                }
            }

            return inside;
        }

        private readonly struct Point2
        {
            public Point2(double x, double y)
            {
                X = x;
                Y = y;
            }

            public double X { get; }

            public double Y { get; }
        }

        private readonly struct Candidate
        {
            public Candidate(int i, int j, double roundedLength)
            {
                I = i;
                J = j;
                RoundedLength = roundedLength;
            }

            /// <summary>Lower loop position in <see cref="Fill.NodeIds"/>.</summary>
            public int I { get; }

            /// <summary>Higher loop position in <see cref="Fill.NodeIds"/>.</summary>
            public int J { get; }

            /// <summary>2D length in whole steps of <see cref="Constants.Bracing.LengthTieToleranceMeters"/>.</summary>
            public double RoundedLength { get; }
        }
    }
}
