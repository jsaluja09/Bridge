using System;
using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>The Core side of the result visuals: star shape and reveal, snowfall, and every car's pose.</summary>
    public class StarTests
    {
        [Fact]
        public void Star_HasFiveTipsAndFiveNotches_FirstTipUp()
        {
            var p = StarShape.Points(2);
            Assert.Equal(10, p.Length);
            for (int i = 0; i < 10; i++)
            {
                double r = Math.Sqrt(p[i].x * p[i].x + p[i].y * p[i].y);
                Assert.Equal(i % 2 == 0 ? 2 : 2 * StarShape.InnerRatio, r, 9);
            }
            Assert.Equal(0, p[0].x, 9);
            Assert.Equal(2, p[0].y, 9);
        }

        [Fact]
        public void Star_IsSymmetricLeftToRight()
        {
            var p = StarShape.Points(1);
            for (int i = 1; i < 10; i++)
            {
                Assert.Equal(-p[i].x, p[10 - i].x, 9);
                Assert.Equal(p[i].y, p[10 - i].y, 9);
            }
        }

        [Fact]
        public void EarnedStar_PopsPastFullSizeThenSettles()
        {
            double start = StarReveal.StartTime(0);
            Assert.Equal(0, StarReveal.Fill(0, 1, start - 0.01));
            Assert.Equal(0, StarReveal.Fill(0, 1, start));
            double peak = StarReveal.Fill(0, 1, start + 0.7 * StarReveal.PopSeconds);
            Assert.Equal(StarReveal.Overshoot, peak, 9);
            Assert.Equal(1, StarReveal.Fill(0, 1, start + StarReveal.PopSeconds));
            Assert.Equal(1, StarReveal.Fill(0, 1, 100));
            for (double t = 0; t < 3; t += 0.01)
                Assert.InRange(StarReveal.Fill(0, 1, t), 0, StarReveal.Overshoot);
        }

        [Fact]
        public void UnearnedStars_StayEmptyOutlines()
        {
            for (double t = 0; t < 5; t += 0.1)
            {
                Assert.Equal(0, StarReveal.Fill(1, 1, t));
                Assert.Equal(0, StarReveal.Fill(2, 1, t));
                Assert.Equal(0, StarReveal.Fill(0, 0, t));
            }
        }

        [Fact]
        public void StarsFillOneAfterAnother()
        {
            Assert.True(StarReveal.StartTime(1) > StarReveal.StartTime(0));
            Assert.True(StarReveal.StartTime(2) > StarReveal.StartTime(1));
            double secondStarts = StarReveal.StartTime(1);
            Assert.Equal(1, StarReveal.Fill(0, 3, secondStarts + 0.2));    // the first is done before the second ends
            Assert.True(StarReveal.Fill(1, 3, secondStarts + 0.1) > 0);
            Assert.Equal(0, StarReveal.Fill(2, 3, secondStarts + 0.1));
        }

        [Fact]
        public void EachStarSignalsItsPopOnce()
        {
            int signals = 0;
            for (double t = 0; t < 3; t += 1.0 / 72)
            {
                if (StarReveal.StartsBetween(1, 3, t, t + 1.0 / 72))
                    signals++;
            }
            Assert.Equal(1, signals);
            Assert.False(StarReveal.StartsBetween(2, 2, 0, 10));
        }
    }

    public class SnowfallTests
    {
        static readonly Vec3 Min = new Vec3(0, 0.75, -0.2), Max = new Vec3(1, 1.5, 0.2);

        [Fact]
        public void Flakes_StayInTheBox()
        {
            double d = Snowfall.DriftAmplitude;
            for (int i = 0; i < 200; i++)
            {
                for (double t = 0; t < 30; t += 0.7)
                {
                    var p = Snowfall.Flake(i, t, Min, Max);
                    Assert.InRange(p.X, Min.X - d, Max.X + d);
                    Assert.InRange(p.Y, Min.Y, Max.Y);
                    Assert.InRange(p.Z, Min.Z - d, Max.Z + d);
                }
            }
        }

        [Fact]
        public void Flakes_FallAndLoop()
        {
            var a = Snowfall.Flake(7, 1.0, Min, Max);
            var b = Snowfall.Flake(7, 1.5, Min, Max);
            Assert.True(b.Y < a.Y || b.Y > Max.Y - 0.1);               // fell, or looped back to the top
            double loop = (Max.Y - Min.Y) / Snowfall.FallSpeed;
            Assert.Equal(a.Y, Snowfall.Flake(7, 1.0 + loop, Min, Max).Y, 6);
        }

        [Fact]
        public void Flakes_AreSpreadOutAndRepeatable()
        {
            var flakes = Enumerable.Range(0, 100).Select(i => Snowfall.Flake(i, 2, Min, Max)).ToList();
            Assert.Equal(flakes, Enumerable.Range(0, 100).Select(i => Snowfall.Flake(i, 2, Min, Max)));
            Assert.True(flakes.Select(f => Math.Round(f.X, 2)).Distinct().Count() > 50);
            Assert.True(flakes.Count(f => f.X < 0.5) > 25 && flakes.Count(f => f.X >= 0.5) > 25);
        }
    }

    public class CarPoseSetTests
    {
        const double Dt = 1.0 / 60;

        static (BridgeRun run, CarPoseSet set, CarPoses poses) Traffic(BridgeModel model, Pad[] pads)
        {
            var config = new ChallengeConfig().ToRunConfiguration(new StartSurfaceChoice(0, 1), pads[0].Across, 20);
            config.Traffic = new TrafficSpec { CarsPerLane = 3 };
            var run = new BridgeRun(BridgeCompiler.Compile(model, pads, 20), config, 0);
            return (run, new CarPoseSet(run.Coupled, config.Car, 20, 0), new CarPoses());
        }

        [Fact]
        public void EveryCarOnTheBridge_HasAVisiblePose()
        {
            var pads = Rooms.Pads();
            var (run, set, poses) = Traffic(Rooms.Truss(pads), pads);
            for (int i = 0; i < 90; i++)
            {
                run.Step(Dt);
                set.Update(run.Result, Dt, poses);
                for (int s = 0; s < poses.Count; s++)
                {
                    var car = run.Coupled.CarAt(s);
                    Assert.Equal(car != null, poses.Visible[s]);
                    if (car != null)
                        Assert.True(Vec3.Distance(car.Position / 20, poses.Poses[s].Origin) < 0.05);
                }
            }
            Assert.Equal(Constants.Traffic.MaxCars, poses.Count);
            Assert.True(poses.Visible.Count(v => v) >= 2);
        }

        [Fact]
        public void ANewCarInAReusedSlot_StartsAFreshPose()
        {
            var pads = Rooms.Pads();
            var bridge = BridgeCompiler.Compile(Rooms.Truss(pads), pads, 20);
            var config = new ChallengeConfig().ToRunConfiguration(new StartSurfaceChoice(0, 1), pads[0].Across, 20);
            var step = new CoupledStep(new SuspensionCar(Constants.Loads.Gravity, 0.9, 8), pads, bridge.Decks);
            step.Reset(bridge.Input, config);
            var set = new CarPoseSet(step, config.Car, 20, 0);
            var poses = new CarPoses();
            for (int i = 0; i < 60; i++)
            {
                step.Step(null, Dt, null);
                set.Update(RunResult.Running, Dt, poses);
            }
            int before = step.CarSerial(0);
            double spinBefore = poses.Poses[0].SpinAngle;
            Assert.True(spinBefore > 0);

            step.RemoveCar(0);
            Assert.Equal(0, step.AddCar(step.RestingCar(new Vec3(10, 15, 1), -Vec3.UnitX)));
            step.Step(null, Dt, null);
            set.Update(RunResult.Running, Dt, poses);

            Assert.NotEqual(before, step.CarSerial(0));
            Assert.True(Vec3.Distance(step.CarAt(0).Position / 20, poses.Poses[0].Origin) < 0.05);
            Assert.True(poses.Poses[0].SpinAngle < spinBefore);          // a fresh tracker, not the old car's
            Assert.Equal(-1, poses.Poses[0].Forward.X, 3);
        }

        [Fact]
        public void AddCar_BumpsTheSlotSerial()
        {
            var pads = Rooms.Pads();
            var bridge = BridgeCompiler.Compile(Rooms.Truss(pads), pads, 20);
            var step = new CoupledStep(new SuspensionCar(Constants.Loads.Gravity, 0.9), pads, bridge.Decks, carCapacity: 2);
            step.Reset(bridge.Input, new ChallengeConfig().ToRunConfiguration(new StartSurfaceChoice(0, 1), pads[0].Across, 20));
            int first = step.CarSerial(0);
            Assert.Equal(0, step.CarSerial(1));
            step.RemoveCar(0);
            Assert.Equal(first, step.CarSerial(0));                         // removing doesn't change it
            step.AddCar(step.RestingCar(new Vec3(5, 15, 0), Vec3.UnitX));
            Assert.Equal(first + 1, step.CarSerial(0));
        }

        [Fact]
        public void FallenCars_StayWhereTheyLanded()
        {
            var pads = Rooms.Pads();
            var (run, set, poses) = Traffic(Rooms.BareDeck(pads), pads);
            for (int i = 0; i < 1200; i++)
            {
                run.Step(Dt);
                set.Update(run.Result, Dt, poses);
            }
            Assert.True(run.Traffic.Fell > 0);
            var landed = Enumerable.Range(0, poses.Count).Where(s => poses.Visible[s] && poses.Poses[s].Landed).ToList();
            Assert.NotEmpty(landed);
            Assert.All(landed, s => Assert.Equal(0, poses.Poses[s].Origin.Y, 6));     // on the floor
        }

        [Fact]
        public void CarPoses_CopyResizesAndCopies()
        {
            var a = new CarPoses();
            a.Resize(3);
            a.Visible[1] = true;
            a.Poses[1].Origin = new Vec3(1, 2, 3);
            var b = new CarPoses();
            a.CopyTo(b);
            Assert.Equal(3, b.Count);
            Assert.True(b.Visible[1]);
            Assert.Equal(new Vec3(1, 2, 3), b.Poses[1].Origin);
            Assert.NotSame(a.Poses[1], b.Poses[1]);
        }

        [Fact]
        public void Snapshot_SaysHowTheFirstMemberBroke()
        {
            var pads = Rooms.Pads();
            var run = Rooms.Run(Rooms.BareDeck(pads), pads);
            run.RunToEnd();
            var snap = new RunSnapshot();
            run.Write(snap);
            Assert.NotEqual(FailureMode.None, snap.FirstBreak);
            Assert.False(snap.Stuck);
        }
    }
}
