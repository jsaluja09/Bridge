using System;
using System.Collections.Generic;
using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>An integrator that records the loads it is stepped with.</summary>
    sealed class SpyIntegrator : IFrameIntegrator
    {
        readonly FrameDynamics _inner;
        public readonly List<Vec3[]> StepLoads = new List<Vec3[]>();
        public SpyIntegrator(SolverInput input) => _inner = new FrameDynamics(input);
        public int NodeCount => _inner.NodeCount;
        public bool[] Active => _inner.Active;
        public bool[] InFrame => _inner.InFrame;
        public bool[] NodeInFrame => _inner.NodeInFrame;
        public MemberForce[] Forces => _inner.Forces;
        public double[] Utilization => _inner.Utilization;
        public FailureMode[] Mode => _inner.Mode;
        public FailureMode[] BrokenMode => _inner.BrokenMode;
        public Vec3 Position(int node) => _inner.Position(node);
        public Vec3 Velocity(int node) => _inner.Velocity(node);
        public Vec3 Displacement(int node) => _inner.Displacement(node);
        public double NodeMass(int node) => _inner.NodeMass(node);
        public IReadOnlyList<int> MembersAt(int node) => _inner.MembersAt(node);
        public int MemberNodeA(int member) => _inner.MemberNodeA(member);
        public int MemberNodeB(int member) => _inner.MemberNodeB(member);
        public List<int> Rebuild(bool firstTime) => _inner.Rebuild(firstTime);
        public void SolveStatic(Vec3[] loads) => _inner.SolveStatic(loads);
        /// <summary>A member to break at the next failure check, whatever its utilization (-1: none).</summary>
        public int ForceFail = -1;
        bool _forced;

        public List<int> RemoveFailures(int maxPerStep)
        {
            var failed = _inner.RemoveFailures(maxPerStep);
            if (ForceFail >= 0 && _inner.Active[ForceFail])
            {
                _inner.Active[ForceFail] = false;
                failed.Add(ForceFail);
                _forced = true;
            }
            return failed;
        }

        public List<int> DetachAfterFailures()
        {
            if (!_forced)
                return _inner.DetachAfterFailures();
            _forced = false;
            return _inner.Rebuild(false);
        }
        public void Step(Vec3[] loads, double dt)
        {
            StepLoads.Add((Vec3[])loads.Clone());
            _inner.Step(loads, dt);
        }
    }

    /// <summary>A car that records the deck samples it reads and returns fixed wheel loads per step.</summary>
    sealed class SpyCar : ICarModel
    {
        public readonly List<DeckSample[]> Reads = new List<DeckSample[]>();
        int _step;
        public CarState Step(CarState state, CarParameters p, DeckSample[] deck, Vec3 externalForceBridge, double dt)
        {
            Reads.Add(deck);
            var next = state.Clone();
            _step++;
            for (int i = 0; i < next.WheelLoads.Length; i++)
                next.WheelLoads[i] = 1000 * _step;
            return next;
        }
    }

    /// <summary>Builds room bridges the way a player does: two pads, then free-drawn lines.</summary>
    static class Rooms
    {
        public const double Top = 0.75;
        public static Pad[] Pads() => Pad.FromTaps(new Vec3(0, Top, 0), new Vec3(1, Top, 0)) is var (a, b) ? new[] { a, b } : null;

        public static BridgeModel Model() => new BridgeModel(new BudgetPolicy
        {
            CostPerMeter = Constants.Budget.CostPerMeter, CostPerSquareMeter = Constants.Budget.CostPerSquareMeter,
            MaterialBudget = Constants.Budget.MaterialBudget, NodeCap = Constants.Human.NodeCap + Constants.Draw.SupportNodeAllowance,
        });

        /// <summary>Draws a stroke from a to b (with slight hand noise) and adds it, like the game does.</summary>
        public static void Draw(BridgeModel m, Pad[] pads, params Vec3[] corners)
        {
            var raw = new List<Vec3>();
            var rng = new Random(7);
            for (int c = 0; c + 1 < corners.Length; c++)
                for (int i = 0; i <= 30; i++)
                    raw.Add(Vec3.Lerp(corners[c], corners[c + 1], i / 30.0) + new Vec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5) * 0.002);
            var ctx = new FreeDraw.Context
            {
                Nodes = m.Nodes,
                Joined = (x, y) => m.Members.Any(k => (k.NodeA == x && k.NodeB == y) || (k.NodeA == y && k.NodeB == x)),
                Pads = pads,
            };
            foreach (var chain in FreeDraw.Build(raw, ctx, out _))
            {
                var vertices = new List<LineVertex>();
                foreach (var v in chain)
                {
                    if (v.IsExisting)
                        vertices.Add(LineVertex.Existing(v.ExistingId));
                    else if (v.NewSupport)
                        vertices.Add(LineVertex.Existing(m.AddFixedNode(v.Position, v.SurfaceId, v.Ground)));
                    else
                        vertices.Add(LineVertex.New(v.Position));
                }
                Assert.True(m.TryAddFreeLine(vertices).Ok);
            }
        }

        public static Vec3 P(double x, double y, double z) => new Vec3(x, y, z);

        /// <summary>Two straight beams pad to pad, 0.2 m apart: the bare span with an automatic deck.</summary>
        public static BridgeModel BareDeck(Pad[] pads)
        {
            var m = Model();
            Draw(m, pads, P(0, Top, -0.1), P(1, Top, -0.1));
            Draw(m, pads, P(0, Top, 0.1), P(1, Top, 0.1));
            return m;
        }

        /// <summary>A Pratt-style truss on each side of the deck, 0.2 m deep.</summary>
        public static BridgeModel Truss(Pad[] pads)
        {
            var m = BareDeck(pads);
            const double h = Top + 0.2;
            foreach (double z in new[] { -0.1, 0.1 })
            {
                Draw(m, pads, P(0, Top, z), P(0.2, h, z), P(0.8, h, z), P(1, Top, z));
                foreach (double x in new[] { 0.4, 0.6 })
                    Draw(m, pads, P(x, Top, z), P(x, h, z));
                Draw(m, pads, P(0.2, h, z), P(0.4, Top, z));
                Draw(m, pads, P(0.8, h, z), P(0.6, Top, z));
                Draw(m, pads, P(0.4, h, z), P(0.5, Top, z));
                Draw(m, pads, P(0.6, h, z), P(0.5, Top, z));
            }
            return m;
        }

        /// <summary>The bare deck plus a dense Warren truss under each rail: `panels` triangles, `depth` deep.</summary>
        public static BridgeModel UnderTruss(Pad[] pads, int panels = 10, double depth = 0.1)
        {
            var m = BareDeck(pads);
            double step = 1.0 / panels;
            foreach (double z in new[] { -0.1, 0.1 })
            {
                var apex = Enumerable.Range(0, panels).Select(i => P((i + 0.5) * step, Top - depth, z)).ToArray();
                Draw(m, pads, apex);
                for (int i = 0; i < panels; i++)
                {
                    Draw(m, pads, P(i * step, Top, z), apex[i]);
                    Draw(m, pads, apex[i], P((i + 1) * step, Top, z));
                }
            }
            return m;
        }

        public static BridgeRun Run(BridgeModel m, Pad[] pads, ChallengeConfig c = null)
        {
            var bridge = BridgeCompiler.Compile(m, pads, Constants.Scale.RoomToBridge);
            var config = (c ?? new ChallengeConfig()).ToRunConfiguration(new StartSurfaceChoice(0, 1), pads[0].Across, Constants.Scale.RoomToBridge);
            return new BridgeRun(bridge, config, 0);
        }
    }

    /// <summary>W3-01: lagged car and solver coupling.</summary>
    public class CoupledStepTests
    {
        static (CoupledStep step, SpyIntegrator frame, SpyCar car, CompiledBridge bridge) Make()
        {
            var pads = Rooms.Pads();
            var model = Rooms.BareDeck(pads);
            var bridge = BridgeCompiler.Compile(model, pads, 20);
            SpyIntegrator spy = null;
            var car = new SpyCar();
            var step = new CoupledStep(car, pads, bridge.Decks, input => spy = new SpyIntegrator(input));
            step.Reset(bridge.Input, new ChallengeConfig().ToRunConfiguration(new StartSurfaceChoice(0, 1), Vec3.UnitZ, 20));
            return (step, spy, car, bridge);
        }

        [Fact]
        public void Coupling_UsesPreviousWheelLoads()
        {
            var (step, frame, _, _) = Make();
            var first = step.Step(new WheelLoad[0], 1.0 / 60, null);
            var loadsFromFirst = first.Car.WheelLoads.Select((l, i) => new WheelLoad(i, l)).ToArray();
            step.Step(loadsFromFirst, 1.0 / 60, null);
            // The first step saw no wheel loads, so it carried only the static loads; the car produced 1000 N per wheel then.
            Assert.Equal(1000, first.Car.WheelLoads[0]);
            Assert.Equal(2, frame.StepLoads.Count);
        }

        [Fact]
        public void Coupling_ThenUpdatesCarFromNewDeck()
        {
            var (step, _, car, _) = Make();
            step.Step(new WheelLoad[0], 1.0 / 60, null);
            Assert.Single(car.Reads);
            Assert.Equal(4, car.Reads[0].Length);
            Assert.All(car.Reads[0], s => Assert.True(s.HasDeck));       // parked on the start pad
        }

        [Fact]
        public void Coupling_CopyDoesNotMutateOriginal()
        {
            var pads = Rooms.Pads();
            var model = Rooms.BareDeck(pads);
            var before = model.Nodes.Select(n => n.Position).ToList();
            int members = model.Members.Count;
            var copy = model.Copy();
            var run = Rooms.Run(copy, pads);
            for (int i = 0; i < 30; i++)
                run.Step(1.0 / 60);
            Assert.Equal(members, model.Members.Count);
            Assert.Equal(before, model.Nodes.Select(n => n.Position).ToList());
        }

        [Fact]
        public void Coupling_IsDeterministic()
        {
            var pads = Rooms.Pads();
            var a = Rooms.Run(Rooms.Truss(pads), pads);
            var b = Rooms.Run(Rooms.Truss(pads), pads);
            for (int i = 0; i < 60; i++)
            {
                a.Step(1.0 / 60);
                b.Step(1.0 / 60);
            }
            Assert.Equal(a.Coupled.Positions, b.Coupled.Positions);
            Assert.Equal(a.Coupled.Car.WheelLoads, b.Coupled.Car.WheelLoads);
            Assert.Equal(a.Coupled.Car.Speed, b.Coupled.Car.Speed);
        }

        [Fact]
        public void Coupling_NoNaN()
        {
            var pads = Rooms.Pads();
            var run = Rooms.Run(Rooms.Truss(pads), pads);
            for (int i = 0; i < 120; i++)
            {
                run.Step(1.0 / 60);
                Assert.All(run.Coupled.Positions, p => Assert.True(p.IsFinite));
                Assert.All(run.Coupled.Car.WheelLoads, l => Assert.True(double.IsFinite(l)));
                Assert.True(double.IsFinite(run.Coupled.Car.Speed));
            }
        }

        [Fact]
        public void SelfWeight_DropsBrokenMembers()
        {
            var (step, frame, _, bridge) = Make();
            var input = bridge.Input;
            int k = Enumerable.Range(0, input.Members.Count).First(i => !input.Members[i].HiddenBrace);
            var all = DeadLoadPreview.SelfWeight(input, Constants.Loads.Gravity, 20);
            var standing = Enumerable.Range(0, input.Members.Count).Select(i => i != k).ToArray();
            var without = DeadLoadPreview.SelfWeight(input, Constants.Loads.Gravity, 20, standing);

            // The car is parked on the pad, so the bridge carries only its own weight.
            step.Step(new WheelLoad[0], 1.0 / 60, null);
            Assert.Equal(all.Select(l => l.Force), frame.StepLoads[0]);

            frame.ForceFail = k;
            step.Step(new WheelLoad[0], 1.0 / 60, null);    // breaks member k after its solve
            step.Step(new WheelLoad[0], 1.0 / 60, null);
            Assert.Equal(without.Select(l => l.Force), frame.StepLoads[2]);

            var m = input.Members[k];
            var s = input.Sections.First(x => x.Id == m.SectionId);
            double length = Vec3.Distance(input.Nodes.First(x => x.Id == m.NodeA).Position, input.Nodes.First(x => x.Id == m.NodeB).Position) * 20;
            double lost = frame.StepLoads[2].Sum(f => f.Y) - frame.StepLoads[0].Sum(f => f.Y);
            Assert.Equal(length * s.A * s.Density * Constants.Loads.Gravity, lost, 6);
        }

        [Fact]
        public void Coupling_CancelFlagSkipsSolve()
        {
            var (step, frame, _, _) = Make();
            var first = step.Step(new WheelLoad[0], 1.0 / 60, null);
            var again = step.Step(new WheelLoad[0], 1.0 / 60, () => true);
            Assert.Same(first, again);
            Assert.Single(frame.StepLoads);
        }
    }

    sealed class FakeClock : IClock
    {
        public double Now { get; set; }
    }

    /// <summary>W3-02: snapshot mailbox and preview scheduler.</summary>
    public class SnapshotAndPreviewTests
    {
        [Fact]
        public void SnapshotMailbox_ReaderSeesConsistentGeneration()
        {
            var box = new SnapshotMailbox();
            box.Publish(new SimulationSnapshot { StepId = 1, Displacements = new[] { new Vec3(0.1, 0, 0) } });
            box.Publish(new SimulationSnapshot { StepId = 2, Displacements = new[] { new Vec3(0.2, 0, 0) } });
            Assert.True(box.TryRead(out var s));
            Assert.Equal(2, s.StepId);
            Assert.Equal(0.2, s.Displacements[0].X);
        }

        [Fact]
        public void Preview_DoesNotRunBeforeDebounce()
        {
            var clock = new FakeClock();
            int calls = 0;
            var s = new PreviewScheduler(clock, () => calls++, 0.300);
            s.NotifyPaused();
            clock.Now = 0.299;
            s.Tick();
            Assert.Equal(0, calls);
        }

        [Fact]
        public void Preview_RunsAfterDebounce()
        {
            var clock = new FakeClock();
            int calls = 0;
            var s = new PreviewScheduler(clock, () => calls++, 0.300);
            s.NotifyPaused();
            clock.Now = 0.300;
            s.Tick();
            s.Tick();
            Assert.Equal(1, calls);
        }

        [Fact]
        public void Preview_CancelOnDrawClearsPending()
        {
            var clock = new FakeClock();
            int calls = 0;
            var s = new PreviewScheduler(clock, () => calls++, 0.300);
            s.NotifyPaused();
            s.NotifyDrawStarted();
            clock.Now = 1;
            s.Tick();
            Assert.Equal(0, calls);
            Assert.False(s.Pending);
        }

        [Fact]
        public void Debounce_DefaultReadsConstant()
        {
            var clock = new FakeClock();
            int calls = 0;
            var s = new PreviewScheduler(clock, () => calls++);
            s.NotifyPaused();
            clock.Now = Constants.Preview.DebounceSeconds - 1e-6;
            s.Tick();
            Assert.Equal(0, calls);
            clock.Now = Constants.Preview.DebounceSeconds;
            s.Tick();
            Assert.Equal(1, calls);
        }
    }

    /// <summary>W3-03: challenge config and start-surface choice.</summary>
    public class ChallengeConfigTests
    {
        [Fact]
        public void Challenge_HoldsCarWeightWindGripBudget()
        {
            var c = new ChallengeConfig { CarWeightKg = 1000, WindSpeed = 15, Grip = 0.8, Budget = 500 };
            Assert.Equal(1000, c.CarWeightKg);
            Assert.Equal(15, c.WindSpeed);
            Assert.Equal(0.8, c.Grip);
            Assert.Equal(500, c.Budget);
            Assert.DoesNotContain(typeof(ChallengeConfig).GetProperties(), p => p.PropertyType == typeof(Vec3));
        }

        [Fact]
        public void StartSurface_SelectsTheOtherAsFar()
        {
            var s = new StartSurfaceChoice(7, 9);
            s.Choose(7);
            Assert.Equal(9, s.Far);
            s.Choose(9);
            Assert.Equal(7, s.Far);
        }

        [Fact] public void StartSurface_RejectsUnknownId() => Assert.Throws<ArgumentException>(() => new StartSurfaceChoice(0, 1).Choose(5));

        [Fact]
        public void GroundOverride_CopiedOntoRunConfiguration()
        {
            var start = new StartSurfaceChoice(0, 1);
            Assert.Equal(1.25, new ChallengeConfig { GroundHeightOverride = 1.25 }.ToRunConfiguration(start, Vec3.UnitX, 20).GroundHeightOverride);
            Assert.Null(new ChallengeConfig().ToRunConfiguration(start, Vec3.UnitX, 20).GroundHeightOverride);
        }

        [Fact]
        public void FreePlay_IsAbsent() =>
            Assert.DoesNotContain(typeof(ChallengeConfig).GetMethods(), m => m.Name == "FreePlay" || m.Name == "SetPlayerConditions");
    }

    /// <summary>Spec 11: free drawing, automatic decks, and the whole loop from strokes to a test run.</summary>
    public class GameFlowTests
    {
        readonly Xunit.Abstractions.ITestOutputHelper _out;
        public GameFlowTests(Xunit.Abstractions.ITestOutputHelper output) => _out = output;

        [Fact]
        public void FreeDraw_StartsAnywhere_AndCutsEqualSegments()
        {
            var pads = Rooms.Pads();
            var m = Rooms.Model();
            Rooms.Draw(m, pads, Rooms.P(0.3, 1.2, 0), Rooms.P(0.8, 1.2, 0));
            Assert.Single(m.Lines);
            Assert.Equal(6, m.Nodes.Count);                       // 0.5 m: 5 segments, 6 new free nodes
            Assert.All(m.Nodes, n => Assert.False(n.Fixed));
        }

        [Fact]
        public void FreeDraw_EndsOnPads_BecomeSupports()
        {
            var pads = Rooms.Pads();
            var m = Rooms.Model();
            Rooms.Draw(m, pads, Rooms.P(-0.05, Rooms.Top + 0.01, 0), Rooms.P(1.05, Rooms.Top, 0));
            Assert.Equal(2, m.Nodes.Count(n => n.Fixed));
            Assert.Contains(m.Nodes, n => n.Fixed && n.SurfaceId == 0);
            Assert.Contains(m.Nodes, n => n.Fixed && n.SurfaceId == 1);
        }

        [Fact]
        public void FreeDraw_PassingNearADot_FusesWithIt()
        {
            var pads = Rooms.Pads();
            var m = Rooms.Model();
            Rooms.Draw(m, pads, Rooms.P(0.5, 1.0, -0.3), Rooms.P(0.5, 1.0, 0.3));
            var middle = m.Nodes.OrderBy(n => Math.Abs(n.Position.Z)).First();
            Rooms.Draw(m, pads, Rooms.P(0.2, 1.0, middle.Position.Z + 0.01), Rooms.P(0.8, 1.0, middle.Position.Z + 0.01));
            Assert.Contains(m.Members, k => k.LineId == m.Lines[1].Id && (k.NodeA == middle.Id || k.NodeB == middle.Id));
        }

        [Fact]
        public void AutoDeck_FormsBetweenFlatSideBySideLines_Only()
        {
            var pads = Rooms.Pads();
            Assert.Single(AutoDeck.Find(Rooms.BareDeck(pads)));

            var far = Rooms.Model();
            Rooms.Draw(far, pads, Rooms.P(0, Rooms.Top, -0.4), Rooms.P(1, Rooms.Top, -0.4));
            Rooms.Draw(far, pads, Rooms.P(0, Rooms.Top, 0.4), Rooms.P(1, Rooms.Top, 0.4));
            Assert.Empty(AutoDeck.Find(far));

            var steep = Rooms.Model();
            Rooms.Draw(steep, pads, Rooms.P(0.2, 0.5, -0.1), Rooms.P(0.6, 1.0, -0.1));
            Rooms.Draw(steep, pads, Rooms.P(0.2, 0.5, 0.1), Rooms.P(0.6, 1.0, 0.1));
            Assert.Empty(AutoDeck.Find(steep));
        }

        [Fact]
        public void BareDeck_RoadValidates_ButFailsUnderTheCar()
        {
            var pads = Rooms.Pads();
            var run = Rooms.Run(Rooms.BareDeck(pads), pads);
            Assert.True(run.Bridge.Road.Ok, run.Bridge.Road.Message);
            run.RunToEnd();
            _out.WriteLine($"bare deck: {run.Result} {run.Message} lost {run.MembersLost} peak {run.PeakUtilization:F2} t {run.Time:F1}s; {run.Lesson}");
            Assert.Equal(RunResult.Failed, run.Result);
            Assert.True(run.MembersLost > 0);
            Assert.NotEmpty(run.Lesson);
        }

        [Fact]
        public void Truss_CarriesTheCarAcross()
        {
            var pads = Rooms.Pads();
            var run = Rooms.Run(Rooms.Truss(pads), pads);
            run.RunToEnd();
            _out.WriteLine($"truss: {run.Result} {run.Message} lost {run.MembersLost} peak {run.PeakUtilization:F2} t {run.Time:F1}s");
            Assert.Equal(RunResult.Passed, run.Result);
            Assert.Equal(0, run.MembersLost);
            Assert.True(run.PeakUtilization < 0.8);
        }

        [Fact]
        public void FloatingLine_IsFlaggedByThePreview_AndFallsWhenTested()
        {
            var pads = Rooms.Pads();
            var m = Rooms.Truss(pads);
            Rooms.Draw(m, pads, Rooms.P(0.3, 1.4, 0.4), Rooms.P(0.7, 1.4, 0.4));
            var bridge = BridgeCompiler.Compile(m, pads, 20);
            var preview = DeadLoadPreview.Run(bridge.Input, new BandedStaticSolver(), null);
            Assert.Equal(PreviewStatus.Completed, preview.Status);
            var run = new BridgeRun(bridge, new ChallengeConfig().ToRunConfiguration(new StartSurfaceChoice(0, 1), Vec3.UnitZ, 20), 0);
            for (int i = 0; i < 60; i++)
                run.Step(1.0 / 60);
            Assert.True(run.Coupled.Debris.Count > 0);
        }
    }
}
