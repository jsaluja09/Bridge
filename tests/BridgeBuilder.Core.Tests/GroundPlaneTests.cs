using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class GroundPlaneTests
    {
        [Fact]
        public void Grid_SitsAtFloorHeight()
        {
            var a0 = new Vec3(0, 2, 0);
            var a1 = new Vec3(0.3, 2, 0);
            var b0 = new Vec3(0, 2, 0.5);
            var b1 = new Vec3(0.3, 2, 0.5);

            Node[] nodes = GroundPlane.BuildGrid(a0, a1, b0, b1, 0.5, null);
            Assert.NotEmpty(nodes);
            AssertGroundAnchors(nodes, 0.5);

            Assert.Equal(0.10, Constants.Ground.GridSpacingMeters);
            Node[] explicitSpacing = GroundPlane.BuildGrid(a0, a1, b0, b1, 0.5, null, Constants.Ground.GridSpacingMeters);
            AssertSameNodes(explicitSpacing, nodes);
        }

        [Fact]
        public void Grid_OverrideReplacesHeight()
        {
            var a0 = new Vec3(0, 1, 0);
            var a1 = new Vec3(1, 1, 0);
            var b0 = new Vec3(0, 1, 2);
            var b1 = new Vec3(1, 1, 2);

            Node[] onFloor0 = GroundPlane.BuildGrid(a0, a1, b0, b1, 0.0, -1.5);
            Assert.NotEmpty(onFloor0);
            AssertGroundAnchors(onFloor0, -1.5);

            Node[] onFloor2 = GroundPlane.BuildGrid(a0, a1, b0, b1, 2.0, -1.5);
            AssertGroundAnchors(onFloor2, -1.5);
            Assert.Equal(PlanPoints(onFloor0), PlanPoints(onFloor2));

            Assert.Equal(-1.5, GroundPlane.Height(0.0, -1.5));
            Assert.Equal(-1.5, GroundPlane.Height(2.0, -1.5));
            Assert.Equal(2.0, GroundPlane.Height(2.0, null));
        }

        [Fact]
        public void Grid_LimitedToFootprint()
        {
            var expected = new List<(double X, double Z)>();
            for (int x = 0; x <= 2; x++)
            {
                for (int z = 0; z <= 3; z++)
                {
                    expected.Add((x, z));
                }
            }

            Node[] parallel = GroundPlane.BuildGrid(new Vec3(0, 0, 0), new Vec3(2, 0, 0), new Vec3(0, 0, 3), new Vec3(2, 0, 3), 0.0, null, 1.0);
            Assert.Equal(12, parallel.Length);
            Assert.Equal(expected, PlanPoints(parallel));
            AssertGroundAnchors(parallel, 0.0);
            AssertOrderedByXThenZ(parallel);

            Node[] crossed = GroundPlane.BuildGrid(new Vec3(0, 0, 0), new Vec3(2, 0, 3), new Vec3(2, 0, 0), new Vec3(0, 0, 3), 0.0, null, 1.0);
            Assert.Equal(expected, PlanPoints(crossed));
            AssertGroundAnchors(crossed, 0.0);
        }

        [Fact]
        public void Grid_EmptyWhenSpacingNonPositive()
        {
            var a0 = new Vec3(0, 0, 0);
            var a1 = new Vec3(2, 0, 0);
            var b0 = new Vec3(0, 0, 3);
            var b1 = new Vec3(2, 0, 3);

            ArgumentOutOfRangeException zero = Assert.Throws<ArgumentOutOfRangeException>(() => GroundPlane.BuildGrid(a0, a1, b0, b1, 0.0, null, 0.0));
            Assert.Equal("spacingMeters", zero.ParamName);
            ArgumentOutOfRangeException negative = Assert.Throws<ArgumentOutOfRangeException>(() => GroundPlane.BuildGrid(a0, a1, b0, b1, 0.0, null, -1.0));
            Assert.Equal("spacingMeters", negative.ParamName);
        }

        [Fact]
        public void Grid_IsDeterministic()
        {
            var a0 = new Vec3(-0.37, 1.12, 0.42);
            var a1 = new Vec3(0.81, 1.19, 0.45);
            var b0 = new Vec3(-0.35, 0.94, 1.63);
            var b1 = new Vec3(0.79, 1.01, 1.70);

            Node[] first = GroundPlane.BuildGrid(a0, a1, b0, b1, 0.0, null);
            Node[] second = GroundPlane.BuildGrid(a0, a1, b0, b1, 0.0, null);

            Assert.NotEmpty(first);
            Assert.Equal(first.Select(n => n.Position), second.Select(n => n.Position));
            AssertSameNodes(first, second);
            AssertGroundAnchors(first, 0.0);
            AssertOrderedByXThenZ(first);
            Assert.All(first, n => Assert.True(n.Position.X >= -0.37 && n.Position.X <= 0.81 && n.Position.Z >= 0.42 && n.Position.Z <= 1.70));
        }

        [Fact]
        public void Grid_KeepsLastRowAtDefaultSpacing()
        {
            double spacing = Constants.Ground.GridSpacingMeters;

            Node[] nodes = GroundPlane.BuildGrid(new Vec3(0, 0, 0), new Vec3(0.7, 0, 0), new Vec3(0, 0, 0.3), new Vec3(0.7, 0, 0.3), 0.0, null);
            double[] xs = nodes.Select(n => n.Position.X).Distinct().ToArray();
            double[] zs = nodes.Select(n => n.Position.Z).Distinct().ToArray();
            Assert.Equal(8, xs.Length);
            Assert.Equal(4, zs.Length);
            Assert.Equal(32, nodes.Length);
            for (int i = 0; i < xs.Length; i++)
            {
                Assert.Equal(i * spacing, xs[i], 12);
            }

            for (int j = 0; j < zs.Length; j++)
            {
                Assert.Equal(j * spacing, zs[j], 12);
            }

            Assert.Equal(0.7, xs[xs.Length - 1]);
            Assert.Equal(0.3, zs[zs.Length - 1]);
            AssertGroundAnchors(nodes, 0.0);

            Node[] partial = GroundPlane.BuildGrid(new Vec3(1.2, 0, -0.05), new Vec3(1.45, 0, -0.05), new Vec3(1.2, 0, 0.1), new Vec3(1.45, 0, 0.1), 0.0, null);
            double[] partialXs = partial.Select(n => n.Position.X).Distinct().ToArray();
            double[] partialZs = partial.Select(n => n.Position.Z).Distinct().ToArray();
            Assert.Equal(3, partialXs.Length);
            Assert.Equal(2, partialZs.Length);
            Assert.Equal(1.2, partialXs[0]);
            Assert.Equal(1.4, partialXs[2], 12);
            Assert.Equal(-0.05, partialZs[0]);
            Assert.Equal(0.05, partialZs[1], 12);
            AssertGroundAnchors(partial, 0.0);
        }

        [Fact]
        public void Grid_RejectsNonFiniteInputs()
        {
            var a0 = new Vec3(0, 0, 0);
            var a1 = new Vec3(1, 0, 0);
            var b0 = new Vec3(0, 0, 1);
            var b1 = new Vec3(1, 0, 1);

            Assert.Throws<ArgumentOutOfRangeException>(() => GroundPlane.BuildGrid(a0, a1, b0, b1, 0.0, null, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => GroundPlane.BuildGrid(a0, a1, b0, b1, 0.0, null, double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => GroundPlane.BuildGrid(a0, a1, b0, b1, double.NaN, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => GroundPlane.BuildGrid(a0, a1, b0, b1, double.PositiveInfinity, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => GroundPlane.BuildGrid(a0, a1, b0, b1, 0.0, double.NegativeInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => GroundPlane.BuildGrid(a0, a1, b0, b1, 0.0, double.NaN));
            Assert.Throws<ArgumentException>(() => GroundPlane.BuildGrid(new Vec3(double.PositiveInfinity, 0, 0), a1, b0, b1, 0.0, null));
            Assert.Throws<ArgumentException>(() => GroundPlane.BuildGrid(a0, a1, b0, new Vec3(1, 0, double.NegativeInfinity), 0.0, null));

            ArgumentOutOfRangeException tooFine = Assert.Throws<ArgumentOutOfRangeException>(() => GroundPlane.BuildGrid(a0, a1, b0, b1, 0.0, null, 1e-6));
            Assert.Equal("spacingMeters", tooFine.ParamName);
        }

        private static List<(double X, double Z)> PlanPoints(Node[] nodes)
        {
            return nodes.Select(n => (n.Position.X, n.Position.Z)).ToList();
        }

        private static void AssertGroundAnchors(Node[] nodes, double expectedY)
        {
            foreach (Node node in nodes)
            {
                Assert.True(double.IsFinite(node.Position.X), "Non-finite X at node " + node.Id + ".");
                Assert.True(double.IsFinite(node.Position.Y), "Non-finite Y at node " + node.Id + ".");
                Assert.True(double.IsFinite(node.Position.Z), "Non-finite Z at node " + node.Id + ".");
                Assert.Equal(expectedY, node.Position.Y);
                Assert.True(node.Fixed, "Node " + node.Id + " must be fixed.");
                Assert.True(node.GroundAnchor, "Node " + node.Id + " must be a ground anchor.");
                Assert.Null(node.SurfaceId);
            }

            Assert.Equal(Enumerable.Range(0, nodes.Length), nodes.Select(n => n.Id));
        }

        private static void AssertOrderedByXThenZ(Node[] nodes)
        {
            for (int k = 1; k < nodes.Length; k++)
            {
                Vec3 previous = nodes[k - 1].Position;
                Vec3 current = nodes[k].Position;
                bool ordered = previous.X < current.X || (previous.X == current.X && previous.Z < current.Z);
                Assert.True(ordered, "Node " + nodes[k].Id + " is out of X-then-Z order.");
            }
        }

        private static void AssertSameNodes(Node[] expected, Node[] actual)
        {
            Assert.Equal(expected.Length, actual.Length);
            for (int k = 0; k < expected.Length; k++)
            {
                Assert.Equal(expected[k].Id, actual[k].Id);
                Assert.Equal(expected[k].Position, actual[k].Position);
                Assert.Equal(expected[k].Fixed, actual[k].Fixed);
                Assert.Equal(expected[k].GroundAnchor, actual[k].GroundAnchor);
                Assert.Equal(expected[k].SurfaceId, actual[k].SurfaceId);
            }
        }
    }
}
