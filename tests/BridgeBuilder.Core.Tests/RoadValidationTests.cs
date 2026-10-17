using System;
using System.Collections.Generic;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class RoadValidationTests
    {
        private const int SurfaceA = 1;
        private const int SurfaceB = 2;
        private const int FirstFillId = 101;
        private const int SecondFillId = 102;
        private const double MaxSlopeDegrees = 20.0;
        private const double GentleSlopeDegrees = 10.0;
        private const double SteepSlopeDegrees = 25.0;
        private const double RoadWidth = 0.4;
        private const double FillLength = 0.5;
        private const int Precision = 9;

        private enum Link
        {
            SharedEdge,
            OneSharedNode,
            NoSharedNode,
        }

        [Fact]
        public void Chain_ConnectingBothSurfaces_Passes()
        {
            (Node[] nodes, Fill[] fills) = Chain(Link.SharedEdge, GentleSlopeDegrees, GentleSlopeDegrees);

            RoadValidationResult result = RoadValidation.Validate(nodes, fills, SurfaceA, SurfaceB, MaxSlopeDegrees);

            AssertFiniteReports(result);
            Assert.True(result.IsValid);
            Assert.False(result.HasGap);
            Assert.Empty(result.TooSteepFillIds);
            RoadComponent chain = Assert.Single(result.Components);
            Assert.Equal(new[] { FirstFillId, SecondFillId }, chain.FillIds);
            Assert.True(chain.TouchesSurfaceA);
            Assert.True(chain.TouchesSurfaceB);
            Assert.Equal(new[] { FirstFillId, SecondFillId }, Array.ConvertAll(result.Fills, f => f.FillId));
            Assert.All(result.Fills, f => Assert.Equal(GentleSlopeDegrees, f.SlopeDegrees, Precision));
            Assert.All(result.Fills, f => Assert.False(f.TooSteep));
        }

        [Fact]
        public void MissingLink_ReportsGap()
        {
            foreach (Link link in new[] { Link.NoSharedNode, Link.OneSharedNode })
            {
                (Node[] nodes, Fill[] fills) = Chain(link, GentleSlopeDegrees, GentleSlopeDegrees);

                RoadValidationResult result = RoadValidation.Validate(nodes, fills, SurfaceA, SurfaceB, MaxSlopeDegrees);

                AssertFiniteReports(result);
                Assert.False(result.IsValid);
                Assert.True(result.HasGap);
                Assert.Empty(result.TooSteepFillIds);
                Assert.All(result.Fills, f => Assert.False(f.TooSteep));
                Assert.Equal(2, result.Components.Length);
                Assert.Equal(new[] { FirstFillId }, result.Components[0].FillIds);
                Assert.True(result.Components[0].TouchesSurfaceA);
                Assert.False(result.Components[0].TouchesSurfaceB);
                Assert.Equal(new[] { SecondFillId }, result.Components[1].FillIds);
                Assert.False(result.Components[1].TouchesSurfaceA);
                Assert.True(result.Components[1].TouchesSurfaceB);
            }
        }

        [Fact]
        public void SteepFill_ReportsTooSteep()
        {
            (Node[] singleNodes, Fill[] singleFills) = Chain(Link.SharedEdge, SteepSlopeDegrees);

            RoadValidationResult single = RoadValidation.Validate(singleNodes, singleFills, SurfaceA, SurfaceB, MaxSlopeDegrees);

            AssertFiniteReports(single);
            Assert.False(single.IsValid);
            Assert.False(single.HasGap);
            Assert.Equal(new[] { FirstFillId }, single.TooSteepFillIds);
            Assert.Equal(SteepSlopeDegrees, Assert.Single(single.Fills).SlopeDegrees, Precision);

            (Node[] nodes, Fill[] fills) = Chain(Link.SharedEdge, GentleSlopeDegrees, SteepSlopeDegrees);

            RoadValidationResult result = RoadValidation.Validate(nodes, fills, SurfaceA, SurfaceB, MaxSlopeDegrees);

            AssertFiniteReports(result);
            Assert.False(result.IsValid);
            Assert.False(result.HasGap);
            Assert.Equal(new[] { SecondFillId }, result.TooSteepFillIds);
            RoadFillReport steep = Assert.Single(result.Fills, f => f.TooSteep);
            Assert.Equal(SecondFillId, steep.FillId);
            Assert.Equal(SteepSlopeDegrees, steep.SlopeDegrees, Precision);
        }

        [Fact]
        public void SlopeAtLimit_Passes()
        {
            (Node[] nodes, Fill[] fills) = Chain(Link.SharedEdge, MaxSlopeDegrees, MaxSlopeDegrees);

            RoadValidationResult result = RoadValidation.Validate(nodes, fills, SurfaceA, SurfaceB, MaxSlopeDegrees);

            AssertFiniteReports(result);
            Assert.True(result.IsValid);
            Assert.Empty(result.TooSteepFillIds);
            Assert.All(result.Fills, f => Assert.Equal(MaxSlopeDegrees, f.SlopeDegrees, Precision));
            Assert.Equal(MaxSlopeDegrees, RoadValidation.SlopeDegrees(fills[0], nodes), Precision);
        }

        [Fact]
        public void Width_IsShortestInPlaneEdge()
        {
            const double shortEdge = 2.0;
            const double longEdge = 4.0;
            foreach ((double acrossLength, double alongSlopeLength) in new[] { (longEdge, shortEdge), (shortEdge, longEdge) })
            {
                Vec3 across = new Vec3(acrossLength, 0.0, 0.0);
                Vec3 alongSlope = Rise(GentleSlopeDegrees) * (alongSlopeLength / FillLength);
                Node[] nodes =
                {
                    SurfaceNode(1, Vec3.Zero, SurfaceA),
                    SurfaceNode(2, across, SurfaceA),
                    SurfaceNode(3, across + alongSlope, SurfaceB),
                    SurfaceNode(4, alongSlope, SurfaceB),
                };
                Fill rectangle = RoadFill(FirstFillId, 1, 2, 3, 4);

                double width = RoadValidation.Width(rectangle, nodes);
                RoadValidationResult result = RoadValidation.Validate(nodes, new[] { rectangle }, SurfaceA, SurfaceB, MaxSlopeDegrees);

                Assert.False(double.IsNaN(width) || double.IsInfinity(width));
                Assert.Equal(shortEdge, width, Precision);
                AssertFiniteReports(result);
                Assert.Equal(shortEdge, Assert.Single(result.Fills).Width, Precision);
            }
        }

        [Fact]
        public void DefaultSlope_ReadsConstant()
        {
            const double steeperByDegrees = 5.0;
            double limit = Constants.Road.MaxSlopeDegrees;

            (Node[] atLimitNodes, Fill[] atLimitFills) = Chain(Link.SharedEdge, limit, limit);
            RoadValidationResult atLimit = RoadValidation.Validate(atLimitNodes, atLimitFills, SurfaceA, SurfaceB);

            AssertFiniteReports(atLimit);
            Assert.True(atLimit.IsValid);
            Assert.Empty(atLimit.TooSteepFillIds);

            (Node[] steeperNodes, Fill[] steeperFills) = Chain(Link.SharedEdge, limit, limit + steeperByDegrees);
            RoadValidationResult steeper = RoadValidation.Validate(steeperNodes, steeperFills, SurfaceA, SurfaceB);

            AssertFiniteReports(steeper);
            Assert.False(steeper.IsValid);
            Assert.False(steeper.HasGap);
            Assert.Equal(new[] { SecondFillId }, steeper.TooSteepFillIds);
            Assert.Equal(limit + steeperByDegrees, steeper.Fills[1].SlopeDegrees, Precision);
        }

        [Fact]
        public void NonRoadOrInvalidFill_DoesNotLinkChain()
        {
            (Node[] nodes, Fill[] fills) = Chain(Link.SharedEdge, GentleSlopeDegrees, GentleSlopeDegrees);
            fills[1].Road = false;

            RoadValidationResult notRoad = RoadValidation.Validate(nodes, fills, SurfaceA, SurfaceB, MaxSlopeDegrees);

            Assert.True(notRoad.HasGap);
            Assert.Equal(FirstFillId, Assert.Single(notRoad.Fills).FillId);

            fills[1].Road = true;
            fills[1].Valid = false;

            RoadValidationResult invalid = RoadValidation.Validate(nodes, fills, SurfaceA, SurfaceB, MaxSlopeDegrees);

            Assert.True(invalid.HasGap);
            Assert.Equal(FirstFillId, Assert.Single(invalid.Fills).FillId);

            fills[0].Road = false;

            RoadValidationResult none = RoadValidation.Validate(nodes, fills, SurfaceA, SurfaceB, MaxSlopeDegrees);

            Assert.True(none.HasGap);
            Assert.False(none.IsValid);
            Assert.Empty(none.Fills);
            Assert.Empty(none.Components);
        }

        [Fact]
        public void Validate_RejectsBadInput()
        {
            (Node[] nodes, Fill[] fills) = Chain(Link.SharedEdge, GentleSlopeDegrees);

            Assert.Throws<ArgumentException>(() => RoadValidation.Validate(nodes, fills, SurfaceA, SurfaceA, MaxSlopeDegrees));
            Assert.Throws<ArgumentOutOfRangeException>(() => RoadValidation.Validate(nodes, fills, SurfaceA, SurfaceB, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => RoadValidation.Validate(nodes, fills, SurfaceA, SurfaceB, -1.0));
            Assert.Throws<ArgumentNullException>(() => RoadValidation.Validate(null, fills, SurfaceA, SurfaceB, MaxSlopeDegrees));
            Assert.Throws<ArgumentNullException>(() => RoadValidation.Validate(nodes, null, SurfaceA, SurfaceB, MaxSlopeDegrees));

            Fill missingNode = RoadFill(SecondFillId, 1, 2, 999);
            Assert.Throws<ArgumentException>(() => RoadValidation.Validate(nodes, new[] { missingNode }, SurfaceA, SurfaceB, MaxSlopeDegrees));

            Node[] collinear =
            {
                SurfaceNode(1, Vec3.Zero, SurfaceA),
                SurfaceNode(2, Vec3.UnitX, null),
                SurfaceNode(3, Vec3.UnitX * 2.0, SurfaceB),
            };
            Fill flat = RoadFill(FirstFillId, 1, 2, 3);
            Assert.Throws<ArgumentException>(() => RoadValidation.Validate(collinear, new[] { flat }, SurfaceA, SurfaceB, MaxSlopeDegrees));
            Assert.Throws<ArgumentException>(() => RoadValidation.SlopeDegrees(flat, collinear));
            Assert.Throws<ArgumentException>(() => RoadValidation.Width(flat, collinear));
        }

        /// <summary>
        /// Builds a straight road of fills along +Z, one per slope, each <see cref="FillLength"/> long
        /// in its own plane and <see cref="RoadWidth"/> wide along X. The first row of nodes is on
        /// surface A and the last row on surface B. <paramref name="link"/> controls how many node ids
        /// the first and second fills share; coincident replacement nodes keep the geometry the same.
        /// </summary>
        private static (Node[] Nodes, Fill[] Fills) Chain(Link link, params double[] slopesDegrees)
        {
            var nodes = new List<Node>();
            var fills = new List<Fill>();

            Node AddNode(Vec3 position, int? surfaceId)
            {
                Node node = SurfaceNode(nodes.Count + 1, position, surfaceId);
                nodes.Add(node);
                return node;
            }

            var across = new Vec3(RoadWidth, 0.0, 0.0);
            Node left = AddNode(Vec3.Zero, SurfaceA);
            Node right = AddNode(across, SurfaceA);
            for (int i = 0; i < slopesDegrees.Length; i++)
            {
                if (i == 1 && link != Link.SharedEdge)
                {
                    left = AddNode(left.Position, null);
                    if (link == Link.NoSharedNode)
                    {
                        right = AddNode(right.Position, null);
                    }
                }

                Vec3 rowStart = left.Position;
                Vec3 rowEnd = rowStart + Rise(slopesDegrees[i]);
                int? surfaceId = i == slopesDegrees.Length - 1 ? SurfaceB : (int?)null;
                Node nextLeft = AddNode(rowEnd, surfaceId);
                Node nextRight = AddNode(rowEnd + across, surfaceId);
                fills.Add(RoadFill(FirstFillId + i, left.Id, right.Id, nextRight.Id, nextLeft.Id));
                left = nextLeft;
                right = nextRight;
            }

            return (nodes.ToArray(), fills.ToArray());
        }

        /// <summary>A step of <see cref="FillLength"/> along +Z that rises at <paramref name="slopeDegrees"/> from horizontal.</summary>
        private static Vec3 Rise(double slopeDegrees)
        {
            double radians = slopeDegrees * Math.PI / 180.0;
            return new Vec3(0.0, FillLength * Math.Sin(radians), FillLength * Math.Cos(radians));
        }

        private static Node SurfaceNode(int id, Vec3 position, int? surfaceId)
        {
            return new Node { Id = id, Position = position, SurfaceId = surfaceId, Fixed = surfaceId.HasValue };
        }

        private static Fill RoadFill(int id, params int[] nodeIds)
        {
            return new Fill { Id = id, NodeIds = nodeIds, Road = true, Valid = true };
        }

        private static void AssertFiniteReports(RoadValidationResult result)
        {
            foreach (RoadFillReport report in result.Fills)
            {
                Assert.False(double.IsNaN(report.SlopeDegrees) || double.IsInfinity(report.SlopeDegrees), "Fill " + report.FillId + " slope is not finite.");
                Assert.False(double.IsNaN(report.Width) || double.IsInfinity(report.Width), "Fill " + report.FillId + " width is not finite.");
            }
        }
    }
}
