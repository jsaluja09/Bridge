using System;
using System.Collections.Generic;
using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>Lanes and traffic: two-way or single file, car following, crossings, falls and jams.</summary>
    public class TrafficTests
    {
        const double Scale = 20;
        const double Dt = 1.0 / 60;
        const double Top = Rooms.Top;

        static Vec3 P(double x, double y, double z) => new Vec3(x, y, z);

        /// <summary>Two rails pad to pad, `width` apart (room m), over a span of `span` (room m).</summary>
        static (Pad[] pads, BridgeModel model) Deck(double width, double span = 1.0)
        {
            var (a, b) = Pad.FromTaps(P(0, Top, 0), P(span, Top, 0));
            var pads = new[] { a, b };
            var m = Rooms.Model();
            Rooms.Draw(m, pads, P(0, Top, -width / 2), P(span, Top, -width / 2));
            Rooms.Draw(m, pads, P(0, Top, width / 2), P(span, Top, width / 2));
            return (pads, m);
        }

        /// <summary>Deck(0.2, span) plus a Pratt truss on each side, like Rooms.Truss scaled to the span.</summary>
        static (Pad[] pads, BridgeModel model) TrussDeck(double span)
        {
            var (pads, m) = Deck(0.2, span);
            double h = Top + 0.1, q = span / 4;
            foreach (double z in new[] { -0.1, 0.1 })
            {
                Rooms.Draw(m, pads, P(0, Top, z), P(q, h, z), P(3 * q, h, z), P(span, Top, z));
                Rooms.Draw(m, pads, P(q, Top, z), P(q, h, z));
                Rooms.Draw(m, pads, P(3 * q, Top, z), P(3 * q, h, z));
                Rooms.Draw(m, pads, P(q, h, z), P(2 * q, Top, z));
                Rooms.Draw(m, pads, P(3 * q, h, z), P(2 * q, Top, z));
            }
            return (pads, m);
        }

        static BridgeRun Run(Pad[] pads, BridgeModel model, TrafficSpec traffic)
        {
            var bridge = BridgeCompiler.Compile(model, pads, Scale);
            var config = new ChallengeConfig().ToRunConfiguration(new StartSurfaceChoice(0, 1), pads[0].Across, Scale);
            config.Traffic = traffic;
            return new BridgeRun(bridge, config, 0);
        }

        static IEnumerable<int> Slots(BridgeRun run) =>
            Enumerable.Range(0, run.Coupled.CarCapacity).Where(s => run.Coupled.CarAt(s) != null);

        /// <summary>Smallest bumper gap between neighbours in any lane, among cars still on wheels.</summary>
        static double MinGap(BridgeRun run)
        {
            double min = double.MaxValue;
            var t = run.Traffic;
            for (int l = 0; l < t.Lanes.Count; l++)
            {
                var progress = Slots(run).Where(s => t.LaneOf(s) == l && !run.Coupled.CarAt(s).Falling)
                    .Select(s => t.Lanes[l].Progress(run.Coupled.CarAt(s).Position)).OrderByDescending(p => p).ToList();
                for (int i = 1; i < progress.Count; i++)
                    min = Math.Min(min, progress[i - 1] - progress[i] - Constants.Car.Length);
            }
            return min;
        }

        // ---------- Lanes ----------

        [Fact]
        public void WideDeck_GetsTwoWayLanesDrivingOnTheRight()
        {
            var (pads, model) = Deck(0.2);                       // 4 m road
            var lanes = LanePlan.Plan(BridgeCompiler.Compile(model, pads, Scale), 0, Scale);

            Assert.True(LanePlan.TwoWay(lanes));
            Assert.Equal(pads[0].Forward, lanes[0].Heading);
            Assert.Equal(pads[1].Forward, lanes[1].Heading);
            // Each lane sits a quarter of the road off the middle, on its own right-hand side.
            Assert.Equal(1.0, Vec3.Dot(lanes[0].Entry, pads[0].Across), 2);
            Assert.Equal(1.0, Vec3.Dot(lanes[1].Entry, pads[1].Across), 2);
            // Cars start in the middle of their own pad.
            Assert.Equal(-pads[0].Depth / 2 * Scale, lanes[0].Entry.X, 6);
            Assert.Equal((1 + pads[1].Depth / 2) * Scale, lanes[1].Entry.X, 6);
            Assert.Equal(Top * Scale, lanes[0].Entry.Y, 6);
        }

        [Fact]
        public void LaneDistances_MarkTheDeckAndTheExit()
        {
            var (pads, model) = Deck(0.2);
            var lane = LanePlan.Plan(BridgeCompiler.Compile(model, pads, Scale), 0, Scale)[0];
            double half = pads[0].Depth / 2 * Scale;

            Assert.Equal(half, lane.DeckStart, 6);
            Assert.Equal(half + 20, lane.DeckEnd, 6);
            Assert.Equal(half + 20 + half, lane.Exit, 6);
        }

        [Fact]
        public void NarrowDeck_RunsSingleFileFromTheStartPad()
        {
            var (pads, model) = Deck(0.15);                      // 3 m: under two 1.75 m lanes
            var lanes = LanePlan.Plan(BridgeCompiler.Compile(model, pads, Scale), 0, Scale);

            var lane = Assert.Single(lanes);
            Assert.False(LanePlan.TwoWay(lanes));
            Assert.Equal(pads[0].Forward, lane.Heading);
            Assert.Equal(0, lane.Entry.Z, 2);                    // down the middle of the road
        }

        [Fact]
        public void TwoLanesNeedTwiceTheLaneWidth()
        {
            double justEnough = 2 * Constants.Traffic.LaneWidthMeters / Scale;
            var (padsA, wide) = Deck(justEnough + 0.002);
            var (padsB, narrow) = Deck(justEnough - 0.01);
            Assert.Equal(2, LanePlan.Plan(BridgeCompiler.Compile(wide, padsA, Scale), 0, Scale).Length);
            Assert.Single(LanePlan.Plan(BridgeCompiler.Compile(narrow, padsB, Scale), 0, Scale));
        }

        [Fact]
        public void NoDeck_StillPlansOneLaneDownTheMiddle()
        {
            var (a, b) = Pad.FromTaps(P(0, Top, 0), P(1, Top, 0));
            var pads = new[] { a, b };
            var lane = Assert.Single(LanePlan.Plan(BridgeCompiler.Compile(Rooms.Model(), pads, Scale), 0, Scale));
            Assert.Equal(0, lane.Entry.Z, 6);
        }

        // ---------- Following ----------

        const double Brake = 4.0;

        [Fact]
        public void FollowingSpeed_IsZeroAtTheMinimumGapBehindAStoppedCar()
        {
            Assert.Equal(0, Traffic.FollowingSpeed(Constants.Traffic.MinGapMeters, 0, 0, Brake));
            Assert.Equal(0, Traffic.FollowingSpeed(-3, 2, 0, Brake));
        }

        [Fact]
        public void FollowingSpeed_CruisesWithNothingAhead() =>
            Assert.Equal(Constants.Car.CruiseSpeed, Traffic.FollowingSpeed(double.MaxValue, Constants.Car.CruiseSpeed, 0, Brake));

        [Fact]
        public void FollowingSpeed_LetsAStoppedCarStopInTheRoomLeft()
        {
            // From the allowed speed, braking at Brake after the response delay ends exactly a minimum gap behind.
            double gap = 8;
            double v = Traffic.FollowingSpeed(gap, 0, 0, Brake);
            Assert.Equal(Math.Sqrt(2 * Brake * (gap - Constants.Traffic.MinGapMeters)), v, 9);
        }

        [Fact]
        public void FollowingSpeed_GrowsWithGapAndWithTheLeadersSpeed()
        {
            double previous = -1;
            for (double gap = 0; gap <= 30; gap += 0.25)
            {
                double v = Traffic.FollowingSpeed(gap, 4, 0, Brake);
                Assert.True(v >= previous && double.IsFinite(v));
                previous = v;
            }
            Assert.True(Traffic.FollowingSpeed(3, 4, 6, Brake) > Traffic.FollowingSpeed(3, 4, 0, Brake));
        }

        [Fact]
        public void FollowingSpeed_FasterCarsLeaveMoreRoom() =>
            Assert.True(Traffic.FollowingSpeed(6, 8, 0, Brake) < Traffic.FollowingSpeed(6, 0, 0, Brake));

        [Fact]
        public void CarsToFill_CoverTheDeckBumperToBumper()
        {
            var lane = new Lane { DeckStart = 3, DeckEnd = 23 };
            double pitch = Constants.Car.Length + Constants.Traffic.MinGapMeters;
            Assert.Equal((int)Math.Ceiling(20 / pitch) + 1, Traffic.CarsToFill(lane));
        }

        // ---------- Runs ----------

        [Fact]
        public void TrussCarriesTwoWayTraffic_AndEveryCarCrosses()
        {
            var pads = Rooms.Pads();
            var run = Run(pads, Rooms.Truss(pads), new TrafficSpec { CarsPerLane = 3 });
            double minGap = double.MaxValue;
            while (run.Result == RunResult.Running && run.Time < 70)
            {
                run.Step(Dt);
                minGap = Math.Min(minGap, MinGap(run));
            }

            Assert.Equal(RunResult.Passed, run.Result);
            Assert.Equal(6, run.Traffic.Quota);
            Assert.Equal(6, run.Traffic.Crossed);
            Assert.Equal(0, run.Traffic.Fell);
            Assert.Empty(Slots(run));                         // everyone left the far pad
            Assert.True(minGap > 0.5, $"cars came within {minGap:F2} m");
        }

        [Fact]
        public void BareDeck_DropsTheTraffic()
        {
            var pads = Rooms.Pads();
            var run = Run(pads, Rooms.BareDeck(pads), new TrafficSpec { CarsPerLane = 3 });
            run.RunToEnd(Dt, 70);

            Assert.Equal(RunResult.Failed, run.Result);
            Assert.True(run.Traffic.Fell > 0);
            Assert.Contains("fell", run.Message);
        }

        [Fact]
        public void ShortStiffSpan_HoldsAJam()
        {
            var (pads, model) = TrussDeck(0.3);                  // 6 m trussed span
            var run = Run(pads, model, new TrafficSpec { Jam = true });
            run.RunToEnd(Dt, 70);

            Assert.Equal(RunResult.Passed, run.Result);
            Assert.Contains("jam", run.Message);
            Assert.True(run.Traffic.JamHeldSeconds >= Constants.Traffic.JamHoldSeconds);
            Assert.Equal(run.Traffic.Quota, run.Traffic.Spawned);
            Assert.All(Slots(run), s => Assert.True(Math.Abs(run.Coupled.CarAt(s).Speed) < Constants.Traffic.StoppedSpeed));
            // The lead car in each lane stopped with its nose at the end of the deck.
            var t = run.Traffic;
            for (int l = 0; l < t.Lanes.Count; l++)
            {
                double lead = Slots(run).Where(s => t.LaneOf(s) == l).Max(s => t.Lanes[l].Progress(run.Coupled.CarAt(s).Position));
                Assert.InRange(lead + Constants.Car.Length / 2, t.Lanes[l].DeckEnd - 0.5, t.Lanes[l].DeckEnd + 0.5);
            }
        }

        [Fact]
        public void JamQuota_IsCappedByTheCarSlots()
        {
            var (pads, model) = Deck(0.2, span: 3.0);            // 60 m: more cars than slots would fill it
            var run = Run(pads, model, new TrafficSpec { Jam = true });
            Assert.Equal(Constants.Traffic.MaxCars, run.Traffic.Quota);
        }

        [Fact]
        public void NarrowDeck_TrafficAllDrivesOneWay()
        {
            var (pads, model) = Deck(0.15, span: 0.5);
            var run = Run(pads, model, new TrafficSpec { CarsPerLane = 3 });
            for (int i = 0; i < 120; i++)
            {
                run.Step(Dt);
                Assert.All(Slots(run), s => Assert.Equal(pads[0].Forward, run.Coupled.CarAt(s).Heading));
            }
            Assert.Equal(3, run.Traffic.Quota);
        }

        [Fact]
        public void Traffic_IsDeterministic()
        {
            var pads = Rooms.Pads();
            var a = Run(pads, Rooms.Truss(pads), new TrafficSpec { CarsPerLane = 3 });
            var b = Run(pads, Rooms.Truss(pads), new TrafficSpec { CarsPerLane = 3 });
            for (int i = 0; i < 200; i++)
            {
                a.Step(Dt);
                b.Step(Dt);
            }
            Assert.Equal(a.Coupled.Positions, b.Coupled.Positions);
            Assert.Equal(a.Traffic.Crossed, b.Traffic.Crossed);
            Assert.Equal(Slots(a), Slots(b));
            foreach (int s in Slots(a))
                Assert.Equal(a.Coupled.CarAt(s).Position, b.Coupled.CarAt(s).Position);
        }

        [Fact]
        public void SingleCarRun_HasNoTraffic()
        {
            var pads = Rooms.Pads();
            var run = Rooms.Run(Rooms.Truss(pads), pads);
            Assert.Null(run.Traffic);
            Assert.Equal(1, run.Coupled.CarCapacity);
        }
    }
}
