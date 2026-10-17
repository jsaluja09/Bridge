using System;
using System.Collections.Generic;
using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>W2-03: fill planarity.</summary>
    public class FillPlanarityTests
    {
        static List<Vec3> Quad(double lift) => new List<Vec3> { new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(1, 0, 1), new Vec3(0, lift, 1) };

        [Fact] public void PlanarQuad_Accepted() => Assert.True(FillPlanarity.IsPlanar(Quad(0), 0.01));
        [Fact] public void NonPlanarQuad_Rejected() => Assert.False(FillPlanarity.IsPlanar(Quad(0.05), 0.01));

        [Fact]
        public void ToleranceIsInclusive_OnThePlane()
        {
            Assert.True(FillPlanarity.IsPlanar(Quad(0), 0.01));
            Assert.True(FillPlanarity.IsPlanar(Quad(0.01), 0.01));
            Assert.False(FillPlanarity.IsPlanar(Quad(0.011), 0.01));
        }

        [Fact]
        public void DefaultTolerance_ReadsConstant()
        {
            Assert.Equal(0.01, Constants.Fill.PlanarityToleranceMeters);
            Assert.True(FillPlanarity.IsPlanar(Quad(0.01)));
            Assert.False(FillPlanarity.IsPlanar(Quad(0.02)));
        }
    }

    /// <summary>W2-04: hidden bracing generation. The pattern itself waits on HG-bracing.</summary>
    public class FillBracingTests
    {
        static (BridgeModel model, int fill) Deck(double lift = 0)
        {
            var m = new BridgeModel(new BudgetPolicy { CostPerMeter = 1, CostPerSquareMeter = 1, MaterialBudget = 1000, NodeCap = 1000 });
            int a = m.AddFixedNode(new Vec3(0, 0, 0)), d = m.AddFixedNode(new Vec3(0, 0, 0.2));
            var top = m.TryAddLine(new[] { LineVertex.Existing(a), LineVertex.New(new Vec3(0.5, 0, 0)), LineVertex.New(new Vec3(1, 0, 0)) });
            var nodesA = m.LineNodes(top.Id);
            var bottom = m.TryAddLine(new[] { LineVertex.Existing(d), LineVertex.New(new Vec3(0.5, 0, 0.2)), LineVertex.New(new Vec3(1, lift, 0.2)) });
            var nodesB = m.LineNodes(bottom.Id);
            m.TryAddLine(new[] { LineVertex.Existing(nodesA.Last()), LineVertex.Existing(nodesB.Last()) });
            var loop = nodesA.Concat(Enumerable.Reverse(nodesB)).ToList();
            return (m, m.TryAddFill(loop, true).Id);
        }

        [Fact]
        public void Bracing_UsesOnlyBoundaryNodes()
        {
            var (m, fill) = Deck();
            int nodes = m.Nodes.Count;
            var added = FillBracing.Apply(m, fill, 0);
            Assert.NotEmpty(added);
            var loop = m.GetFill(fill).Loop;
            foreach (int id in added)
                Assert.True(loop.Contains(m.GetMember(id).NodeA) && loop.Contains(m.GetMember(id).NodeB));
            Assert.Equal(nodes, m.Nodes.Count);
        }

        [Fact]
        public void Bracing_MembersLieInFillPlane()
        {
            var (m, fill) = Deck();
            foreach (int id in FillBracing.Apply(m, fill, 0))
            {
                var mem = m.GetMember(id);
                Assert.True(Math.Abs(m.GetNode(mem.NodeA).Position.Y) <= Constants.Fill.PlanarityToleranceMeters);
                Assert.True(Math.Abs(m.GetNode(mem.NodeB).Position.Y) <= Constants.Fill.PlanarityToleranceMeters);
            }
        }

        [Fact]
        public void Bracing_MarkedHidden()
        {
            var (m, fill) = Deck();
            Assert.All(FillBracing.Apply(m, fill, 0), id => Assert.True(m.GetMember(id).HiddenBrace));
        }

        [Fact]
        public void Bracing_DoesNotChangeBoundaryMembers()
        {
            var (m, fill) = Deck();
            var before = m.Members.Select(x => (x.Id, x.NodeA, x.NodeB)).ToList();
            FillBracing.Apply(m, fill, 0);
            foreach (var b in before)
                Assert.Contains(m.Members, x => x.Id == b.Id && x.NodeA == b.NodeA && x.NodeB == b.NodeB);
        }

        [Fact]
        public void NonPlanarFill_Throws()
        {
            var (m, fill) = Deck(lift: 0.1);
            int members = m.Members.Count;
            Assert.Throws<InvalidOperationException>(() => FillBracing.Apply(m, fill, 0));
            Assert.Equal(members, m.Members.Count);
        }

        [Fact]
        public void Bracing_NotChargedToBudget()
        {
            var (m, fill) = Deck();
            double spent = m.Spent;
            FillBracing.Apply(m, fill, 0);
            Assert.Equal(spent, m.Spent);
        }

        [Fact(Skip = "Waits on HG-bracing: a human writes the quad and n-gon node pairs into issue W2-04.")]
        public void Bracing_MatchesAppendedPattern() { }
    }

    /// <summary>W2-05: road-chain validation.</summary>
    public class RoadValidationTests
    {
        static Dictionary<int, Node> nodes;

        static Func<int, Node> Lookup => id => nodes[id];

        static List<Fill> Chain(double slopeDeg, bool linked)
        {
            double rise = Math.Tan(slopeDeg * Math.PI / 180);
            nodes = new Dictionary<int, Node>
            {
                [0] = new Node { Id = 0, Position = new Vec3(0, 0, 0), Fixed = true, SurfaceId = 0 },
                [1] = new Node { Id = 1, Position = new Vec3(0, 0, 1), Fixed = true, SurfaceId = 0 },
                [2] = new Node { Id = 2, Position = new Vec3(1, rise, 0) },
                [3] = new Node { Id = 3, Position = new Vec3(1, rise, 1) },
                [4] = new Node { Id = 4, Position = new Vec3(1, rise, 0) },
                [5] = new Node { Id = 5, Position = new Vec3(1, rise, 1) },
                [6] = new Node { Id = 6, Position = new Vec3(2, 2 * rise, 0), Fixed = true, SurfaceId = 1 },
                [7] = new Node { Id = 7, Position = new Vec3(2, 2 * rise, 1), Fixed = true, SurfaceId = 1 },
            };
            return new List<Fill>
            {
                new Fill { Id = 10, Loop = new List<int> { 0, 2, 3, 1 }, Road = true },
                new Fill { Id = 11, Loop = linked ? new List<int> { 2, 6, 7, 3 } : new List<int> { 4, 6, 7, 5 }, Road = true },
            };
        }

        [Fact]
        public void Chain_ConnectingBothSurfaces_Passes() => Assert.True(RoadValidation.Validate(Chain(5, true), Lookup, 0, 1, 20).Ok);

        [Fact]
        public void MissingLink_ReportsGap()
        {
            var r = RoadValidation.Validate(Chain(5, false), Lookup, 0, 1, 20);
            Assert.True(r.Gap);
            Assert.Empty(r.TooSteepFillIds);
        }

        [Fact]
        public void SteepFill_ReportsTooSteep()
        {
            var r = RoadValidation.Validate(Chain(25, true), Lookup, 0, 1, 20);
            Assert.False(r.Ok);
            Assert.Contains(10, r.TooSteepFillIds);
        }

        [Fact] public void SlopeAtLimit_Passes() => Assert.True(RoadValidation.Validate(Chain(20, true), Lookup, 0, 1, 20).Ok);

        [Fact]
        public void Width_IsShortestInPlaneEdge()
        {
            nodes = new Dictionary<int, Node>
            {
                [0] = new Node { Id = 0, Position = new Vec3(0, 0, 0), SurfaceId = 0 },
                [1] = new Node { Id = 1, Position = new Vec3(4, 0, 0), SurfaceId = 1 },
                [2] = new Node { Id = 2, Position = new Vec3(4, 0, 2) },
                [3] = new Node { Id = 3, Position = new Vec3(0, 0, 2) },
            };
            var r = RoadValidation.Validate(new List<Fill> { new Fill { Id = 1, Loop = new List<int> { 0, 1, 2, 3 }, Road = true } }, Lookup, 0, 1, 20);
            Assert.Equal(2, r.Width[1], 9);
        }

        [Fact]
        public void DefaultSlope_ReadsConstant()
        {
            Assert.Equal(20.0, Constants.Road.MaxSlopeDegrees);
            Assert.True(RoadValidation.Validate(Chain(Constants.Road.MaxSlopeDegrees, true), Lookup, 0, 1).Ok);
            Assert.NotEmpty(RoadValidation.Validate(Chain(Constants.Road.MaxSlopeDegrees + 5, true), Lookup, 0, 1).TooSteepFillIds);
        }
    }
}
