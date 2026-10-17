using System;
using System.Linq;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class SnapshotAndPreviewTests
    {
        private const double TestSolverPeriod = 0.02;
        private const double TestDebounce = 0.300;

        [Fact]
        public void SnapshotMailbox_ReaderSeesConsistentGeneration()
        {
            var mailbox = new SnapshotMailbox();

            mailbox.Publish(Snapshot(stepId: 1, displacement: 0.1));
            mailbox.Publish(Snapshot(stepId: 2, displacement: 0.2));

            Assert.True(mailbox.TryRead(out SimulationSnapshot read));
            Assert.Equal(2, read.StepId);
            Assert.All(read.Displacements, d => Assert.Equal(new Vec3(0.0, 0.2, 0.0), d));
            Assert.Equal(new[] { 0.2 }, read.Utilizations);
            AssertFinite(read);
        }

        [Fact]
        public void SnapshotMailbox_EmptyUntilFirstPublish()
        {
            var mailbox = new SnapshotMailbox();

            Assert.False(mailbox.TryRead(out SimulationSnapshot none));
            Assert.Null(none);
        }

        [Fact]
        public void SnapshotMailbox_PublisherReusingItsBuffersDoesNotTearReads()
        {
            var mailbox = new SnapshotMailbox();
            SimulationSnapshot buffer = Snapshot(stepId: 1, displacement: 0.1);
            mailbox.Publish(buffer);

            buffer.StepId = 2;
            buffer.Displacements[0] = new Vec3(0.0, 0.2, 0.0);
            buffer.Utilizations[0] = 0.2;

            Assert.True(mailbox.TryRead(out SimulationSnapshot read));
            Assert.Equal(1, read.StepId);
            Assert.All(read.Displacements, d => Assert.Equal(new Vec3(0.0, 0.1, 0.0), d));
            Assert.Equal(new[] { 0.1 }, read.Utilizations);
        }

        [Fact]
        public void SnapshotMailbox_RejectsInconsistentSnapshots()
        {
            var mailbox = new SnapshotMailbox();
            Assert.Throws<ArgumentNullException>(() => mailbox.Publish(null));

            SimulationSnapshot missingArray = Snapshot(1, 0.1);
            missingArray.Rotations = null;
            Assert.Throws<ArgumentException>(() => mailbox.Publish(missingArray));

            SimulationSnapshot nodeMismatch = Snapshot(1, 0.1);
            nodeMismatch.Rotations = new[] { Vec3.Zero };
            Assert.Throws<ArgumentException>(() => mailbox.Publish(nodeMismatch));

            SimulationSnapshot memberMismatch = Snapshot(1, 0.1);
            memberMismatch.Utilizations = new[] { 0.1, 0.2 };
            Assert.Throws<ArgumentException>(() => mailbox.Publish(memberMismatch));

            SimulationSnapshot nanUtilization = Snapshot(1, 0.1);
            nanUtilization.Utilizations[0] = double.NaN;
            Assert.Throws<ArgumentException>(() => mailbox.Publish(nanUtilization));

            SimulationSnapshot infiniteDisplacement = Snapshot(1, 0.1);
            infiniteDisplacement.Displacements[1] = new Vec3(0.0, double.PositiveInfinity, 0.0);
            Assert.Throws<ArgumentException>(() => mailbox.Publish(infiniteDisplacement));

            Assert.False(mailbox.TryRead(out _));
        }

        [Fact]
        public void Preview_DoesNotRunBeforeDebounce()
        {
            var rig = new Rig(debounceSeconds: TestDebounce);
            rig.Scheduler.NotifyEdit(Portal());

            rig.Clock.Now = 0.299;

            Assert.False(rig.Scheduler.Tick());
            Assert.Equal(0, rig.Solver.CallCount);
            Assert.True(rig.Scheduler.HasPending);
            Assert.False(rig.Mailbox.TryRead(out _));
        }

        [Fact]
        public void Preview_RunsAfterDebounce()
        {
            var rig = new Rig(debounceSeconds: TestDebounce);
            SolverInput input = Portal();
            rig.Scheduler.NotifyEdit(input);

            rig.Clock.Now = 0.300;
            Assert.True(rig.Scheduler.Tick());
            Assert.Equal(1, rig.Solver.CallCount);
            Assert.False(rig.Scheduler.HasPending);

            Assert.True(rig.Mailbox.TryRead(out SimulationSnapshot snapshot));
            DeadLoadPreviewResult expected = DeadLoadPreview.Run(Portal(), new StaticSolver(), () => false);
            Assert.Equal(1, snapshot.StepId);
            Assert.Equal(input.Members.Select(m => m.Id), snapshot.MemberIds);
            Assert.Equal(expected.Utilizations, snapshot.Utilizations);
            Assert.Equal(expected.StaticResult.Displacements, snapshot.Displacements);
            Assert.Equal(expected.StaticResult.Rotations, snapshot.Rotations);
            Assert.Contains(snapshot.Utilizations, u => u > 0.0);
            AssertFinite(snapshot);

            rig.Clock.Now = 1.0;
            Assert.False(rig.Scheduler.Tick());
            Assert.Equal(1, rig.Solver.CallCount);
        }

        [Fact]
        public void Preview_CancelOnDrawClearsPending()
        {
            var rig = new Rig(debounceSeconds: TestDebounce);
            rig.Scheduler.NotifyEdit(Portal());
            rig.Clock.Now = 0.1;
            rig.Scheduler.NotifyDrawStart();

            rig.Clock.Now = 0.5;

            Assert.False(rig.Scheduler.Tick());
            Assert.Equal(0, rig.Solver.CallCount);
            Assert.False(rig.Scheduler.HasPending);
            Assert.False(rig.Mailbox.TryRead(out _));
        }

        [Fact]
        public void Debounce_DefaultReadsConstant()
        {
            var rig = new Rig(debounceSeconds: null);
            Assert.Equal(Constants.Preview.DebounceSeconds, rig.Scheduler.DebounceSeconds);
            rig.Scheduler.NotifyEdit(Portal());

            rig.Clock.Now = Math.BitDecrement(Constants.Preview.DebounceSeconds);
            Assert.False(rig.Scheduler.Tick());
            Assert.Equal(0, rig.Solver.CallCount);

            rig.Clock.Now = Constants.Preview.DebounceSeconds;
            Assert.True(rig.Scheduler.Tick());
            Assert.Equal(1, rig.Solver.CallCount);
        }

        [Fact]
        public void Preview_NewerEditRestartsDebounceAndWins()
        {
            var rig = new Rig(debounceSeconds: TestDebounce);
            rig.Scheduler.NotifyEdit(Portal());
            rig.Clock.Now = 0.2;
            SolverInput column = Column();
            rig.Scheduler.NotifyEdit(column);

            rig.Clock.Now = 0.3;
            Assert.False(rig.Scheduler.Tick());
            Assert.Equal(0, rig.Solver.CallCount);

            rig.Clock.Now = 0.5;
            Assert.True(rig.Scheduler.Tick());
            Assert.Equal(1, rig.Solver.CallCount);
            Assert.True(rig.Mailbox.TryRead(out SimulationSnapshot snapshot));
            Assert.Equal(column.Nodes.Length, snapshot.Displacements.Length);
            Assert.Equal(column.Members.Select(m => m.Id), snapshot.MemberIds);
            AssertFinite(snapshot);
        }

        [Fact]
        public void Preview_DrawStartDuringSolveDiscardsResult()
        {
            var rig = new Rig(debounceSeconds: TestDebounce);
            rig.Solver.DuringSolve = () => rig.Scheduler.NotifyDrawStart();
            rig.Scheduler.NotifyEdit(Portal());
            rig.Clock.Now = 0.4;

            Assert.False(rig.Scheduler.Tick());
            Assert.Equal(1, rig.Solver.CallCount);
            Assert.False(rig.Scheduler.HasPending);
            Assert.False(rig.Mailbox.TryRead(out _));

            rig.Solver.DuringSolve = null;
            rig.Scheduler.NotifyEdit(Portal());
            rig.Clock.Now = 1.0;
            Assert.True(rig.Scheduler.Tick());
            Assert.True(rig.Mailbox.TryRead(out SimulationSnapshot snapshot));
            Assert.Equal(1, snapshot.StepId);
        }

        [Fact]
        public void Tick_SchedulesNextTickOneSolverPeriodLater()
        {
            var rig = new Rig(debounceSeconds: TestDebounce, startTime: 2.0);
            Assert.Equal(TestSolverPeriod, rig.Scheduler.SolverPeriodSeconds);
            Assert.Equal(2.0, rig.Scheduler.NextTickTime);

            rig.Clock.Now = 2.5;
            rig.Scheduler.Tick();

            Assert.Equal(2.5 + TestSolverPeriod, rig.Scheduler.NextTickTime);
        }

        [Fact]
        public void Scheduler_RejectsInvalidArguments()
        {
            var clock = new FakeClock();
            var solver = new CountingSolver();
            var mailbox = new SnapshotMailbox();

            Assert.Throws<ArgumentNullException>(() => new PreviewScheduler(null, solver, mailbox, TestSolverPeriod));
            Assert.Throws<ArgumentNullException>(() => new PreviewScheduler(clock, null, mailbox, TestSolverPeriod));
            Assert.Throws<ArgumentNullException>(() => new PreviewScheduler(clock, solver, null, TestSolverPeriod));
            foreach (double bad in new[] { 0.0, -TestSolverPeriod, double.NaN, double.PositiveInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => new PreviewScheduler(clock, solver, mailbox, bad));
            }

            foreach (double bad in new[] { -TestDebounce, double.NaN, double.PositiveInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => new PreviewScheduler(clock, solver, mailbox, TestSolverPeriod, bad));
            }

            var scheduler = new PreviewScheduler(clock, solver, mailbox, TestSolverPeriod, 0.0);
            Assert.Throws<ArgumentNullException>(() => scheduler.NotifyEdit(null));
            clock.Now = double.NaN;
            Assert.Throws<InvalidOperationException>(() => scheduler.Tick());
            Assert.Throws<InvalidOperationException>(() => scheduler.NotifyEdit(Portal()));
            Assert.Equal(0, solver.CallCount);
        }

        private static SimulationSnapshot Snapshot(int stepId, double displacement)
        {
            var moved = new Vec3(0.0, displacement, 0.0);
            return new SimulationSnapshot
            {
                StepId = stepId,
                Displacements = new[] { moved, moved },
                Rotations = new[] { Vec3.Zero, Vec3.Zero },
                MemberIds = new[] { 7 },
                Utilizations = new[] { displacement },
            };
        }

        private static SolverInput Portal()
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
                    new Member { Id = 11, NodeA = 2, NodeB = 3, SectionId = 1, LineId = 2 },
                    new Member { Id = 12, NodeA = 3, NodeB = 4, SectionId = 1, LineId = 3 },
                },
                Sections = new[] { Steel() },
                ScaleFactor = Constants.Scale.RoomToBridge,
            };
        }

        private static SolverInput Column()
        {
            return new SolverInput
            {
                Nodes = new[]
                {
                    new Node { Id = 1, Position = new Vec3(0, 0, 0), Fixed = true, GroundAnchor = true },
                    new Node { Id = 2, Position = new Vec3(0, 0.1, 0) },
                    new Node { Id = 3, Position = new Vec3(0, 0.2, 0) },
                },
                Members = new[]
                {
                    new Member { Id = 20, NodeA = 1, NodeB = 2, SectionId = 1, LineId = 1 },
                    new Member { Id = 21, NodeA = 2, NodeB = 3, SectionId = 1, LineId = 1 },
                },
                Sections = new[] { Steel() },
                ScaleFactor = Constants.Scale.RoomToBridge,
            };
        }

        private static SectionProperties Steel()
        {
            return new SectionProperties
            {
                Id = 1,
                E = 2.0e11,
                G = 7.7e10,
                A = 0.01,
                Iy = 1.0e-4,
                Iz = 1.0e-4,
                J = 2.0e-4,
                YieldStrength = 2.5e8,
                Density = 7850.0,
                Cy = 0.1,
                Cz = 0.1,
            };
        }

        private static void AssertFinite(SimulationSnapshot snapshot)
        {
            Assert.Equal(snapshot.Displacements.Length, snapshot.Rotations.Length);
            Assert.Equal(snapshot.MemberIds.Length, snapshot.Utilizations.Length);
            foreach (Vec3 v in snapshot.Displacements.Concat(snapshot.Rotations))
            {
                Assert.True(double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z), "Node value " + v + " is not finite.");
            }

            Assert.All(snapshot.Utilizations, u => Assert.True(double.IsFinite(u), "Utilization " + u + " is not finite."));
        }

        private sealed class Rig
        {
            public Rig(double? debounceSeconds, double startTime = 0.0)
            {
                Clock = new FakeClock { Now = startTime };
                Scheduler = debounceSeconds.HasValue
                    ? new PreviewScheduler(Clock, Solver, Mailbox, TestSolverPeriod, debounceSeconds.Value)
                    : new PreviewScheduler(Clock, Solver, Mailbox, TestSolverPeriod);
            }

            public FakeClock Clock { get; }

            public CountingSolver Solver { get; } = new CountingSolver();

            public SnapshotMailbox Mailbox { get; } = new SnapshotMailbox();

            public PreviewScheduler Scheduler { get; }
        }

        private sealed class FakeClock : IClock
        {
            public double Now { get; set; }
        }

        private sealed class CountingSolver : IStaticSolver
        {
            private readonly StaticSolver inner = new StaticSolver();

            public int CallCount { get; private set; }

            public Action DuringSolve { get; set; }

            public StaticSolveResult Solve(SolverInput input, NodalLoad[] loads)
            {
                CallCount++;
                DuringSolve?.Invoke();
                return inner.Solve(input, loads);
            }
        }
    }
}
