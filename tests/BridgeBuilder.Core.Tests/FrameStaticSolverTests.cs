using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class FrameStaticSolverTests
    {
        private const string Fixture2 = "fixture-02-fixed-end-beams.json";
        private const string Fixture3 = "fixture-03-unbraced-rectangle.json";

        // Absolute tolerances: meters, and newtons or newton-meters.
        private const double DisplacementTolerance = 1e-9;
        private const double ForceTolerance = 1e-6;

        // Fixture positions are solver lengths, so these solves use no room-to-bridge scaling.
        private const double UnitScale = 1.0;

        [Fact]
        public void FrameFixture2_CantileverTipDeflection_Is0_09m()
        {
            CantileverCase cantilever = FixtureLoader.LoadAs<Fixture2Data>(Fixture2).Cantilever;
            SolverInput input = InputForLine(FixtureLoader.Load(Fixture2), cantilever.LineId);

            StaticSolveResult result = new StaticSolver().Solve(input, cantilever.Loads);

            AssertAllFinite(result);
            SectionProperties section = SectionOf(input, cantilever.MemberId);
            double length = Distance(input, cantilever.FixedNodeId, cantilever.TipNodeId);
            double load = Assert.Single(cantilever.Loads).Force.Z;
            Assert.Equal(cantilever.ExpectedTipDisplacement.Z, load * Math.Pow(length, 3) / (3.0 * section.E * section.Iy), DisplacementTolerance);

            Vec3 tip = result.Displacements[IndexOf(input, cantilever.TipNodeId)];
            Assert.Equal(cantilever.ExpectedTipDisplacement.X, tip.X, DisplacementTolerance);
            Assert.Equal(cantilever.ExpectedTipDisplacement.Y, tip.Y, DisplacementTolerance);
            Assert.Equal(cantilever.ExpectedTipDisplacement.Z, tip.Z, DisplacementTolerance);
        }

        [Fact]
        public void FrameFixture2_CantileverFixedEndMoment_Is48000()
        {
            CantileverCase cantilever = FixtureLoader.LoadAs<Fixture2Data>(Fixture2).Cantilever;
            SolverInput input = InputForLine(FixtureLoader.Load(Fixture2), cantilever.LineId);

            StaticSolveResult result = new StaticSolver().Solve(input, cantilever.Loads);

            AssertAllFinite(result);
            double length = Distance(input, cantilever.FixedNodeId, cantilever.TipNodeId);
            double load = Assert.Single(cantilever.Loads).Force.Z;
            Assert.Equal(cantilever.ExpectedFixedEndMoment, load * length, ForceTolerance);
            Assert.Equal(cantilever.ExpectedFixedEndShear, load, ForceTolerance);

            MemberForce force = ForceOf(result, cantilever.MemberId);
            Assert.Equal(cantilever.FixedNodeId, MemberOf(input, cantilever.MemberId).NodeA);
            Assert.Equal(cantilever.ExpectedFixedEndMoment, Math.Abs(force.MomentLocalYAtA), ForceTolerance);
            Assert.Equal(cantilever.ExpectedFixedEndShear, Math.Abs(force.ShearLocalZAtA), ForceTolerance);
            Assert.Equal(cantilever.ExpectedAxial, force.Axial, ForceTolerance);
            Assert.Equal(cantilever.ExpectedFreeEndMoment, Math.Abs(force.MomentLocalYAtB), ForceTolerance);
            AssertOtherBendingPlaneIsZero(force);
        }

        [Fact]
        public void FrameFixture2_FixedFixedMidspanDeflection_Is0_01m()
        {
            FixedFixedCase beam = FixtureLoader.LoadAs<Fixture2Data>(Fixture2).FixedFixed;
            SolverInput input = InputForLine(FixtureLoader.Load(Fixture2), beam.LineId);

            StaticSolveResult result = new StaticSolver().Solve(input, beam.Loads);

            AssertAllFinite(result);
            SectionProperties section = SectionOf(input, beam.FirstMemberId);
            double span = Distance(input, beam.FirstFixedNodeId, beam.SecondFixedNodeId);
            double load = Assert.Single(beam.Loads).Force.Z;
            Assert.Equal(beam.ExpectedMidspanDisplacementZ, load * Math.Pow(span, 3) / (192.0 * section.E * section.Iy), DisplacementTolerance);

            Vec3 midspan = result.Displacements[IndexOf(input, beam.MidspanNodeId)];
            Assert.Equal(beam.ExpectedMidspanDisplacementZ, midspan.Z, DisplacementTolerance);
            Assert.Equal(0.0, midspan.X, DisplacementTolerance);
            Assert.Equal(0.0, midspan.Y, DisplacementTolerance);
        }

        [Fact]
        public void FrameFixture2_FixedFixedMoments_Are30000()
        {
            FixedFixedCase beam = FixtureLoader.LoadAs<Fixture2Data>(Fixture2).FixedFixed;
            SolverInput input = InputForLine(FixtureLoader.Load(Fixture2), beam.LineId);

            StaticSolveResult result = new StaticSolver().Solve(input, beam.Loads);

            AssertAllFinite(result);
            double span = Distance(input, beam.FirstFixedNodeId, beam.SecondFixedNodeId);
            double load = Assert.Single(beam.Loads).Force.Z;
            Assert.Equal(beam.ExpectedMoment, load * span / 8.0, ForceTolerance);
            Assert.Equal(beam.ExpectedSupportShear, load / 2.0, ForceTolerance);

            Member first = MemberOf(input, beam.FirstMemberId);
            Member second = MemberOf(input, beam.SecondMemberId);
            Assert.Equal((beam.FirstFixedNodeId, beam.MidspanNodeId), (first.NodeA, first.NodeB));
            Assert.Equal((beam.MidspanNodeId, beam.SecondFixedNodeId), (second.NodeA, second.NodeB));

            MemberForce firstForce = ForceOf(result, beam.FirstMemberId);
            MemberForce secondForce = ForceOf(result, beam.SecondMemberId);
            Assert.Equal(beam.ExpectedMoment, Math.Abs(firstForce.MomentLocalYAtA), ForceTolerance);
            Assert.Equal(beam.ExpectedMoment, Math.Abs(firstForce.MomentLocalYAtB), ForceTolerance);
            Assert.Equal(beam.ExpectedMoment, Math.Abs(secondForce.MomentLocalYAtA), ForceTolerance);
            Assert.Equal(beam.ExpectedMoment, Math.Abs(secondForce.MomentLocalYAtB), ForceTolerance);
            Assert.Equal(beam.ExpectedSupportShear, Math.Abs(firstForce.ShearLocalZAtA), ForceTolerance);
            Assert.Equal(beam.ExpectedSupportShear, Math.Abs(secondForce.ShearLocalZAtB), ForceTolerance);
            AssertOtherBendingPlaneIsZero(firstForce);
            AssertOtherBendingPlaneIsZero(secondForce);
        }

        [Fact]
        public void FrameFixture3_UnbracedRectangle_IsFiniteNotAMechanism()
        {
            BridgeFixture rectangle = FixtureLoader.Load(Fixture3);
            Fixture3Data data = FixtureLoader.LoadAs<Fixture3Data>(Fixture3);
            SolverInput input = rectangle.ToSolverInput(UnitScale);

            StaticSolveResult result = new StaticSolver().Solve(input, data.Loads);

            AssertAllFinite(result);
            Assert.Equal(24, DofValues(result).Count());
            double magnitude = result.Displacements[IndexOf(input, data.LoadedNodeId)].Length;
            Assert.True(double.IsFinite(magnitude), "Loaded-node translation must be finite.");
            Assert.True(magnitude > 0.0, "Loaded-node translation must be greater than 0.");
        }

        [Fact]
        public void FrameFixture3_Diagonal_IsStrictlyStiffer()
        {
            BridgeFixture rectangle = FixtureLoader.Load(Fixture3);
            Fixture3Data data = FixtureLoader.LoadAs<Fixture3Data>(Fixture3);
            SolverInput unbraced = rectangle.ToSolverInput(UnitScale);
            SolverInput braced = rectangle.ToSolverInput(UnitScale);
            braced.Members = rectangle.Members.Concat(data.Brace.Members).ToArray();

            Member diagonal = Assert.Single(data.Brace.Members);
            Assert.Equal(new Vec3(0, 0, 0), PositionOf(braced, diagonal.NodeA));
            Assert.Equal(new Vec3(2, 1, 0), PositionOf(braced, diagonal.NodeB));

            var solver = new StaticSolver();
            StaticSolveResult unbracedResult = solver.Solve(unbraced, data.Loads);
            StaticSolveResult bracedResult = solver.Solve(braced, data.Loads);

            AssertAllFinite(unbracedResult);
            AssertAllFinite(bracedResult);
            Assert.Equal(24, DofValues(bracedResult).Count());
            double magUnbraced = unbracedResult.Displacements[IndexOf(unbraced, data.LoadedNodeId)].Length;
            double magDiagonal = bracedResult.Displacements[IndexOf(braced, data.LoadedNodeId)].Length;
            Assert.True(magDiagonal < magUnbraced, "The braced rectangle must move less than the unbraced one.");
            Assert.True(
                (magUnbraced - magDiagonal) / magUnbraced >= data.MinimumRelativeStiffening,
                "The diagonal must cut the loaded-node translation by at least " + data.MinimumRelativeStiffening + ".");
        }

        [Fact]
        public void StaticSolve_AllFixedNode_HasZeroDisplacement()
        {
            var input = new SolverInput
            {
                Nodes = new[]
                {
                    new Node { Id = 1, Position = new Vec3(0, 0, 0), Fixed = true },
                    new Node { Id = 2, Position = new Vec3(2, 0, 0), Fixed = true },
                },
                Members = new[] { new Member { Id = 1, NodeA = 1, NodeB = 2, SectionId = 1, LineId = 1 } },
                Sections = new[] { TestSection(1) },
                ScaleFactor = UnitScale,
            };
            var loads = new[] { new NodalLoad { NodeId = 2, Force = new Vec3(0, -5000, 0) } };

            StaticSolveResult result = new StaticSolver().Solve(input, loads);

            AssertAllFinite(result);
            Assert.Equal(12, DofValues(result).Count());
            Assert.All(DofValues(result), value => Assert.Equal(0.0, value, DisplacementTolerance));
        }

        [Fact]
        public void StaticSolve_RejectsNaNLoad()
        {
            SolverInput input = Cantilever(fixedId: 1, tipId: 2);
            var solver = new StaticSolver();

            // Vec3 cannot hold NaN, so a NaN component is rejected while the load is built.
            Assert.Throws<ArgumentException>(() => solver.Solve(input, new[] { new NodalLoad { NodeId = 2, Force = new Vec3(double.NaN, 0, 0) } }));
            Assert.Throws<ArgumentException>(() => solver.Solve(input, new[] { new NodalLoad { NodeId = 2, Moment = new Vec3(0, 0, double.NaN) } }));

            var infiniteForce = new NodalLoad { NodeId = 2, Force = new Vec3(0, 0, double.PositiveInfinity) };
            var infiniteMoment = new NodalLoad { NodeId = 2, Moment = new Vec3(double.PositiveInfinity, 0, 0) };
            Assert.Throws<ArgumentException>(() => solver.Solve(input, new[] { infiniteForce }));
            Assert.Throws<ArgumentException>(() => solver.Solve(input, new[] { infiniteMoment }));
        }

        [Fact]
        public void FrameElement_LocalAxes_FollowIssueRule()
        {
            AssertAxes(FrameElement.LocalAxes(new Vec3(1, 1, 1), new Vec3(3, 1, 1)), Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ);
            AssertAxes(FrameElement.LocalAxes(new Vec3(0, 0, 0), new Vec3(0, 2, 0)), Vec3.UnitY, -Vec3.UnitX, Vec3.UnitZ);
            AssertAxes(FrameElement.LocalAxes(new Vec3(0, 0, 0), new Vec3(0, 0, 2)), Vec3.UnitZ, Vec3.UnitY, -Vec3.UnitX);
            AssertAxes(FrameElement.LocalAxes(new Vec3(0, 0, 2), new Vec3(0, 0, 0)), -Vec3.UnitZ, Vec3.UnitY, Vec3.UnitX);

            Mat3 oblique = FrameElement.LocalAxes(new Vec3(1, 2, 3), new Vec3(4, 0, 5));
            Vec3 x = new Vec3(3, -2, 2).Normalized();
            Vec3 y = Vec3.Cross(Vec3.UnitZ, x).Normalized();
            AssertAxes(oblique, x, y, Vec3.Cross(x, y));
            Assert.Equal(1.0, oblique.Determinant, 12);
        }

        [Fact]
        public void FrameElement_WorldStiffness_AlongX_HasIssueCoefficients()
        {
            var section = new SectionProperties { Id = 7, E = 2.0e11, G = 8.0e10, A = 2.0e-3, Iy = 3.0e-6, Iz = 5.0e-6, J = 1.5e-6 };
            const double L = 2.0;

            double[,] k = FrameElement.WorldStiffness(new Vec3(1, 0, 0), new Vec3(1 + L, 0, 0), section);

            Assert.Equal(12, k.GetLength(0));
            Assert.Equal(12, k.GetLength(1));
            for (int row = 0; row < 12; row++)
            {
                for (int column = 0; column < 12; column++)
                {
                    Assert.Equal(k[row, column], k[column, row]);
                }
            }

            double e = section.E;
            AssertRelative(e * section.A / L, k[0, 0]);
            AssertRelative(-e * section.A / L, k[0, 6]);
            AssertRelative(section.G * section.J / L, k[3, 3]);
            AssertRelative(-section.G * section.J / L, k[3, 9]);

            // Deflection along local y (uy) with rotation rz bends about local z and uses Iz.
            AssertRelative(12 * e * section.Iz / (L * L * L), k[1, 1]);
            AssertRelative(6 * e * section.Iz / (L * L), k[1, 5]);
            AssertRelative(4 * e * section.Iz / L, k[5, 5]);
            AssertRelative(2 * e * section.Iz / L, k[5, 11]);

            // Deflection along local z (uz) with rotation ry bends about local y and uses Iy.
            AssertRelative(12 * e * section.Iy / (L * L * L), k[2, 2]);
            AssertRelative(-6 * e * section.Iy / (L * L), k[2, 4]);
            AssertRelative(4 * e * section.Iy / L, k[4, 4]);
            AssertRelative(2 * e * section.Iy / L, k[4, 10]);
            Assert.Equal(0.0, k[1, 2]);
            Assert.Equal(0.0, k[0, 1]);
        }

        [Fact]
        public void StaticSolve_SparseIdsAndNodeOrder_FollowNodesArray()
        {
            SolverInput input = Cantilever(fixedId: 40, tipId: 7);
            input.Nodes = input.Nodes.Reverse().ToArray();
            var loads = new[] { new NodalLoad { NodeId = 7, Force = new Vec3(0, 0, 16000) } };

            StaticSolveResult result = new StaticSolver().Solve(input, loads);

            AssertAllFinite(result);
            Assert.Equal(7, input.Nodes[0].Id);
            Assert.Equal(0.09, result.Displacements[0].Z, DisplacementTolerance);
            Assert.Equal(Vec3.Zero, result.Displacements[1]);
            Assert.Equal(Vec3.Zero, result.Rotations[1]);
        }

        [Fact]
        public void StaticSolve_FreeNodeWithoutMembers_IsAMechanism()
        {
            SolverInput input = Cantilever(fixedId: 1, tipId: 2);
            input.Nodes = input.Nodes.Concat(new[] { new Node { Id = 3, Position = new Vec3(0, 5, 0) } }).ToArray();

            Assert.Throws<InvalidOperationException>(() => new StaticSolver().Solve(input, Array.Empty<NodalLoad>()));
        }

        private static SolverInput Cantilever(int fixedId, int tipId)
        {
            return new SolverInput
            {
                Nodes = new[]
                {
                    new Node { Id = fixedId, Position = new Vec3(0, 0, 0), Fixed = true },
                    new Node { Id = tipId, Position = new Vec3(3, 0, 0) },
                },
                Members = new[] { new Member { Id = 1, NodeA = fixedId, NodeB = tipId, SectionId = 1, LineId = 1 } },
                Sections = new[] { new SectionProperties { Id = 1, E = 2.0e11, G = 1.0e11, A = 1.0e-3, Iy = 8.0e-6, Iz = 8.0e-6, J = 1.0e-6 } },
                ScaleFactor = UnitScale,
            };
        }

        private static SectionProperties TestSection(int id)
        {
            return new SectionProperties { Id = id, E = 2.0e11, G = 1.0e11, A = 1.0e-3, Iy = 1.0e-5, Iz = 1.0e-5, J = 1.0e-6 };
        }

        private static SolverInput InputForLine(BridgeFixture fixture, int lineId)
        {
            int[] memberIds = fixture.Lines.Single(line => line.Id == lineId).MemberIds;
            Member[] members = memberIds.Select(id => fixture.Members.Single(m => m.Id == id)).ToArray();
            var nodeIds = new HashSet<int>(members.SelectMany(m => new[] { m.NodeA, m.NodeB }));
            return new SolverInput
            {
                Nodes = fixture.Nodes.Where(n => nodeIds.Contains(n.Id)).ToArray(),
                Members = members,
                Sections = fixture.Sections,
                ScaleFactor = UnitScale,
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

        private static double Distance(SolverInput input, int nodeA, int nodeB)
        {
            return Vec3.Distance(PositionOf(input, nodeA), PositionOf(input, nodeB));
        }

        private static Member MemberOf(SolverInput input, int memberId)
        {
            return input.Members.Single(m => m.Id == memberId);
        }

        private static SectionProperties SectionOf(SolverInput input, int memberId)
        {
            int sectionId = MemberOf(input, memberId).SectionId;
            return input.Sections.Single(s => s.Id == sectionId);
        }

        private static MemberForce ForceOf(StaticSolveResult result, int memberId)
        {
            return result.MemberForces.Single(f => f.MemberId == memberId);
        }

        private static IEnumerable<double> DofValues(StaticSolveResult result)
        {
            for (int i = 0; i < result.Displacements.Length; i++)
            {
                Vec3 d = result.Displacements[i];
                Vec3 r = result.Rotations[i];
                yield return d.X;
                yield return d.Y;
                yield return d.Z;
                yield return r.X;
                yield return r.Y;
                yield return r.Z;
            }
        }

        private static void AssertAllFinite(StaticSolveResult result)
        {
            Assert.Equal(result.Displacements.Length, result.Rotations.Length);
            Assert.All(DofValues(result), value => Assert.True(double.IsFinite(value), "Displacement or rotation is not finite."));
            Assert.All(result.MemberForces, force =>
            {
                double[] values =
                {
                    force.Axial, force.ShearLocalYAtA, force.ShearLocalZAtA, force.MomentLocalYAtA, force.MomentLocalZAtA,
                    force.ShearLocalYAtB, force.ShearLocalZAtB, force.MomentLocalYAtB, force.MomentLocalZAtB, force.Utilization,
                };
                Assert.All(values, value => Assert.True(double.IsFinite(value), "Member " + force.MemberId + " has a non-finite force."));
            });
        }

        private static void AssertOtherBendingPlaneIsZero(MemberForce force)
        {
            Assert.Equal(0.0, force.ShearLocalYAtA, ForceTolerance);
            Assert.Equal(0.0, force.ShearLocalYAtB, ForceTolerance);
            Assert.Equal(0.0, force.MomentLocalZAtA, ForceTolerance);
            Assert.Equal(0.0, force.MomentLocalZAtB, ForceTolerance);
        }

        private static void AssertAxes(Mat3 axes, Vec3 x, Vec3 y, Vec3 z)
        {
            AssertClose(x, axes.Row(0));
            AssertClose(y, axes.Row(1));
            AssertClose(z, axes.Row(2));
        }

        private static void AssertClose(Vec3 expected, Vec3 actual)
        {
            Assert.Equal(expected.X, actual.X, 1e-15);
            Assert.Equal(expected.Y, actual.Y, 1e-15);
            Assert.Equal(expected.Z, actual.Z, 1e-15);
        }

        private static void AssertRelative(double expected, double actual)
        {
            Assert.True(Math.Abs(actual - expected) <= 1e-12 * Math.Abs(expected), "Expected " + expected + " but got " + actual + ".");
        }

        private sealed class Fixture2Data
        {
            public CantileverCase Cantilever { get; set; }

            public FixedFixedCase FixedFixed { get; set; }
        }

        private sealed class CantileverCase
        {
            public int LineId { get; set; }

            public int MemberId { get; set; }

            public int FixedNodeId { get; set; }

            public int TipNodeId { get; set; }

            public NodalLoad[] Loads { get; set; } = Array.Empty<NodalLoad>();

            public Vec3 ExpectedTipDisplacement { get; set; }

            public double ExpectedFixedEndMoment { get; set; }

            public double ExpectedFixedEndShear { get; set; }

            public double ExpectedAxial { get; set; }

            public double ExpectedFreeEndMoment { get; set; }
        }

        private sealed class FixedFixedCase
        {
            public int LineId { get; set; }

            public int FirstMemberId { get; set; }

            public int SecondMemberId { get; set; }

            public int FirstFixedNodeId { get; set; }

            public int MidspanNodeId { get; set; }

            public int SecondFixedNodeId { get; set; }

            public NodalLoad[] Loads { get; set; } = Array.Empty<NodalLoad>();

            public double ExpectedMidspanDisplacementZ { get; set; }

            public double ExpectedMoment { get; set; }

            public double ExpectedSupportShear { get; set; }
        }

        private sealed class Fixture3Data
        {
            public NodalLoad[] Loads { get; set; } = Array.Empty<NodalLoad>();

            public int LoadedNodeId { get; set; }

            public BridgeFixture Brace { get; set; }

            public double MinimumRelativeStiffening { get; set; }
        }
    }
}
