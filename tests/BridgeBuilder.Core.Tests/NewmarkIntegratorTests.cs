using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class NewmarkIntegratorTests
    {
        private const string Fixture6 = "fixture-06-progressive-failure.json";

        // Issue W2-01 single-degree-of-freedom scenario. Do not change.
        private const double SdofMass = 1.0;
        private const double SdofStiffness = 100.0;
        private const double SdofInitialDisplacement = 0.1;
        private const double SdofInitialVelocity = 0.0;
        private const double SdofInitialAcceleration = -10.0;
        private const double WorkedExampleTolerance = 1e-9;

        // Absolute tolerances: meters or radians, and newtons or newton-meters relative to the value.
        private const double DisplacementTolerance = 1e-9;
        private const double RelativeForceTolerance = 1e-6;

        [Fact]
        public void Newmark_FirstStep_MatchesWorkedExample()
        {
            const double dt = 0.01;
            const double force = 0.0;
            var start = new SingleDofState(SdofInitialDisplacement, SdofInitialVelocity, SdofInitialAcceleration);

            Assert.Equal(0.25, Constants.Dynamics.NewmarkBeta);
            Assert.Equal(0.5, Constants.Dynamics.NewmarkGamma);
            var c = new NewmarkCoefficients(dt);
            Assert.Equal(40000.0, c.A0, WorkedExampleTolerance);
            Assert.Equal(200.0, c.A1, WorkedExampleTolerance);
            Assert.Equal(400.0, c.A2, WorkedExampleTolerance);
            Assert.Equal(1.0, c.A3, WorkedExampleTolerance);
            Assert.Equal(1.0, c.A4, WorkedExampleTolerance);
            Assert.Equal(0.0, c.A5, WorkedExampleTolerance);

            double damping = NewmarkIntegrator.DampingCoefficient(SdofMass, SdofStiffness, 0.0);
            Assert.Equal(0.0, damping);
            double effectiveStiffness = SdofStiffness + c.A0 * SdofMass + c.A1 * damping;
            double effectiveLoad = force
                + SdofMass * (c.A0 * start.Displacement + c.A2 * start.Velocity + c.A3 * start.Acceleration)
                + damping * (c.A1 * start.Displacement + c.A4 * start.Velocity + c.A5 * start.Acceleration);
            Assert.Equal(40100.0, effectiveStiffness, WorkedExampleTolerance);
            Assert.Equal(3990.0, effectiveLoad, WorkedExampleTolerance);

            SingleDofState next = NewmarkIntegrator.StepSingleDof(SdofMass, SdofStiffness, 0.0, force, start, dt);

            AssertFinite(next);
            Assert.Equal(0.09950124688279302, next.Displacement, WorkedExampleTolerance);
            Assert.Equal(-0.09975062344139696, next.Velocity, WorkedExampleTolerance);
            Assert.Equal(-9.950124688279391, next.Acceleration, WorkedExampleTolerance);
        }

        [Fact]
        public void Newmark_LargeTimestep_StaysBounded()
        {
            const double dt = 1.0;
            const int steps = 20;
            const double bound = 0.2;
            var state = new SingleDofState(SdofInitialDisplacement, SdofInitialVelocity, SdofInitialAcceleration);

            for (int step = 1; step <= steps; step++)
            {
                state = NewmarkIntegrator.StepSingleDof(SdofMass, SdofStiffness, 0.0, 0.0, state, dt);

                AssertFinite(state);
                Assert.True(Math.Abs(state.Displacement) <= bound, "Step " + step + " displacement is " + state.Displacement + ".");
            }
        }

        [Fact]
        public void Newmark_DampingReducesAmplitude()
        {
            const double zeta = 0.05;
            const double dt = 0.01;
            const int steps = 500;
            double damping = NewmarkIntegrator.DampingCoefficient(SdofMass, SdofStiffness, zeta);
            Assert.Equal(1.0, damping, 1e-12);

            double initialAcceleration = -(damping * SdofInitialVelocity + SdofStiffness * SdofInitialDisplacement) / SdofMass;
            var state = new SingleDofState(SdofInitialDisplacement, SdofInitialVelocity, initialAcceleration);
            AssertFinite(state);
            var displacements = new List<double> { state.Displacement };
            for (int step = 1; step <= steps; step++)
            {
                state = NewmarkIntegrator.StepSingleDof(SdofMass, SdofStiffness, zeta, 0.0, state, dt);
                AssertFinite(state);
                displacements.Add(state.Displacement);
            }

            double earlyPeak = PeakAbsolute(displacements, 0, 100);
            double latePeak = PeakAbsolute(displacements, 400, 500);
            Assert.True(latePeak < earlyPeak, "Late peak " + latePeak + " is not below early peak " + earlyPeak + ".");
        }

        [Fact]
        public void Progressive_RemovingMember_RedistributesAndStaysFinite()
        {
            Fixture6Data data = FixtureLoader.LoadAs<Fixture6Data>(Fixture6);
            SolverInput input = Fixture6Input(data);
            Assert.Equal(2, input.Members.Length);
            Member weak = Assert.Single(input.Members, m => m.Id == data.FirstFailingMemberId);
            Member other = Assert.Single(input.Members, m => m.Id != data.FirstFailingMemberId);
            Assert.NotEqual(weak.SectionId, other.SectionId);
            Assert.True(data.Dt > 0.0 && data.StepCount > 1 && data.Loads.Length > 0, "Fixture 6 must define dt, steps, and a load.");

            var counter = new FactorCounter();
            var integrator = new NewmarkIntegrator(counter.CreateSolver);
            integrator.Reset(input);

            int failureStep = 0;
            MemberForce previousOther = null;
            double peakOtherBefore = 0.0;
            double peakOtherAfter = 0.0;
            for (int step = 1; step <= data.StepCount; step++)
            {
                int factorsBefore = counter.FactorCalls;
                StepOutput output = integrator.Step(data.Loads, data.Dt);
                int factors = counter.FactorCalls - factorsBefore;

                AssertFinite(output, input.Nodes.Length);
                AssertUtilizationsComeFromMemberCapacity(input, output);
                MemberForce otherForce = Assert.Single(output.MemberForces, f => f.MemberId == other.Id);
                Assert.True(otherForce.Utilization < 1.0, "Member " + other.Id + " reached utilization " + otherForce.Utilization + " on step " + step + ".");

                if (output.FailedMemberIds.Length > 0)
                {
                    Assert.True(failureStep == 0, "A second failure on step " + step + ".");
                    Assert.True(previousOther != null, "Member " + weak.Id + " failed on the first step, before any load was redistributed.");
                    Assert.Equal(new[] { weak.Id }, output.FailedMemberIds);
                    Assert.DoesNotContain(output.MemberForces, f => f.MemberId == weak.Id);
                    Assert.Equal(1, factors);
                    double change = LargestComponentChange(previousOther, otherForce);
                    Assert.True(change > RelativeForceTolerance * LargestComponent(previousOther), "Member " + other.Id + " force did not change on the failure step.");
                    failureStep = step;
                }
                else if (failureStep > 0)
                {
                    Assert.DoesNotContain(output.MemberForces, f => f.MemberId == weak.Id);
                    Assert.Equal(0, factors);
                }
                else
                {
                    MemberForce weakForce = Assert.Single(output.MemberForces, f => f.MemberId == weak.Id);
                    Assert.True(weakForce.Utilization < 1.0);
                    if (step > 1)
                    {
                        Assert.Equal(0, factors);
                    }
                }

                if (failureStep == 0)
                {
                    peakOtherBefore = Math.Max(peakOtherBefore, otherForce.Utilization);
                }
                else
                {
                    peakOtherAfter = Math.Max(peakOtherAfter, otherForce.Utilization);
                }

                previousOther = otherForce;
            }

            Assert.True(failureStep > 1, "Member " + weak.Id + " never failed.");
            Assert.True(peakOtherAfter > peakOtherBefore, "Member " + other.Id + " did not pick up the load of the failed member.");
            Assert.Equal(2, input.Members.Length);
        }

        [Fact]
        public void Dynamics_ConstantsHaveIssueValues()
        {
            Assert.Equal(0.25, Constants.Dynamics.NewmarkBeta);
            Assert.Equal(0.5, Constants.Dynamics.NewmarkGamma);
            Assert.Equal(0.02, Constants.Dynamics.DampingRatio);
            Assert.Equal(60.0, Constants.Dynamics.SolverRateHz);
            Assert.Equal(1.0 / 60.0, NewmarkIntegrator.DefaultTimestep);
        }

        [Fact]
        public void Newmark_EquilibriumStart_MatchesStaticSolverAndHolds()
        {
            SolverInput input = TripodFrame();
            NodalLoad[] loads = TripodLoads();
            StaticSolveResult expected = new StaticSolver().Solve(input, loads);

            var integrator = new NewmarkIntegrator();
            integrator.Reset(input, loads);
            for (int step = 1; step <= 5; step++)
            {
                StepOutput output = integrator.Step(loads, NewmarkIntegrator.DefaultTimestep);

                AssertFinite(output, input.Nodes.Length);
                Assert.Empty(output.FailedMemberIds);
                for (int node = 0; node < input.Nodes.Length; node++)
                {
                    AssertClose(expected.Displacements[node], output.Displacements[node]);
                    AssertClose(expected.Rotations[node], output.Rotations[node]);
                }

                Assert.Equal(input.Members.Select(m => m.Id), output.MemberForces.Select(f => f.MemberId));
                for (int m = 0; m < input.Members.Length; m++)
                {
                    AssertForceClose(expected.MemberForces[m], output.MemberForces[m]);
                    Assert.True(output.MemberForces[m].Utilization > 0.0 && output.MemberForces[m].Utilization < 1.0);
                }
            }

            Assert.True(expected.Displacements.Any(d => d.Length > 1e-4), "The loads must move the frame measurably.");
        }

        [Fact]
        public void Newmark_FundamentalFrequency_MatchesFixedFixedBeam()
        {
            Fixture6Data data = FixtureLoader.LoadAs<Fixture6Data>(Fixture6);
            SolverInput input = Fixture6Input(data);
            SectionProperties first = input.Sections[0];
            SectionProperties second = input.Sections[1];
            Assert.Equal((first.E, first.A, first.Iy, first.Iz, first.Density), (second.E, second.A, second.Iy, second.Iz, second.Density));

            Node joint = Assert.Single(input.Nodes, n => !n.Fixed);
            Node[] supports = input.Nodes.Where(n => n.Fixed).ToArray();
            double span = Vec3.Distance(supports[0].Position, supports[1].Position);
            double memberLength = Vec3.Distance(supports[0].Position, joint.Position);
            Assert.Equal(span / 2.0, memberLength, 1e-12);

            // The joint carries half of each member's mass; its deflection under a central load is P * S^3 / (192 * E * I).
            double jointMass = first.Density * first.A * memberLength;
            double expected = Math.Sqrt(192.0 * first.E * Math.Min(first.Iy, first.Iz) / (span * span * span * jointMass));

            var integrator = new NewmarkIntegrator();
            integrator.Reset(input);

            Assert.Equal(expected, integrator.FundamentalFrequency, expected * 1e-9);
        }

        [Fact]
        public void Newmark_RefactorsOnlyAtRunStartOnFailureOrNewSettings()
        {
            SolverInput input = TripodFrame();
            NodalLoad[] loads = TripodLoads();
            var counter = new FactorCounter();
            var integrator = new NewmarkIntegrator(counter.CreateSolver);
            const double dt = 0.02;

            integrator.Reset(input);
            Assert.Equal(1, counter.FactorCalls);
            Assert.Equal(1, counter.SolverCount);

            integrator.Step(loads, dt);
            Assert.Equal(2, counter.FactorCalls);
            for (int step = 0; step < 10; step++)
            {
                AssertFinite(integrator.Step(loads, dt), input.Nodes.Length);
            }

            Assert.Equal(2, counter.FactorCalls);
            integrator.Step(loads, dt / 2.0);
            Assert.Equal(3, counter.FactorCalls);
            integrator.Step(loads, dt / 2.0, 0.1);
            Assert.Equal(4, counter.FactorCalls);
            integrator.Step(loads, dt / 2.0, 0.1);
            Assert.Equal(4, counter.FactorCalls);
            Assert.Equal(counter.FactorCalls, counter.SolverCount);
            Assert.Equal(3 * Constants.Solver.DofPerNode, counter.LargestOrder);
        }

        [Fact]
        public void Newmark_RejectsInvalidArguments()
        {
            NodalLoad[] none = Array.Empty<NodalLoad>();
            var start = new SingleDofState(SdofInitialDisplacement, SdofInitialVelocity, SdofInitialAcceleration);
            Assert.Throws<ArgumentNullException>(() => new NewmarkIntegrator(null));
            Assert.Throws<InvalidOperationException>(() => new NewmarkIntegrator().Step(none, 0.01));

            var integrator = new NewmarkIntegrator();
            Assert.Throws<ArgumentNullException>(() => integrator.Reset(null));
            Assert.Throws<ArgumentNullException>(() => integrator.Reset(TripodFrame(), null));
            integrator.Reset(TripodFrame());
            foreach (double dt in new[] { 0.0, -0.01, double.NaN, double.PositiveInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => integrator.Step(none, dt));
                Assert.Throws<ArgumentOutOfRangeException>(() => new NewmarkCoefficients(dt));
                Assert.Throws<ArgumentOutOfRangeException>(() => NewmarkIntegrator.StepSingleDof(SdofMass, SdofStiffness, 0.0, 0.0, start, dt));
            }

            foreach (double ratio in new[] { -0.01, double.NaN, double.PositiveInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => integrator.Step(none, 0.01, ratio));
                Assert.Throws<ArgumentOutOfRangeException>(() => NewmarkIntegrator.DampingCoefficient(SdofMass, SdofStiffness, ratio));
            }

            Assert.Throws<ArgumentNullException>(() => integrator.Step(null, 0.01));
            Assert.Throws<ArgumentException>(() => integrator.Step(new NodalLoad[] { null }, 0.01));
            Assert.Throws<ArgumentException>(() => integrator.Step(new[] { new NodalLoad { NodeId = 999, Force = new Vec3(0, -1, 0) } }, 0.01));
            Assert.Throws<ArgumentException>(() => integrator.Step(new[] { new NodalLoad { NodeId = 9, Force = new Vec3(double.PositiveInfinity, 0, 0) } }, 0.01));
            AssertFinite(integrator.Step(none, 0.01), TripodFrame().Nodes.Length);

            Assert.Throws<ArgumentOutOfRangeException>(() => NewmarkIntegrator.StepSingleDof(0.0, SdofStiffness, 0.0, 0.0, start, 0.01));
            Assert.Throws<ArgumentOutOfRangeException>(() => NewmarkIntegrator.StepSingleDof(SdofMass, -1.0, 0.0, 0.0, start, 0.01));
            Assert.Throws<ArgumentOutOfRangeException>(() => NewmarkIntegrator.StepSingleDof(SdofMass, SdofStiffness, 0.0, double.NaN, start, 0.01));
            Assert.Throws<ArgumentOutOfRangeException>(() => NewmarkIntegrator.StepSingleDof(SdofMass, SdofStiffness, 0.0, 0.0, new SingleDofState(double.NaN, 0, 0), 0.01));

            SolverInput massless = TripodFrame();
            massless.Sections = new[] { TripodSection(density: 0.0) };
            Assert.Throws<ArgumentException>(() => new NewmarkIntegrator().Reset(massless));
            SolverInput missingSection = TripodFrame();
            missingSection.Sections = Array.Empty<SectionProperties>();
            Assert.Throws<ArgumentException>(() => new NewmarkIntegrator().Reset(missingSection));
        }

        private static SolverInput Fixture6Input(Fixture6Data data)
        {
            return RoomBridgeAdapter.ToBridge(FixtureLoader.Load(Fixture6).ToSolverInput(data.ScaleFactor));
        }

        /// <summary>A small 3D frame with sparse ids, nodes out of order, and three fixed feet, in bridge-world meters.</summary>
        private static SolverInput TripodFrame()
        {
            return new SolverInput
            {
                Nodes = new[]
                {
                    new Node { Id = 9, Position = new Vec3(2, 3, 2) },
                    new Node { Id = 40, Position = new Vec3(0, 0, 0), Fixed = true },
                    new Node { Id = 12, Position = new Vec3(4, 3, 0) },
                    new Node { Id = 31, Position = new Vec3(2, 0, 2), Fixed = true },
                    new Node { Id = 7, Position = new Vec3(0, 3, 0) },
                    new Node { Id = 25, Position = new Vec3(4, 0, 0), Fixed = true },
                },
                Members = new[]
                {
                    new Member { Id = 1, NodeA = 40, NodeB = 7, SectionId = 5, LineId = 1 },
                    new Member { Id = 2, NodeA = 7, NodeB = 12, SectionId = 5, LineId = 2 },
                    new Member { Id = 3, NodeA = 25, NodeB = 12, SectionId = 5, LineId = 3 },
                    new Member { Id = 4, NodeA = 7, NodeB = 9, SectionId = 5, LineId = 4 },
                    new Member { Id = 5, NodeA = 9, NodeB = 12, SectionId = 5, LineId = 5 },
                    new Member { Id = 6, NodeA = 31, NodeB = 9, SectionId = 5, LineId = 6 },
                },
                Sections = new[] { TripodSection(density: 7850.0) },
                ScaleFactor = 1.0,
            };
        }

        private static SectionProperties TripodSection(double density)
        {
            return new SectionProperties
            {
                Id = 5, E = 2.0e11, G = 7.692307692307693e10, A = 2.0e-3, Iy = 2.0e-5, Iz = 3.0e-5, J = 1.0e-5,
                YieldStrength = 3.55e8, Density = density, Cy = 0.1, Cz = 0.12,
            };
        }

        private static NodalLoad[] TripodLoads()
        {
            return new[]
            {
                new NodalLoad { NodeId = 9, Force = new Vec3(1000, -20000, 500) },
                new NodalLoad { NodeId = 12, Force = new Vec3(0, -5000, 0), Moment = new Vec3(0, 0, 800) },
            };
        }

        private static double PeakAbsolute(List<double> values, int first, int last)
        {
            double peak = 0.0;
            for (int i = first; i <= last; i++)
            {
                peak = Math.Max(peak, Math.Abs(values[i]));
            }

            return peak;
        }

        private static double[] Components(MemberForce force)
        {
            return new[]
            {
                force.Axial, force.ShearLocalYAtA, force.ShearLocalZAtA, force.MomentLocalYAtA, force.MomentLocalZAtA,
                force.ShearLocalYAtB, force.ShearLocalZAtB, force.MomentLocalYAtB, force.MomentLocalZAtB,
            };
        }

        private static double LargestComponent(MemberForce force)
        {
            return Components(force).Max(Math.Abs);
        }

        private static double LargestComponentChange(MemberForce before, MemberForce after)
        {
            double[] a = Components(before);
            double[] b = Components(after);
            return a.Zip(b, (x, y) => Math.Abs(x - y)).Max();
        }

        private static void AssertUtilizationsComeFromMemberCapacity(SolverInput input, StepOutput output)
        {
            foreach (MemberForce force in output.MemberForces)
            {
                Member member = input.Members.Single(m => m.Id == force.MemberId);
                SectionProperties section = input.Sections.Single(s => s.Id == member.SectionId);
                double length = Vec3.Distance(
                    input.Nodes.Single(n => n.Id == member.NodeA).Position,
                    input.Nodes.Single(n => n.Id == member.NodeB).Position);
                Assert.Equal(MemberCapacity.Evaluate(force, section, length).Utilization, force.Utilization);
            }
        }

        private static void AssertFinite(SingleDofState state)
        {
            Assert.True(double.IsFinite(state.Displacement), "Displacement is " + state.Displacement + ".");
            Assert.True(double.IsFinite(state.Velocity), "Velocity is " + state.Velocity + ".");
            Assert.True(double.IsFinite(state.Acceleration), "Acceleration is " + state.Acceleration + ".");
        }

        private static void AssertFinite(StepOutput output, int nodeCount)
        {
            Assert.Equal(nodeCount, output.Displacements.Length);
            Assert.Equal(nodeCount, output.Rotations.Length);
            foreach (Vec3 v in output.Displacements.Concat(output.Rotations))
            {
                Assert.True(double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z), "Displacement or rotation " + v + " is not finite.");
            }

            foreach (MemberForce force in output.MemberForces)
            {
                Assert.All(Components(force), value => Assert.True(double.IsFinite(value), "Member " + force.MemberId + " has a non-finite force."));
                Assert.True(double.IsFinite(force.Utilization), "Member " + force.MemberId + " has a non-finite utilization.");
            }
        }

        private static void AssertClose(Vec3 expected, Vec3 actual)
        {
            Assert.Equal(expected.X, actual.X, DisplacementTolerance);
            Assert.Equal(expected.Y, actual.Y, DisplacementTolerance);
            Assert.Equal(expected.Z, actual.Z, DisplacementTolerance);
        }

        private static void AssertForceClose(MemberForce expected, MemberForce actual)
        {
            Assert.Equal(expected.MemberId, actual.MemberId);
            double tolerance = RelativeForceTolerance * Math.Max(1.0, LargestComponent(expected));
            double[] a = Components(expected);
            double[] b = Components(actual);
            for (int i = 0; i < a.Length; i++)
            {
                Assert.Equal(a[i], b[i], tolerance);
            }
        }

        private sealed class Fixture6Data
        {
            public double ScaleFactor { get; set; }

            public double Dt { get; set; }

            public int StepCount { get; set; }

            public int FirstFailingMemberId { get; set; }

            public NodalLoad[] Loads { get; set; } = Array.Empty<NodalLoad>();
        }

        /// <summary>Hands out <see cref="BandedMatrix"/> systems and counts <see cref="ILinearSolver.Factor"/> calls across all of them.</summary>
        private sealed class FactorCounter
        {
            public int FactorCalls { get; private set; }

            public int SolverCount { get; private set; }

            public int LargestOrder { get; private set; }

            public ILinearSolver CreateSolver(int order, int halfBandwidth)
            {
                SolverCount++;
                LargestOrder = Math.Max(LargestOrder, order);
                return new CountingSolver(this, new BandedMatrix(order, halfBandwidth));
            }

            private sealed class CountingSolver : ILinearSolver
            {
                private readonly FactorCounter owner;
                private readonly ILinearSolver inner;

                public CountingSolver(FactorCounter owner, ILinearSolver inner)
                {
                    this.owner = owner;
                    this.inner = inner;
                }

                public void Add(int row, int column, double value)
                {
                    inner.Add(row, column, value);
                }

                public void Factor()
                {
                    owner.FactorCalls++;
                    inner.Factor();
                }

                public void Solve(double[] rhs, double[] solution)
                {
                    inner.Solve(rhs, solution);
                }
            }
        }
    }
}
