using System;
using System.Collections.Generic;
using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>How far a running phase has got toward passing, by the run's own end rules (for the HUD).</summary>
    public class RunProgressTests
    {
        const double Scale = 20;
        const double Dt = 1.0 / 60;
        const double MaxSeconds = 70;

        static BridgeRun Start(BridgeModel model, Pad[] pads, PhaseKind kind)
        {
            var config = TestPhases.All.Single(p => p.Kind == kind).Configure(ChallengeConfig.Level(), new StartSurfaceChoice(0, 1), pads[0].Across, Scale);
            return new BridgeRun(BridgeCompiler.Compile(model.Copy(), pads, Scale), config, 0);
        }

        static RunSnapshot Snapshot(BridgeRun run)
        {
            var snap = new RunSnapshot();
            run.Write(snap);
            return snap;
        }

        /// <summary>
        /// Steps to the result, checking after every step that the snapshot's progress is finite, within 0..1 and
        /// never lower than the step before. Returns every step's progress, starting before the first step.
        /// </summary>
        static List<double> RunToResult(BridgeRun run, Action<RunSnapshot> eachStep = null)
        {
            var snap = Snapshot(run);
            var seen = new List<double> { snap.Progress };
            while (run.Result == RunResult.Running && run.Time < MaxSeconds)
            {
                run.Step(Dt);
                run.Write(snap);
                double previous = seen[seen.Count - 1];
                Assert.True(double.IsFinite(snap.Progress), $"progress {snap.Progress} at {run.Time:F2} s");
                Assert.InRange(snap.Progress, 0, 1);
                Assert.True(snap.Progress >= previous, $"progress went back from {previous} to {snap.Progress} at {run.Time:F2} s");
                Assert.Equal(run.Progress, snap.Progress);
                eachStep?.Invoke(snap);
                seen.Add(snap.Progress);
            }
            return seen;
        }

        [Fact]
        public void SingleCar_FillsTheBarAsItDrivesAndEndsFullOnAPass()
        {
            var pads = Rooms.Pads();
            var run = Start(Rooms.Truss(pads), pads, PhaseKind.Calm);
            Assert.Equal(0, run.Progress);

            var seen = RunToResult(run);

            Assert.Equal(RunResult.Passed, run.Result);
            Assert.Equal(0, seen[0]);
            Assert.Equal(1, run.Progress);
            Assert.Equal(1, Snapshot(run).Progress);
            Assert.Contains(seen, p => p > 0.2 && p < 0.8);
            double biggestStep = seen.Zip(seen.Skip(1), (a, b) => b - a).Max();
            Assert.True(biggestStep < 0.05, $"progress jumped {biggestStep:F3} in one step");
        }

        [Fact]
        public void SingleCar_SendsNoTraffic()
        {
            var pads = Rooms.Pads();
            var run = Start(Rooms.Truss(pads), pads, PhaseKind.Calm);
            for (int i = 0; i < 30; i++)
                run.Step(Dt);

            var snap = Snapshot(run);
            Assert.Equal(0, run.CarsTotal);
            Assert.Equal(0, snap.CarsTotal);
            Assert.Equal(0, snap.CarsSent);
            Assert.Equal(0, snap.CarsCrossed);
            Assert.Equal(0, snap.JamHeldSeconds);
        }

        [Fact]
        public void FailedRun_StopsShortAndFreezes()
        {
            var pads = Rooms.Pads();
            var run = Start(Rooms.BareDeck(pads), pads, PhaseKind.Calm);
            RunToResult(run);
            Assert.Equal(RunResult.Failed, run.Result);
            var atResult = Snapshot(run);
            Assert.True(atResult.Progress > 0 && atResult.Progress < 1, $"progress {atResult.Progress} at the result");

            for (int i = 0; i < 120; i++)
                run.Step(Dt);

            var later = Snapshot(run);
            Assert.Equal(atResult.Progress, run.Progress);
            Assert.Equal(atResult.Progress, later.Progress);
            Assert.Equal(atResult.CarsCrossed, later.CarsCrossed);
            Assert.Equal(atResult.CarsSent, later.CarsSent);
            Assert.Equal(atResult.CarsTotal, later.CarsTotal);
            Assert.Equal(atResult.JamHeldSeconds, later.JamHeldSeconds);
        }

        [Fact]
        public void FailedTraffic_KeepsItsProgressAfterCarsFall()
        {
            var pads = Rooms.Pads();
            var run = Start(Rooms.BareDeck(pads), pads, PhaseKind.Traffic);
            RunToResult(run);
            Assert.Equal(RunResult.Failed, run.Result);
            var atResult = Snapshot(run);
            Assert.True(atResult.Progress < 1);

            for (int i = 0; i < 120; i++)
                run.Step(Dt);

            var later = Snapshot(run);
            Assert.Equal(atResult.Progress, later.Progress);
            Assert.Equal(atResult.CarsCrossed, later.CarsCrossed);
            Assert.Equal(atResult.CarsSent, later.CarsSent);
            Assert.Equal(atResult.CarsTotal, later.CarsTotal);
        }

        [Fact]
        public void TwoWayTraffic_CountsTheCarsAcrossAndEndsFull()
        {
            var pads = Rooms.Pads();
            var run = Start(Rooms.Truss(pads), pads, PhaseKind.Traffic);
            Assert.Equal(run.Traffic.Quota, Snapshot(run).CarsTotal);
            int crossed = 0;

            var seen = RunToResult(run, snap =>
            {
                Assert.True(snap.CarsCrossed >= crossed);
                Assert.InRange(snap.CarsCrossed, 0, snap.CarsSent);
                Assert.InRange(snap.CarsSent, 0, snap.CarsTotal);
                crossed = snap.CarsCrossed;
            });

            Assert.Equal(RunResult.Passed, run.Result);
            var end = Snapshot(run);
            Assert.Equal(1, end.Progress);
            Assert.True(end.CarsTotal > 0);
            Assert.Equal(end.CarsTotal, end.CarsCrossed);
            Assert.Equal(end.CarsTotal, end.CarsSent);
            Assert.Contains(seen, p => p > 0.2 && p < 0.8);
        }

        [Fact]
        public void Jam_FillingEarnsItsShareThenHoldingEarnsTheRest()
        {
            var pads = Rooms.Pads();
            var run = Start(Rooms.Truss(pads), pads, PhaseKind.Jam);
            double share = Constants.Traffic.JamFillShare;
            double filling = 0, holdStartProgress = -1, holdStartHeld = 0;
            int holdStartSent = -1;

            var seen = RunToResult(run, snap =>
            {
                if (snap.JamHeldSeconds == 0)
                {
                    filling = snap.Progress;
                }
                else if (holdStartSent < 0)
                {
                    holdStartProgress = snap.Progress;
                    holdStartHeld = snap.JamHeldSeconds;
                    holdStartSent = snap.CarsSent;
                }
            });

            Assert.Equal(RunResult.Passed, run.Result);
            var end = Snapshot(run);
            Assert.Equal(1, end.Progress);
            Assert.True(end.JamHeldSeconds >= Constants.Traffic.JamHoldSeconds);
            // Filling moves the bar most of the way to its share; the hold starts only once the bridge is full,
            // with the filling share earned and none of the holding share yet.
            Assert.InRange(filling, share / 2, share);
            Assert.Equal(end.CarsSent, holdStartSent);
            Assert.Equal(share, holdStartProgress - (1 - share) * holdStartHeld / Constants.Traffic.JamHoldSeconds, 9);
            Assert.Contains(seen, p => p > share + 0.1 && p < 0.9);
        }
    }
}
