using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class UnsupportedFallTests
    {
        private const double Gravity = Constants.Loads.Gravity;
        private const double FallTolerance = 1e-9;
        private const int Steps = 30;
        private const int FailStep = 5;
        private const int FailedMemberId = 6;
        private const int IntegratorSteps = 120;

        private static readonly double Dt = NewmarkIntegrator.DefaultTimestep;
        private static readonly int[] TrussNodeIds = { 1, 2, 3, 4, 5 };
        private static readonly int[] TrussMemberIds = { 1, 2, 3, 4, 5, 6, 7 };
        private static readonly int[] PieceNodeIds = { 101, 102, 103, 201, 202, 203 };
        private static readonly int[] PieceMemberIds = { 11, 12, 21, 22, 23 };

        [Fact]
        public void Fall_UnsupportedPieceFallsAtGravity()
        {
            SolverInput input = RoomBridgeAdapter.ToBridge(FloatingFrame(Constants.Scale.RoomToBridge));
            RunConfiguration configuration = Configuration();
            var spy = new SpyDynamicSolver();
            var fall = new UnsupportedFallSolver(spy);
            WheelLoad[] wheels = { new WheelLoad { WheelIndex = 0, NormalLoad = 3000.0, Position = new Vec3(5, 0, 0) } };

            fall.Reset(input, configuration);

            Assert.Equal(1, spy.ResetCount);
            Assert.Same(configuration, spy.ResetConfiguration);
            Assert.Equal(TrussNodeIds, spy.ResetInput.Nodes.Select(n => n.Id));
            Assert.Equal(TrussMemberIds, spy.ResetInput.Members.Select(m => m.Id));
            Assert.Equal(1.0, spy.ResetInput.ScaleFactor);

            for (int n = 1; n <= Steps; n++)
            {
                StepOutput output = fall.Step(wheels, Dt, () => false);
                StepOutput spyOutput = spy.Outputs.Last();

                Assert.Equal(n, spy.StepCount);
                Assert.Same(wheels, spy.ReceivedWheelLoads.Last());
                Assert.Equal(Dt, spy.ReceivedDt.Last());
                AssertRelative(n * Dt, fall.ElapsedSeconds, FallTolerance);

                double t = n * Dt;
                double drop = 0.5 * Gravity * t * t;
                foreach (int id in PieceNodeIds)
                {
                    Vec3 displacement = output.Displacements[IndexOf(input, id)];
                    AssertRelative(-drop, displacement.Y, FallTolerance);
                    Assert.Equal(0.0, displacement.X);
                    Assert.Equal(0.0, displacement.Z);
                    Assert.Equal(Vec3.Zero, output.Rotations[IndexOf(input, id)]);
                }

                for (int j = 0; j < spy.ResetInput.Nodes.Length; j++)
                {
                    int index = IndexOf(input, spy.ResetInput.Nodes[j].Id);
                    Assert.Equal(spyOutput.Displacements[j], output.Displacements[index]);
                    Assert.Equal(spyOutput.Rotations[j], output.Rotations[index]);
                }

                int[] expectedIds = input.Members.Select(m => m.Id).Where(id => n < FailStep || id != FailedMemberId).ToArray();
                Assert.Equal(expectedIds, output.MemberForces.Select(f => f.MemberId));
                foreach (MemberForce force in output.MemberForces)
                {
                    if (PieceMemberIds.Contains(force.MemberId))
                    {
                        AssertZero(force);
                    }
                    else
                    {
                        Assert.Same(spyOutput.MemberForces.Single(f => f.MemberId == force.MemberId), force);
                    }
                }

                Assert.Same(spyOutput.FailedMemberIds, output.FailedMemberIds);
                Assert.Equal(n == FailStep ? new[] { FailedMemberId } : Array.Empty<int>(), output.FailedMemberIds);
                Assert.Same(spyOutput.CarState, output.CarState);
            }
        }

        [Fact]
        public void Fall_ReportsInTheUnitsOfTheInput()
        {
            const double scale = Constants.Scale.RoomToBridge;
            SolverInput room = FloatingFrame(scale);
            room.Nodes = room.Nodes.Append(new Node { Id = 77, Position = new Vec3(0.5, 2, 0) }).ToArray();
            var spy = new SpyDynamicSolver();
            var fall = new UnsupportedFallSolver(spy);

            fall.Reset(room, Configuration());
            StepOutput output = null;
            for (int n = 0; n < Steps; n++)
            {
                output = fall.Step(Array.Empty<WheelLoad>(), Dt, () => false);
            }

            double t = Steps * Dt;
            double bridgeDrop = 0.5 * Gravity * t * t;
            Assert.Equal(scale, spy.ResetInput.ScaleFactor);
            Assert.Equal(new[] { 101, 201, 102, 202, 103, 203, 77 }, fall.Partition.UnsupportedNodeIds);
            foreach (int id in PieceNodeIds)
            {
                Vec3 roomDisplacement = output.Displacements[IndexOf(room, id)];
                AssertRelative(-bridgeDrop / scale, roomDisplacement.Y, FallTolerance);
                AssertRelative(-bridgeDrop, RoomBridgeAdapter.ToBridgeLength(roomDisplacement.Y, scale), FallTolerance);
            }

            Assert.Equal(Vec3.Zero, output.Displacements[IndexOf(room, 77)]);
            Assert.Equal(Vec3.Zero, output.Rotations[IndexOf(room, 77)]);
        }

        [Fact]
        public void Fall_CancelDoesNotAdvance()
        {
            SolverInput input = RoomBridgeAdapter.ToBridge(FloatingFrame(Constants.Scale.RoomToBridge));
            var spy = new SpyDynamicSolver();
            var fall = new UnsupportedFallSolver(spy);
            fall.Reset(input, Configuration());
            int polls = 0;
            Func<bool> cancel = () =>
            {
                polls++;
                return true;
            };

            StepOutput atRest = fall.Step(Array.Empty<WheelLoad>(), Dt, cancel);

            Assert.Equal(1, polls);
            Assert.Equal(0, spy.StepCount);
            Assert.Equal(0.0, fall.ElapsedSeconds);
            Assert.Equal(input.Nodes.Length, atRest.Displacements.Length);
            Assert.All(atRest.Displacements, v => Assert.Equal(Vec3.Zero, v));
            Assert.All(atRest.Rotations, v => Assert.Equal(Vec3.Zero, v));
            Assert.Equal(input.Members.Select(m => m.Id), atRest.MemberForces.Select(f => f.MemberId));
            Assert.All(atRest.MemberForces, AssertZero);
            Assert.Empty(atRest.FailedMemberIds);
            Assert.Null(atRest.CarState);
            Assert.Same(atRest, fall.Step(Array.Empty<WheelLoad>(), Dt, cancel));

            StepOutput last = null;
            for (int n = 0; n < 3; n++)
            {
                last = fall.Step(Array.Empty<WheelLoad>(), Dt, () => false);
            }

            double elapsed = fall.ElapsedSeconds;
            polls = 0;
            StepOutput cancelled = fall.Step(Array.Empty<WheelLoad>(), Dt, cancel);

            Assert.Same(last, cancelled);
            Assert.Equal(1, polls);
            Assert.Equal(3, spy.StepCount);
            Assert.Equal(elapsed, fall.ElapsedSeconds);

            spy.PollsCancel = true;
            int calls = 0;
            StepOutput cancelledInside = fall.Step(Array.Empty<WheelLoad>(), Dt, () => ++calls > 1);

            Assert.Same(last, cancelledInside);
            Assert.Equal(2, calls);
            Assert.Equal(3, spy.StepCount);
            Assert.Equal(elapsed, fall.ElapsedSeconds);

            StepOutput resumed = fall.Step(Array.Empty<WheelLoad>(), Dt, () => false);

            Assert.Equal(4, spy.StepCount);
            double t = 4 * Dt;
            AssertRelative(t, fall.ElapsedSeconds, FallTolerance);
            foreach (int id in PieceNodeIds)
            {
                AssertRelative(-0.5 * Gravity * t * t, resumed.Displacements[IndexOf(input, id)].Y, FallTolerance);
            }
        }

        [Fact]
        public void Fall_DoesNotMutateInput_AndNoNaN()
        {
            SolverInput input = RoomBridgeAdapter.ToBridge(FloatingFrame(Constants.Scale.RoomToBridge));
            string before = Describe(input);
            Node[] nodes = input.Nodes;
            Member[] members = input.Members;
            Fill[] fills = input.Fills;
            SectionProperties[] sections = input.Sections;

            Assert.Throws<InvalidOperationException>(() => new IntegratorSolver().Reset(input, Configuration()));

            var integrator = new IntegratorSolver();
            var fall = new UnsupportedFallSolver(integrator);
            fall.Reset(input, Configuration());
            var outputs = new List<StepOutput> { fall.Step(Array.Empty<WheelLoad>(), Dt, () => true) };
            for (int n = 0; n < IntegratorSteps; n++)
            {
                outputs.Add(fall.Step(Array.Empty<WheelLoad>(), Dt, () => false));
            }

            Assert.Equal(IntegratorSteps, integrator.StepCount);
            foreach (StepOutput output in outputs)
            {
                Assert.Equal(input.Nodes.Length, output.Displacements.Length);
                Assert.Equal(input.Nodes.Length, output.Rotations.Length);
                Assert.All(output.Displacements, v => AssertFinite(v, "Displacement"));
                Assert.All(output.Rotations, v => AssertFinite(v, "Rotation"));
                Assert.All(output.MemberForces, AssertFinite);
            }

            StepOutput final = outputs.Last();
            double t = IntegratorSteps * Dt;
            Assert.Equal(input.Members.Select(m => m.Id), final.MemberForces.Select(f => f.MemberId));
            Assert.True(final.Displacements[IndexOf(input, 5)].Y < 0.0, "The truss should sag under its own weight.");
            Assert.Contains(final.MemberForces, f => TrussMemberIds.Contains(f.MemberId) && f.Utilization > 0.0);
            foreach (int id in PieceNodeIds)
            {
                AssertRelative(-0.5 * Gravity * t * t, final.Displacements[IndexOf(input, id)].Y, FallTolerance);
            }

            Assert.Same(nodes, input.Nodes);
            Assert.Same(members, input.Members);
            Assert.Same(fills, input.Fills);
            Assert.Same(sections, input.Sections);
            Assert.Equal(before, Describe(input));
        }

        [Fact]
        public void Fall_NothingSupported_DoesNotCallSolver()
        {
            SolverInput input = RoomBridgeAdapter.ToBridge(FloatingFrame(Constants.Scale.RoomToBridge));
            input.Nodes = input.Nodes.Where(n => !TrussNodeIds.Contains(n.Id))
                .Append(new Node { Id = 50, Position = Vec3.Zero, Fixed = true, SurfaceId = 1 }).ToArray();
            input.Members = input.Members.Where(m => !TrussMemberIds.Contains(m.Id)).ToArray();
            var spy = new SpyDynamicSolver { Respond = (_, __) => throw new InvalidOperationException("The solver must not be called.") };
            var fall = new UnsupportedFallSolver(spy);

            fall.Reset(input, Configuration());
            StepOutput output = null;
            for (int n = 0; n < Steps; n++)
            {
                output = fall.Step(Array.Empty<WheelLoad>(), Dt, () => false);
            }

            Assert.Equal(0, spy.ResetCount);
            Assert.Equal(0, spy.StepCount);
            Assert.False(fall.Partition.HasSupportedMembers);
            double t = Steps * Dt;
            foreach (int id in PieceNodeIds)
            {
                AssertRelative(-0.5 * Gravity * t * t, output.Displacements[IndexOf(input, id)].Y, FallTolerance);
            }

            Assert.Equal(Vec3.Zero, output.Displacements[IndexOf(input, 50)]);
            Assert.All(output.Rotations, v => Assert.Equal(Vec3.Zero, v));
            Assert.Equal(input.Members.Select(m => m.Id), output.MemberForces.Select(f => f.MemberId));
            Assert.All(output.MemberForces, AssertZero);
            Assert.Empty(output.FailedMemberIds);
            Assert.Null(output.CarState);
        }

        [Fact]
        public void Fall_RejectsInvalidUse()
        {
            SolverInput input = RoomBridgeAdapter.ToBridge(FloatingFrame(Constants.Scale.RoomToBridge));
            var spy = new SpyDynamicSolver();
            var fall = new UnsupportedFallSolver(spy);

            Assert.Throws<ArgumentNullException>(() => new UnsupportedFallSolver(null));
            Assert.Throws<InvalidOperationException>(() => fall.Step(Array.Empty<WheelLoad>(), Dt, () => false));
            Assert.Throws<ArgumentNullException>(() => fall.Reset(null, Configuration()));
            foreach (double scale in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity })
            {
                SolverInput badScale = RoomBridgeAdapter.ToBridge(FloatingFrame(Constants.Scale.RoomToBridge));
                badScale.ScaleFactor = scale;
                Assert.Throws<ArgumentOutOfRangeException>(() => fall.Reset(badScale, Configuration()));
            }

            SolverInput repeated = RoomBridgeAdapter.ToBridge(FloatingFrame(Constants.Scale.RoomToBridge));
            repeated.Nodes[1] = new Node { Id = 1, Position = new Vec3(3, 3, 3) };
            Assert.Throws<ArgumentException>(() => fall.Reset(repeated, Configuration()));
            Assert.Equal(0, spy.ResetCount);
            Assert.Null(fall.Partition);

            fall.Reset(input, Configuration());
            Assert.Throws<ArgumentNullException>(() => fall.Step(Array.Empty<WheelLoad>(), Dt, null));
            foreach (double dt in new[] { 0.0, -Dt, double.NaN, double.PositiveInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => fall.Step(Array.Empty<WheelLoad>(), dt, () => false));
            }

            Assert.Equal(0, spy.StepCount);

            var broken = new Func<SolverInput, int, StepOutput>[]
            {
                (_, __) => null,
                (received, step) => { StepOutput o = SpyDynamicSolver.DefaultOutput(received, step); o.Displacements = o.Displacements.Skip(1).ToArray(); return o; },
                (received, step) => { StepOutput o = SpyDynamicSolver.DefaultOutput(received, step); o.Rotations = null; return o; },
                (received, step) => { StepOutput o = SpyDynamicSolver.DefaultOutput(received, step); o.MemberForces = null; return o; },
                (received, step) => { StepOutput o = SpyDynamicSolver.DefaultOutput(received, step); o.MemberForces = o.MemberForces.Append(null).ToArray(); return o; },
                (received, step) => { StepOutput o = SpyDynamicSolver.DefaultOutput(received, step); o.MemberForces = o.MemberForces.Append(new MemberForce { MemberId = 11 }).ToArray(); return o; },
                (received, step) => { StepOutput o = SpyDynamicSolver.DefaultOutput(received, step); o.MemberForces = o.MemberForces.Append(o.MemberForces[0]).ToArray(); return o; },
            };
            foreach (Func<SolverInput, int, StepOutput> respond in broken)
            {
                var brokenSpy = new SpyDynamicSolver { Respond = respond };
                var brokenFall = new UnsupportedFallSolver(brokenSpy);
                brokenFall.Reset(input, Configuration());
                Assert.Throws<InvalidOperationException>(() => brokenFall.Step(Array.Empty<WheelLoad>(), Dt, () => false));
                Assert.Equal(0.0, brokenFall.ElapsedSeconds);
                Assert.Throws<InvalidOperationException>(() => brokenFall.Step(Array.Empty<WheelLoad>(), Dt, () => false));
                Assert.Equal(1, brokenSpy.StepCount);
            }

            var failingReset = new UnsupportedFallSolver(new IntegratorSolver());
            SolverInput badSection = RoomBridgeAdapter.ToBridge(FloatingFrame(Constants.Scale.RoomToBridge));
            badSection.Sections = new[] { new SectionProperties { Id = 1 } };
            Assert.ThrowsAny<ArgumentException>(() => failingReset.Reset(badSection, Configuration()));
            Assert.Null(failingReset.Partition);
            Assert.Throws<InvalidOperationException>(() => failingReset.Step(Array.Empty<WheelLoad>(), Dt, () => false));
        }

        // The truss of DeadLoadPreviewTests, fixed at both surfaces, interleaved with a floating
        // two-member line and a floating triangle that share no node with it.
        private static SolverInput FloatingFrame(double scale)
        {
            return new SolverInput
            {
                Nodes = new[]
                {
                    new Node { Id = 1, Position = new Vec3(0, 0, 0), Fixed = true, SurfaceId = 1 },
                    new Node { Id = 101, Position = new Vec3(0.2, 0.6, 0) },
                    new Node { Id = 2, Position = new Vec3(1, 0, 0), Fixed = true, SurfaceId = 2 },
                    new Node { Id = 3, Position = new Vec3(0.25, 0.25, 0) },
                    new Node { Id = 201, Position = new Vec3(0.4, 0.8, 0.1) },
                    new Node { Id = 4, Position = new Vec3(0.75, 0.25, 0) },
                    new Node { Id = 102, Position = new Vec3(0.4, 0.6, 0) },
                    new Node { Id = 5, Position = new Vec3(0.5, 0, 0) },
                    new Node { Id = 202, Position = new Vec3(0.6, 0.8, 0.1) },
                    new Node { Id = 103, Position = new Vec3(0.6, 0.6, 0) },
                    new Node { Id = 203, Position = new Vec3(0.5, 0.95, 0.1) },
                },
                Members = new[]
                {
                    new Member { Id = 1, NodeA = 1, NodeB = 5, SectionId = 1, LineId = 1 },
                    new Member { Id = 2, NodeA = 5, NodeB = 2, SectionId = 1, LineId = 1 },
                    new Member { Id = 11, NodeA = 101, NodeB = 102, SectionId = 1, LineId = 10 },
                    new Member { Id = 3, NodeA = 3, NodeB = 4, SectionId = 1, LineId = 2 },
                    new Member { Id = 21, NodeA = 201, NodeB = 202, SectionId = 1, LineId = 20 },
                    new Member { Id = 4, NodeA = 1, NodeB = 3, SectionId = 1, LineId = 3 },
                    new Member { Id = 12, NodeA = 102, NodeB = 103, SectionId = 1, LineId = 10 },
                    new Member { Id = 5, NodeA = 3, NodeB = 5, SectionId = 1, LineId = 3 },
                    new Member { Id = 22, NodeA = 202, NodeB = 203, SectionId = 1, LineId = 20 },
                    new Member { Id = 6, NodeA = 5, NodeB = 4, SectionId = 1, LineId = 3 },
                    new Member { Id = 7, NodeA = 4, NodeB = 2, SectionId = 1, LineId = 3 },
                    new Member { Id = 23, NodeA = 203, NodeB = 201, SectionId = 1, LineId = 20 },
                },
                Fills = new[]
                {
                    new Fill { Id = 1, NodeIds = new[] { 1, 5, 3 }, Valid = true },
                    new Fill { Id = 2, NodeIds = new[] { 201, 202, 203 }, Road = true, Valid = true },
                },
                Sections = new[] { StandardSection() },
                ScaleFactor = scale,
            };
        }

        private static RunConfiguration Configuration()
        {
            return new RunConfiguration
            {
                StartSurfaceId = 1,
                Car = new CarParameters { MassKg = 1500.0, WheelCount = 4, Wheelbase = 2.6, Track = 1.5, SpringStiffness = 3.0e4, DamperCoefficient = 3.0e3, RestLength = 0.4 },
                Grip = 0.9,
                MaterialBudget = 500.0,
                ScaleFactor = Constants.Scale.RoomToBridge,
            };
        }

        private static SectionProperties StandardSection()
        {
            return new SectionProperties
            {
                Id = 1,
                E = Constants.Human.YoungsModulus,
                G = Constants.Human.ShearModulus,
                A = Constants.Section.Area,
                Iy = Constants.Section.SecondMoment,
                Iz = Constants.Section.SecondMoment,
                J = Constants.Human.TorsionConstant,
                YieldStrength = Constants.Section.YieldStrength,
                Density = Constants.Human.Density,
                Cy = Constants.Section.OuterFiberDistance,
                Cz = Constants.Section.OuterFiberDistance,
            };
        }

        private static string Describe(SolverInput input)
        {
            IEnumerable<string> nodes = input.Nodes.Select(n => "N" + n.Id + " " + n.Position.X.ToString("R") + "," + n.Position.Y.ToString("R") + "," + n.Position.Z.ToString("R") + " " + n.Fixed + " " + n.SurfaceId + " " + n.GroundAnchor);
            IEnumerable<string> members = input.Members.Select(m => "M" + m.Id + " " + m.NodeA + " " + m.NodeB + " " + m.SectionId + " " + m.LineId + " " + m.HiddenBrace);
            IEnumerable<string> fills = input.Fills.Select(f => "F" + f.Id + " " + string.Join(",", f.NodeIds) + " " + f.Road + " " + f.Valid);
            IEnumerable<string> sections = input.Sections.Select(s => "S" + s.Id + " " + s.E + " " + s.G + " " + s.A + " " + s.Iy + " " + s.Iz + " " + s.J + " " + s.YieldStrength + " " + s.Density + " " + s.Cy + " " + s.Cz);
            return string.Join("\n", nodes.Concat(members).Concat(fills).Concat(sections).Append("scale " + input.ScaleFactor.ToString("R")));
        }

        private static int IndexOf(SolverInput input, int nodeId)
        {
            int index = Array.FindIndex(input.Nodes, n => n.Id == nodeId);
            Assert.True(index >= 0, "Node " + nodeId + " is not in the input.");
            return index;
        }

        private static IEnumerable<double> Components(MemberForce force)
        {
            return new[]
            {
                force.Axial, force.ShearLocalYAtA, force.ShearLocalZAtA, force.MomentLocalYAtA, force.MomentLocalZAtA,
                force.ShearLocalYAtB, force.ShearLocalZAtB, force.MomentLocalYAtB, force.MomentLocalZAtB, force.Utilization,
            };
        }

        private static void AssertZero(MemberForce force)
        {
            Assert.All(Components(force), value => Assert.Equal(0.0, value));
        }

        private static void AssertFinite(MemberForce force)
        {
            Assert.All(Components(force), value => AssertFinite(value, "Force component of member " + force.MemberId));
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

        private static void AssertRelative(double expected, double actual, double tolerance)
        {
            Assert.True(
                Math.Abs(actual - expected) <= tolerance * Math.Abs(expected),
                "Expected " + expected.ToString("R") + " within relative " + tolerance + " but got " + actual.ToString("R") + ".");
        }

        /// <summary>
        /// Records every call. By default step n returns distinct finite values for each supported node and
        /// member, and member <see cref="FailedMemberId"/> fails on step <see cref="FailStep"/>.
        /// </summary>
        private sealed class SpyDynamicSolver : IDynamicSolver
        {
            private StepOutput last;

            public Func<SolverInput, int, StepOutput> Respond { get; set; } = DefaultOutput;

            /// <summary>When true, a step polls the cancel callback first and returns its last output if it says stop.</summary>
            public bool PollsCancel { get; set; }

            public int ResetCount { get; private set; }

            public int StepCount { get; private set; }

            public SolverInput ResetInput { get; private set; }

            public RunConfiguration ResetConfiguration { get; private set; }

            public List<WheelLoad[]> ReceivedWheelLoads { get; } = new List<WheelLoad[]>();

            public List<double> ReceivedDt { get; } = new List<double>();

            public List<StepOutput> Outputs { get; } = new List<StepOutput>();

            public static StepOutput DefaultOutput(SolverInput input, int step)
            {
                return new StepOutput
                {
                    Displacements = input.Nodes.Select((n, i) => n.Fixed ? Vec3.Zero : new Vec3(1e-4 * (i + 1) * step, -2e-4 * (i + 1) * step, 5e-5 * step)).ToArray(),
                    Rotations = input.Nodes.Select((n, i) => n.Fixed ? Vec3.Zero : new Vec3(1e-5 * step, -2e-5 * i, 3e-5 * (i + step))).ToArray(),
                    MemberForces = input.Members
                        .Where(m => step < FailStep || m.Id != FailedMemberId)
                        .Select((m, k) => new MemberForce { MemberId = m.Id, Axial = 100.0 * (k + 1) * step, MomentLocalYAtA = -10.0 * k, Utilization = 0.01 * (k + 1) })
                        .ToArray(),
                    FailedMemberIds = step == FailStep ? new[] { FailedMemberId } : Array.Empty<int>(),
                    CarState = new CarState { Position = new Vec3(step, 0, 0), Heading = Vec3.UnitX, Speed = 3.0 },
                };
            }

            public void Reset(SolverInput input, RunConfiguration configuration)
            {
                ResetCount++;
                ResetInput = input;
                ResetConfiguration = configuration;
                StepCount = 0;
                last = null;
            }

            public StepOutput Step(WheelLoad[] previousWheelLoads, double dt, Func<bool> shouldCancel)
            {
                if (PollsCancel && shouldCancel())
                {
                    return last;
                }

                StepCount++;
                ReceivedWheelLoads.Add(previousWheelLoads);
                ReceivedDt.Add(dt);
                last = Respond(ResetInput, StepCount);
                Outputs.Add(last);
                return last;
            }
        }

        /// <summary>The bridge alone under its own weight: a W2-01 integrator stepped with W2-08's self-weight, no car.</summary>
        private sealed class IntegratorSolver : IDynamicSolver
        {
            private readonly NewmarkIntegrator integrator = new NewmarkIntegrator();
            private readonly List<int> failed = new List<int>();
            private SolverInput bridgeInput;

            public int StepCount { get; private set; }

            public void Reset(SolverInput input, RunConfiguration configuration)
            {
                failed.Clear();
                StepCount = 0;
                integrator.Reset(input, DynamicSelfWeight.Loads(input, failed));
                bridgeInput = input;
            }

            public StepOutput Step(WheelLoad[] previousWheelLoads, double dt, Func<bool> shouldCancel)
            {
                StepOutput output = integrator.Step(DynamicSelfWeight.Loads(bridgeInput, failed), dt);
                failed.AddRange(output.FailedMemberIds);
                StepCount++;
                return output;
            }
        }
    }
}
