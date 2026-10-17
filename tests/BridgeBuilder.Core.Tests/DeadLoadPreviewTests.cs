using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class DeadLoadPreviewTests
    {
        private const double StandardGravity = 9.80665;
        private const double LoadTolerance = 1e-6;
        private const double RelativeTolerance = 1e-12;

        [Fact]
        public void SelfWeight_SingleHorizontalMember_NodalLoads()
        {
            const double scale = 10.0;
            SectionProperties section = Section(1, area: 0.01, density: 1000.0);
            var input = new SolverInput
            {
                Nodes = new[]
                {
                    new Node { Id = 1, Position = new Vec3(0, 1, 0), Fixed = true },
                    new Node { Id = 2, Position = new Vec3(2, 1, 0) },
                },
                Members = new[] { new Member { Id = 1, NodeA = 1, NodeB = 2, SectionId = 1, LineId = 1 } },
                Sections = new[] { section },
                ScaleFactor = scale,
            };

            Assert.Equal(StandardGravity, Constants.Loads.Gravity);
            Assert.Equal(1961.33, DeadLoadPreview.MemberWeight(2.0, section, scale, StandardGravity), LoadTolerance);

            NodalLoad[] loads = DeadLoadPreview.SelfWeight(input, StandardGravity);
            NodalLoad[] defaults = DeadLoadPreview.SelfWeight(input);

            foreach (NodalLoad[] set in new[] { loads, defaults })
            {
                Assert.Equal(new[] { 1, 2 }, set.Select(l => l.NodeId));
                foreach (NodalLoad load in set)
                {
                    AssertFinite(load);
                    Assert.Equal(-980.665, load.Force.Y, LoadTolerance);
                    Assert.Equal(0.0, load.Force.X, LoadTolerance);
                    Assert.Equal(0.0, load.Force.Z, LoadTolerance);
                    Assert.Equal(Vec3.Zero, load.Moment);
                }
            }

            for (int i = 0; i < loads.Length; i++)
            {
                Assert.Equal(loads[i].Force, defaults[i].Force);
            }

            Assert.Equal(scale, input.ScaleFactor);
            Assert.Equal(new Vec3(2, 1, 0), input.Nodes[1].Position);
            Assert.Equal(
                DeadLoadPreview.MemberWeight(0.5, section, Constants.Scale.RoomToBridge, Constants.Loads.Gravity),
                DeadLoadPreview.MemberWeight(0.5, section));
        }

        [Fact]
        public void Preview_UsesStaticSolverAndReturnsUtilization()
        {
            const double scale = 10.0;
            SolverInput input = Portal(scale);
            MemberForce[] spyForces =
            {
                new MemberForce { MemberId = 10, Axial = -2.0e4, MomentLocalYAtA = 1.5e3, MomentLocalYAtB = -3.0e3, MomentLocalZAtA = 500.0, MomentLocalZAtB = -200.0, ShearLocalZAtA = 900.0, ShearLocalZAtB = -900.0 },
                new MemberForce { MemberId = 11, Axial = 1.0e4, MomentLocalYAtA = -8.0e3, MomentLocalYAtB = 2.0e3, MomentLocalZAtA = -1.0e3, MomentLocalZAtB = 4.0e3, ShearLocalYAtA = 50.0, ShearLocalYAtB = -50.0 },
                new MemberForce { MemberId = 12, Axial = -5.0e5, MomentLocalYAtA = 100.0, MomentLocalYAtB = -100.0 },
            };
            Vec3[] spyDisplacements = { Vec3.Zero, new Vec3(0.01, -0.02, 0.0), new Vec3(0.01, -0.03, 0.002), Vec3.Zero };
            Vec3[] spyRotations = { Vec3.Zero, new Vec3(0.0, 0.0, -0.001), new Vec3(0.0005, 0.0, 0.002), Vec3.Zero };
            var spy = new SpyStaticSolver(_ => new StaticSolveResult { Displacements = spyDisplacements, Rotations = spyRotations, MemberForces = spyForces });

            DeadLoadPreviewResult result = DeadLoadPreview.Run(input, spy, () => false, StandardGravity);

            Assert.Equal(1, spy.CallCount);
            Assert.Equal(DeadLoadPreviewStatus.Completed, result.Status);

            SolverInput received = spy.ReceivedInput;
            Assert.Equal(1.0, received.ScaleFactor);
            Assert.Equal(input.Nodes.Select(n => n.Id), received.Nodes.Select(n => n.Id));
            for (int i = 0; i < input.Nodes.Length; i++)
            {
                Assert.Equal(input.Nodes[i].Position * scale, received.Nodes[i].Position);
            }

            NodalLoad[] expectedLoads = DeadLoadPreview.SelfWeight(input, StandardGravity);
            Assert.Equal(expectedLoads.Select(l => (l.NodeId, l.Force, l.Moment)), spy.ReceivedLoads.Select(l => (l.NodeId, l.Force, l.Moment)));
            Assert.Equal(expectedLoads.Select(l => (l.NodeId, l.Force, l.Moment)), result.SelfWeight.Select(l => (l.NodeId, l.Force, l.Moment)));

            Assert.Equal(new[] { 10, 11, 12 }, result.MemberIds);
            Assert.Equal(spyForces.Length, result.Utilizations.Length);
            for (int m = 0; m < input.Members.Length; m++)
            {
                Member member = input.Members[m];
                SectionProperties section = input.Sections.Single(s => s.Id == member.SectionId);
                double bridgeLength = Vec3.Distance(PositionOf(received, member.NodeA), PositionOf(received, member.NodeB));
                double expected = MemberCapacity.Evaluate(spyForces[m], section, bridgeLength).Utilization;

                AssertFinite(result.Utilizations[m], "Utilization of member " + member.Id);
                Assert.True(result.Utilizations[m] > 0.0, "Member " + member.Id + " should be loaded.");
                AssertRelative(expected, result.Utilizations[m]);
                Assert.Equal(0.0, spyForces[m].Utilization);
            }

            Assert.NotEqual(result.Utilizations[0], result.Utilizations[1]);
            for (int i = 0; i < input.Nodes.Length; i++)
            {
                AssertComponents(spyDisplacements[i] / scale, result.StaticResult.Displacements[i]);
                Assert.Equal(spyRotations[i], result.StaticResult.Rotations[i]);
            }

            Assert.Equal(spyForces.Select(f => f.Axial), result.StaticResult.MemberForces.Select(f => f.Axial));
        }

        [Fact]
        public void Preview_CancelBeforeSolve_DoesNotCallSolver()
        {
            var spy = new SpyStaticSolver(_ => throw new InvalidOperationException("The solver must not be called."));
            int polls = 0;

            DeadLoadPreviewResult result = DeadLoadPreview.Run(Portal(Constants.Scale.RoomToBridge), spy, () =>
            {
                polls++;
                return true;
            });

            Assert.Equal(DeadLoadPreviewStatus.Cancelled, result.Status);
            Assert.Equal(0, spy.CallCount);
            Assert.Equal(1, polls);
            Assert.Null(result.StaticResult);
            Assert.Empty(result.SelfWeight);
            Assert.Empty(result.MemberIds);
            Assert.Empty(result.Utilizations);
        }

        [Fact]
        public void Preview_NoNaN()
        {
            SolverInput input = Truss(Constants.Scale.RoomToBridge);

            NodalLoad[] selfWeight = DeadLoadPreview.SelfWeight(input);
            DeadLoadPreviewResult result = DeadLoadPreview.Run(input, new StaticSolver(), () => false);
            DeadLoadPreviewResult explicitGravity = DeadLoadPreview.Run(input, new StaticSolver(), () => false, Constants.Loads.Gravity);

            Assert.Equal(input.Nodes.Length, selfWeight.Length);
            Assert.All(selfWeight, AssertFinite);
            Assert.All(result.SelfWeight, AssertFinite);
            double totalWeight = input.Members.Sum(m => DeadLoadPreview.MemberWeight(MemberLength(input, m), input.Sections.Single(s => s.Id == m.SectionId)));
            AssertRelative(-totalWeight, selfWeight.Sum(l => l.Force.Y));

            Assert.Equal(DeadLoadPreviewStatus.Completed, result.Status);
            Assert.Equal(input.Members.Select(m => m.Id), result.MemberIds);
            Assert.Equal(input.Members.Length, result.Utilizations.Length);
            foreach (double utilization in result.Utilizations)
            {
                AssertFinite(utilization, "Utilization");
                Assert.True(utilization >= 0.0);
            }

            Assert.Contains(result.Utilizations, u => u > 0.0);
            Assert.Equal(explicitGravity.Utilizations, result.Utilizations);

            StaticSolveResult solve = result.StaticResult;
            Assert.Equal(input.Nodes.Length, solve.Displacements.Length);
            Assert.All(solve.Displacements, v => AssertFinite(v, "Displacement"));
            Assert.All(solve.Rotations, v => AssertFinite(v, "Rotation"));
            Assert.All(solve.MemberForces, AssertFinite);
            Assert.True(solve.Displacements[IndexOf(input, 5)].Y < 0.0, "Midspan should sag under the bridge's own weight.");
        }

        [Fact]
        public void SelfWeight_SumsSharedNodesInNodeOrder()
        {
            const double scale = 5.0;
            SectionProperties light = Section(3, area: 0.002, density: 500.0);
            SectionProperties heavy = Section(8, area: 0.004, density: 2000.0);
            var input = new SolverInput
            {
                Nodes = new[]
                {
                    new Node { Id = 40, Position = new Vec3(0.3, 0.4, 0) },
                    new Node { Id = 7, Position = new Vec3(0, 0, 0), Fixed = true },
                    new Node { Id = 99, Position = new Vec3(5, 5, 5) },
                    new Node { Id = 12, Position = new Vec3(0.3, 0.4, 0.2) },
                },
                Members = new[]
                {
                    new Member { Id = 1, NodeA = 7, NodeB = 40, SectionId = 3, LineId = 1 },
                    new Member { Id = 2, NodeA = 40, NodeB = 12, SectionId = 8, LineId = 2, HiddenBrace = true },
                },
                Sections = new[] { light, heavy },
                ScaleFactor = scale,
            };
            double sloped = 0.5 * scale * light.A * light.Density * StandardGravity;
            double brace = 0.2 * scale * heavy.A * heavy.Density * StandardGravity;

            NodalLoad[] loads = DeadLoadPreview.SelfWeight(input, StandardGravity);

            Assert.Equal(new[] { 40, 7, 99, 12 }, loads.Select(l => l.NodeId));
            Assert.All(loads, AssertFinite);
            Assert.Equal(-(sloped + brace) / 2.0, loads[0].Force.Y, LoadTolerance);
            Assert.Equal(-sloped / 2.0, loads[1].Force.Y, LoadTolerance);
            Assert.Equal(Vec3.Zero, loads[2].Force);
            Assert.Equal(-brace / 2.0, loads[3].Force.Y, LoadTolerance);
            Assert.All(loads, l => Assert.True(l.Force.X == 0.0 && l.Force.Z == 0.0 && l.Moment == Vec3.Zero));
        }

        [Fact]
        public void Preview_RealSolver_ColumnCarriesItsOwnWeight()
        {
            const double scale = 20.0;
            SectionProperties section = Section(1, area: 0.01, density: 7850.0);
            var input = new SolverInput
            {
                Nodes = new[]
                {
                    new Node { Id = 1, Position = new Vec3(0, 0, 0), Fixed = true, GroundAnchor = true },
                    new Node { Id = 2, Position = new Vec3(0, 0.1, 0) },
                    new Node { Id = 3, Position = new Vec3(0, 0.2, 0) },
                },
                Members = new[]
                {
                    new Member { Id = 1, NodeA = 1, NodeB = 2, SectionId = 1, LineId = 1 },
                    new Member { Id = 2, NodeA = 2, NodeB = 3, SectionId = 1, LineId = 1 },
                },
                Sections = new[] { section },
                ScaleFactor = scale,
            };
            double segmentWeight = 0.1 * scale * section.A * section.Density * StandardGravity;

            DeadLoadPreviewResult result = DeadLoadPreview.Run(input, new StaticSolver(), () => false, StandardGravity);

            Assert.Equal(DeadLoadPreviewStatus.Completed, result.Status);
            MemberForce lower = result.StaticResult.MemberForces[0];
            MemberForce upper = result.StaticResult.MemberForces[1];
            AssertFinite(lower);
            AssertFinite(upper);
            AssertRelative(-1.5 * segmentWeight, lower.Axial, 1e-9);
            AssertRelative(-0.5 * segmentWeight, upper.Axial, 1e-9);
            AssertRelative(MemberCapacity.Evaluate(lower, section, 0.1 * scale).Utilization, result.Utilizations[0], 1e-9);
            AssertRelative(MemberCapacity.Evaluate(upper, section, 0.1 * scale).Utilization, result.Utilizations[1], 1e-9);
            Assert.True(result.Utilizations[0] > result.Utilizations[1], "The lower segment carries more weight.");
            Assert.True(result.StaticResult.Displacements[2].Y < 0.0, "The column top should shorten downward.");
            Assert.Equal(Vec3.Zero, result.StaticResult.Displacements[0]);
        }

        [Fact]
        public void Preview_CancelDuringSolve_DiscardsResult()
        {
            SolverInput input = Portal(Constants.Scale.RoomToBridge);
            bool drawing = false;
            var spy = new SpyStaticSolver(received =>
            {
                drawing = true;
                return new StaticSolver().Solve(received, Array.Empty<NodalLoad>());
            });

            DeadLoadPreviewResult result = DeadLoadPreview.Run(input, spy, () => drawing);

            Assert.Equal(1, spy.CallCount);
            Assert.Equal(DeadLoadPreviewStatus.Cancelled, result.Status);
            Assert.Null(result.StaticResult);
            Assert.Empty(result.Utilizations);
        }

        [Fact]
        public void Preview_RejectsInvalidInput()
        {
            SectionProperties section = Section(1, area: 0.01, density: 1000.0);
            var spy = new SpyStaticSolver(_ => throw new InvalidOperationException("The solver must not be called."));

            Assert.Throws<ArgumentNullException>(() => DeadLoadPreview.SelfWeight(null));
            Assert.Throws<ArgumentNullException>(() => DeadLoadPreview.Run(null, spy, () => false));
            Assert.Throws<ArgumentNullException>(() => DeadLoadPreview.Run(Portal(10.0), null, () => false));
            Assert.Throws<ArgumentNullException>(() => DeadLoadPreview.Run(Portal(10.0), spy, null));
            Assert.Throws<ArgumentNullException>(() => DeadLoadPreview.MemberWeight(1.0, null));

            Assert.Throws<ArgumentOutOfRangeException>(() => DeadLoadPreview.SelfWeight(Portal(0.0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => DeadLoadPreview.SelfWeight(Portal(10.0), double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => DeadLoadPreview.SelfWeight(Portal(10.0), -StandardGravity));
            Assert.Throws<ArgumentOutOfRangeException>(() => DeadLoadPreview.Run(Portal(10.0), spy, () => false, double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => DeadLoadPreview.MemberWeight(0.0, section));
            Assert.Throws<ArgumentOutOfRangeException>(() => DeadLoadPreview.MemberWeight(1.0, section, 0.0, StandardGravity));
            Assert.Throws<ArgumentOutOfRangeException>(() => DeadLoadPreview.MemberWeight(1.0, Section(1, area: 0.01, density: 0.0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => DeadLoadPreview.MemberWeight(1.0, Section(1, area: double.NaN, density: 1000.0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => DeadLoadPreview.MemberWeight(1.0e300, Section(1, area: 1.0e10, density: 1.0e10), 1.0, StandardGravity));

            SolverInput noDensity = Portal(10.0);
            noDensity.Sections = new[] { Section(1, area: 0.01, density: 0.0), Section(2, area: 0.02, density: 1000.0) };
            Assert.Throws<ArgumentOutOfRangeException>(() => DeadLoadPreview.Run(noDensity, spy, () => false));

            SolverInput coincident = Portal(10.0);
            coincident.Nodes[1] = new Node { Id = 2, Position = coincident.Nodes[0].Position };
            Assert.Throws<ArgumentOutOfRangeException>(() => DeadLoadPreview.SelfWeight(coincident));

            SolverInput missingSection = Portal(10.0);
            missingSection.Sections = new[] { section };
            Assert.Throws<ArgumentException>(() => DeadLoadPreview.SelfWeight(missingSection));

            SolverInput missingNode = Portal(10.0);
            missingNode.Members = new[] { new Member { Id = 1, NodeA = 1, NodeB = 77, SectionId = 1 } };
            Assert.Throws<ArgumentException>(() => DeadLoadPreview.SelfWeight(missingNode));

            SolverInput repeatedNode = Portal(10.0);
            repeatedNode.Nodes[3] = new Node { Id = 1, Position = new Vec3(1, 0, 0) };
            Assert.Throws<ArgumentException>(() => DeadLoadPreview.SelfWeight(repeatedNode));

            Assert.Equal(0, spy.CallCount);

            var wrongOrder = new SpyStaticSolver(received =>
            {
                StaticSolveResult solved = new StaticSolver().Solve(received, Array.Empty<NodalLoad>());
                solved.MemberForces = solved.MemberForces.Reverse().ToArray();
                return solved;
            });
            Assert.Throws<InvalidOperationException>(() => DeadLoadPreview.Run(Portal(10.0), wrongOrder, () => false));
            Assert.Throws<InvalidOperationException>(() => DeadLoadPreview.Run(Portal(10.0), new SpyStaticSolver(_ => null), () => false));
        }

        private static SolverInput Portal(double scale)
        {
            return new SolverInput
            {
                Nodes = new[]
                {
                    new Node { Id = 1, Position = new Vec3(0, 0, 0), Fixed = true, SurfaceId = 1 },
                    new Node { Id = 2, Position = new Vec3(0, 0.5, 0) },
                    new Node { Id = 3, Position = new Vec3(1, 0.5, 0) },
                    new Node { Id = 4, Position = new Vec3(1, 0, 0), Fixed = true, SurfaceId = 2 },
                },
                Members = new[]
                {
                    new Member { Id = 10, NodeA = 1, NodeB = 2, SectionId = 1, LineId = 1 },
                    new Member { Id = 11, NodeA = 2, NodeB = 3, SectionId = 2, LineId = 2 },
                    new Member { Id = 12, NodeA = 3, NodeB = 4, SectionId = 1, LineId = 3 },
                },
                Sections = new[]
                {
                    Section(1, area: 0.01, density: 7850.0),
                    new SectionProperties { Id = 2, E = 7.0e10, G = 2.6e10, A = 0.02, Iy = 2.0e-4, Iz = 5.0e-5, J = 1.0e-4, YieldStrength = 2.0e8, Density = 2700.0, Cy = 0.15, Cz = 0.05 },
                },
                ScaleFactor = scale,
            };
        }

        // Fixed at both surfaces: bottom chord 1-5-2, top chord 3-4, diagonals between them.
        private static SolverInput Truss(double scale)
        {
            return new SolverInput
            {
                Nodes = new[]
                {
                    new Node { Id = 1, Position = new Vec3(0, 0, 0), Fixed = true, SurfaceId = 1 },
                    new Node { Id = 2, Position = new Vec3(1, 0, 0), Fixed = true, SurfaceId = 2 },
                    new Node { Id = 3, Position = new Vec3(0.25, 0.25, 0) },
                    new Node { Id = 4, Position = new Vec3(0.75, 0.25, 0) },
                    new Node { Id = 5, Position = new Vec3(0.5, 0, 0) },
                },
                Members = new[]
                {
                    new Member { Id = 1, NodeA = 1, NodeB = 5, SectionId = 1, LineId = 1 },
                    new Member { Id = 2, NodeA = 5, NodeB = 2, SectionId = 1, LineId = 1 },
                    new Member { Id = 3, NodeA = 3, NodeB = 4, SectionId = 1, LineId = 2 },
                    new Member { Id = 4, NodeA = 1, NodeB = 3, SectionId = 1, LineId = 3 },
                    new Member { Id = 5, NodeA = 3, NodeB = 5, SectionId = 1, LineId = 3 },
                    new Member { Id = 6, NodeA = 5, NodeB = 4, SectionId = 1, LineId = 3 },
                    new Member { Id = 7, NodeA = 4, NodeB = 2, SectionId = 1, LineId = 3 },
                },
                Sections = new[] { Section(1, area: 0.01, density: 7850.0) },
                ScaleFactor = scale,
            };
        }

        private static SectionProperties Section(int id, double area, double density)
        {
            return new SectionProperties
            {
                Id = id,
                E = 2.0e11,
                G = 7.7e10,
                A = area,
                Iy = 1.0e-4,
                Iz = 1.0e-4,
                J = 2.0e-4,
                YieldStrength = 2.5e8,
                Density = density,
                Cy = 0.1,
                Cz = 0.1,
            };
        }

        private static int IndexOf(SolverInput input, int nodeId)
        {
            int index = Array.FindIndex(input.Nodes, n => n.Id == nodeId);
            Assert.True(index >= 0, "Node " + nodeId + " is not in the input.");
            return index;
        }

        private static Vec3 PositionOf(SolverInput input, int nodeId)
        {
            return input.Nodes[IndexOf(input, nodeId)].Position;
        }

        private static double MemberLength(SolverInput roomInput, Member member)
        {
            return Vec3.Distance(PositionOf(roomInput, member.NodeA), PositionOf(roomInput, member.NodeB));
        }

        private static void AssertFinite(NodalLoad load)
        {
            AssertFinite(load.Force, "Force on node " + load.NodeId);
            AssertFinite(load.Moment, "Moment on node " + load.NodeId);
        }

        private static void AssertFinite(MemberForce force)
        {
            foreach (double value in new[]
            {
                force.Axial, force.ShearLocalYAtA, force.ShearLocalZAtA, force.MomentLocalYAtA, force.MomentLocalZAtA,
                force.ShearLocalYAtB, force.ShearLocalZAtB, force.MomentLocalYAtB, force.MomentLocalZAtB, force.Utilization,
            })
            {
                AssertFinite(value, "Force component of member " + force.MemberId);
            }
        }

        private static void AssertFinite(Vec3 value, string name)
        {
            AssertFinite(value.X, name + ".X");
            AssertFinite(value.Y, name + ".Y");
            AssertFinite(value.Z, name + ".Z");
        }

        private static void AssertFinite(double value, string name)
        {
            Assert.True(double.IsFinite(value), name + " is " + value + ".");
        }

        private static void AssertComponents(Vec3 expected, Vec3 actual)
        {
            Assert.Equal(expected.X, actual.X, 1e-15);
            Assert.Equal(expected.Y, actual.Y, 1e-15);
            Assert.Equal(expected.Z, actual.Z, 1e-15);
        }

        private static void AssertRelative(double expected, double actual)
        {
            AssertRelative(expected, actual, RelativeTolerance);
        }

        private static void AssertRelative(double expected, double actual, double tolerance)
        {
            Assert.True(
                Math.Abs(actual - expected) <= tolerance * Math.Abs(expected),
                "Expected " + expected.ToString("R") + " within relative " + tolerance + " but got " + actual.ToString("R") + ".");
        }

        private sealed class SpyStaticSolver : IStaticSolver
        {
            private readonly Func<SolverInput, StaticSolveResult> respond;

            public SpyStaticSolver(Func<SolverInput, StaticSolveResult> respond)
            {
                this.respond = respond;
            }

            public int CallCount { get; private set; }

            public SolverInput ReceivedInput { get; private set; }

            public NodalLoad[] ReceivedLoads { get; private set; }

            public StaticSolveResult Solve(SolverInput input, NodalLoad[] loads)
            {
                CallCount++;
                ReceivedInput = input;
                ReceivedLoads = loads;
                return respond(input);
            }
        }
    }
}
