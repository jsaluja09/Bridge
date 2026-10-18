using System;
using System.Collections.Generic;
using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>The one level's phased test: phase settings, the sequence, stars and advice.</summary>
    public class PhaseTests
    {
        const double Scale = 20;

        static TestPhase Phase(PhaseKind kind) => TestPhases.All.Single(p => p.Kind == kind);

        static RunConfiguration Configure(PhaseKind kind, ChallengeConfig level = null)
        {
            var pads = Rooms.Pads();
            return Phase(kind).Configure(level ?? ChallengeConfig.Level(), new StartSurfaceChoice(0, 1), pads[0].Across, Scale);
        }

        static PhaseOutcome Outcome(TestPhase phase, bool passed) => new PhaseOutcome { Phase = phase, Passed = passed };

        // ---------- Phases ----------

        [Fact]
        public void Phases_RunCalmWindSnowTrafficJam()
        {
            Assert.Equal(new[] { PhaseKind.Calm, PhaseKind.Wind, PhaseKind.Snow, PhaseKind.Traffic, PhaseKind.Jam },
                         TestPhases.All.Select(p => p.Kind));
            Assert.All(TestPhases.All, p => Assert.False(string.IsNullOrWhiteSpace(p.Name + p.Intro)));
        }

        [Fact]
        public void StarsGrowAlongThePhases_AndTheJamGivesThree()
        {
            var stars = TestPhases.All.Select(p => p.StarsWhenPassed).ToList();
            Assert.Equal(stars.OrderBy(s => s), stars);
            Assert.Equal(1, Phase(PhaseKind.Calm).StarsWhenPassed);
            Assert.Equal(2, Phase(PhaseKind.Snow).StarsWhenPassed);
            Assert.Equal(TestPhases.MaxStars, Phase(PhaseKind.Jam).StarsWhenPassed);
        }

        [Fact]
        public void Calm_IsOneCarInStillAir()
        {
            var c = Configure(PhaseKind.Calm);
            Assert.Equal(0, c.WindSpeed);
            Assert.Equal(0, c.SnowLoadPa);
            Assert.Null(c.Traffic);
            Assert.Equal(Constants.Car.DefaultGrip, c.Grip);
        }

        [Fact]
        public void Wind_BlowsAGale()
        {
            var c = Configure(PhaseKind.Wind);
            Assert.Equal(Constants.Weather.WindSpeed, c.WindSpeed);
            Assert.Null(c.Traffic);
        }

        [Fact]
        public void Snow_AddsWeightAndIce()
        {
            var c = Configure(PhaseKind.Snow);
            Assert.Equal(Constants.Weather.SnowLoadPa, c.SnowLoadPa);
            Assert.Equal(Constants.Weather.SnowGrip, c.Grip);
            Assert.Equal(0, c.WindSpeed);
        }

        [Fact]
        public void TrafficPhases_SendTrafficOrAJam()
        {
            var traffic = Configure(PhaseKind.Traffic).Traffic;
            Assert.False(traffic.Jam);
            Assert.Equal(Constants.Phases.TrafficCarsPerLane, traffic.CarsPerLane);
            Assert.True(Configure(PhaseKind.Jam).Traffic.Jam);
        }

        [Fact]
        public void EveryPhase_KeepsTheLevelsCarAndBudget()
        {
            var level = new ChallengeConfig { CarWeightKg = 1234, Budget = 9 };
            foreach (var phase in TestPhases.All)
            {
                var c = Configure(phase.Kind, level);
                Assert.Equal(1234, c.Car.MassKg);
                Assert.Equal(9, c.MaterialBudget);
            }
        }

        [Fact]
        public void Level_UsesTheLevelBudgetAndStandardCar()
        {
            var level = ChallengeConfig.Level();
            Assert.Equal(Constants.Phases.LevelBudget, level.Budget);
            Assert.Equal(Constants.Human.CarMassKg, level.CarWeightKg);
            Assert.Equal(0, level.WindSpeed);
        }

        // ---------- Sequence ----------

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 1)]
        [InlineData(2, 1)]
        [InlineData(3, 2)]
        [InlineData(4, 2)]
        public void Sequence_StopsAtTheFirstFailure_WithTheStarsEarnedSoFar(int passed, int stars)
        {
            var seq = new PhaseSequence();
            for (int i = 0; i < passed; i++)
                seq.Record(Outcome(seq.Current, true));
            seq.Record(Outcome(seq.Current, false));

            Assert.True(seq.Finished);
            Assert.Null(seq.Current);
            Assert.Equal(passed + 1, seq.Outcomes.Count);
            Assert.Equal(stars, seq.Stars);
        }

        [Fact]
        public void Sequence_PassingEveryPhase_GivesThreeStars()
        {
            var seq = new PhaseSequence();
            while (!seq.Finished)
                seq.Record(Outcome(seq.Current, true));
            Assert.Equal(TestPhases.All.Count, seq.Outcomes.Count);
            Assert.Equal(3, seq.Stars);
        }

        [Fact]
        public void Sequence_StartsAtCalmWithNoStars()
        {
            var seq = new PhaseSequence();
            Assert.Equal(PhaseKind.Calm, seq.Current.Kind);
            Assert.Equal(0, seq.Stars);
            Assert.False(seq.Finished);
        }

        [Fact]
        public void Sequence_RefusesOutOfTurnOutcomes()
        {
            var seq = new PhaseSequence();
            Assert.Throws<ArgumentException>(() => seq.Record(null));
            Assert.Throws<ArgumentException>(() => seq.Record(Outcome(Phase(PhaseKind.Jam), true)));
            seq.Record(Outcome(seq.Current, false));
            Assert.Throws<InvalidOperationException>(() => seq.Record(Outcome(Phase(PhaseKind.Wind), true)));
        }

        // ---------- Advice ----------

        static PhaseResult Passed(double peak) => new PhaseResult(true, FailureMode.None, false, peak);
        static PhaseResult Broke(FailureMode mode) => new PhaseResult(false, mode, false, 1);
        static PhaseResult OffRoad() => new PhaseResult(false, FailureMode.None, false, 0.3);

        [Fact]
        public void Pass_WithACloseCall_NamesTheLoad()
        {
            string tip = PhaseFeedback.Tip(PhaseKind.Snow, Passed(0.87));
            Assert.Contains("Close call", tip);
            Assert.Contains("87%", tip);
            Assert.Contains("Close call", PhaseFeedback.Tip(PhaseKind.Calm, Passed(Constants.Phases.CloseCallUtilization)));
        }

        [Fact]
        public void Pass_WithRoomToSpare_IsSolid()
        {
            string tip = PhaseFeedback.Tip(PhaseKind.Calm, Passed(0.31));
            Assert.Contains("Solid", tip);
            Assert.Contains("31%", tip);
        }

        [Fact]
        public void EveryPhase_HasItsOwnAdviceAfterABreak()
        {
            var tips = TestPhases.All.Select(p => PhaseFeedback.Tip(p.Kind, Broke(FailureMode.Bending))).ToList();
            Assert.All(tips, t => Assert.False(string.IsNullOrWhiteSpace(t)));
            Assert.Equal(tips.Count, tips.Distinct().Count());
            Assert.Contains("sideways", PhaseFeedback.Tip(PhaseKind.Wind, Broke(FailureMode.Buckling)));
            Assert.Contains("taller", PhaseFeedback.Tip(PhaseKind.Jam, Broke(FailureMode.Tension)));
        }

        [Fact]
        public void CarOffTheRoad_GetsRoadAdvice()
        {
            Assert.Contains("wind", PhaseFeedback.Tip(PhaseKind.Wind, OffRoad()));
            Assert.Contains("icy", PhaseFeedback.Tip(PhaseKind.Snow, OffRoad()));
            Assert.Contains("road", PhaseFeedback.Tip(PhaseKind.Traffic, OffRoad()));
        }

        [Fact]
        public void StuckTraffic_GetsAdviceAboutTheRoad() =>
            Assert.Contains("couldn't get across", PhaseFeedback.Tip(PhaseKind.Traffic, new PhaseResult(false, FailureMode.None, true, 0.2)));

        // ---------- Real runs ----------

        static BridgeRun RunPhase(BridgeModel model, Pad[] pads, PhaseKind kind)
        {
            var run = new BridgeRun(BridgeCompiler.Compile(model.Copy(), pads, Scale), Configure(kind), 0);
            run.RunToEnd(1.0 / 60, 70);
            return run;
        }

        static PhaseSequence RunAll(BridgeModel model, Pad[] pads)
        {
            var seq = new PhaseSequence();
            while (!seq.Finished)
            {
                var run = RunPhase(model, pads, seq.Current.Kind);
                seq.Record(new PhaseOutcome { Phase = seq.Current, Passed = run.Result == RunResult.Passed });
            }
            return seq;
        }

        [Fact]
        public void BrokenBridge_PointsAtTheFirstMemberToBreak()
        {
            var pads = Rooms.Pads();
            var run = RunPhase(Rooms.BareDeck(pads), pads, PhaseKind.Calm);

            Assert.Equal(RunResult.Failed, run.Result);
            Assert.True(run.FirstLostMember >= 0);
            Assert.Equal(run.FirstLostMember, run.WeakMember);
            Assert.NotEqual(FailureMode.None, run.PhaseResult.FirstBreak);
            Assert.False(run.PhaseResult.Passed);
        }

        [Fact]
        public void StandingBridge_PointsAtTheMemberThatCameClosest()
        {
            var pads = Rooms.Pads();
            var run = RunPhase(Rooms.Truss(pads), pads, PhaseKind.Calm);

            Assert.Equal(RunResult.Passed, run.Result);
            Assert.Equal(-1, run.FirstLostMember);
            Assert.Equal(run.PeakMember, run.WeakMember);
            Assert.False(run.Bridge.Input.Members[run.WeakMember].HiddenBrace);
            Assert.Equal(FailureMode.None, run.PhaseResult.FirstBreak);
            Assert.InRange(run.PhaseResult.PeakUtilization, 0.01, 1);

            var snap = new RunSnapshot();
            run.Write(snap);
            Assert.Equal(run.WeakMember, snap.WeakMember);
        }

        [Fact]
        public void Difficulty_BareRailsGetNothing()
        {
            var pads = Rooms.Pads();
            var seq = RunAll(Rooms.BareDeck(pads), pads);
            Assert.Equal(0, seq.Stars);
            Assert.Single(seq.Outcomes);
        }

        [Fact]
        public void Difficulty_ATrussWithinBudgetEarnsThreeStars()
        {
            var pads = Rooms.Pads();
            var truss = Rooms.Truss(pads);
            double cost = truss.Spent + BridgeCompiler.Compile(truss, pads, Scale).DeckCost(truss.Policy);
            Assert.True(cost <= Constants.Phases.LevelBudget, $"the test truss costs {cost:F1}");

            var seq = RunAll(truss, pads);

            Assert.Equal(3, seq.Stars);
            Assert.All(seq.Outcomes, o => Assert.True(o.Passed, $"{o.Phase.Name} failed"));
        }
    }
}
