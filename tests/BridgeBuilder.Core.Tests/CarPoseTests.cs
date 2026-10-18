using System;
using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class CarPoseTests
    {
        const double Scale = 20;
        static readonly CarParameters Car = Constants.Car.Standard(Constants.Human.CarMassKg);
        static double R => CarBlueprint.WheelRadius(Car) / Scale;

        static CarState Resting(Vec3 groundRoom) => SuspensionCar.Resting(Car, Constants.Loads.Gravity, groundRoom * Scale, Vec3.UnitX);

        static void AssertNear(Vec3 expected, Vec3 actual, double tol = 1e-9) =>
            Assert.True(Vec3.Distance(expected, actual) < tol, $"expected {expected}, got {actual}");

        [Fact]
        public void RestingCar_WheelsAtTheBlueprintPositions_OnTheDeck()
        {
            var ground = new Vec3(0.3, 0.75, 0.1);
            var pose = new CarPoseTracker(Car, Scale, 0).Update(Resting(ground), RunResult.Running, 1.0 / 60);

            AssertNear(ground, pose.Origin);
            AssertNear(Vec3.UnitX, pose.Forward);
            AssertNear(Vec3.UnitY, pose.Up);
            Assert.Equal(R, pose.WheelRadius, 12);
            foreach (var part in CarBlueprint.Build(Car, Constants.Car.Length).Where(p => p.Color == CarPartColor.Tire))
            {
                Vec3 expected = pose.Origin + pose.Forward * (part.Along / Scale) + pose.Up * (part.Up / Scale) + pose.Side * (part.Across / Scale);
                AssertNear(expected, pose.WheelCenters[part.Wheel]);
                Assert.Equal(ground.Y, pose.WheelCenters[part.Wheel].Y - pose.WheelRadius, 9);
            }
        }

        [Fact]
        public void Wheels_SpinByDistanceOverRadius()
        {
            var tracker = new CarPoseTracker(Car, Scale, 0);
            var s = Resting(new Vec3(0, 0.75, 0));
            s.Speed = 2.0;                                    // bridge-world m/s
            double r = CarBlueprint.WheelRadius(Car);
            tracker.Update(s, RunResult.Running, 0.1);
            Assert.Equal(2.0 * 0.1 / r, tracker.Pose.SpinAngle, 9);
            for (int i = 0; i < 100; i++)
                tracker.Update(s, RunResult.Running, 0.1);
            Assert.InRange(tracker.Pose.SpinAngle, 0, 2 * Math.PI);
        }

        [Fact]
        public void PitchAndRoll_TiltTheBody()
        {
            var s = Resting(new Vec3(0, 0.75, 0));
            s.Pitch = 0.2;
            s.Roll = 0.1;
            var pose = new CarPoseTracker(Car, Scale, 0).Update(s, RunResult.Running, 0);
            Assert.True(pose.Forward.Y > 0);                  // nose up
            Assert.True(pose.WheelCenters[0].Y > pose.WheelCenters[2].Y);   // front higher than rear
            Assert.True(pose.WheelCenters[0].Y > pose.WheelCenters[1].Y);   // wheel side up
            Assert.Equal(0, Vec3.Dot(pose.Forward, pose.Up), 9);
            Assert.Equal(0, Vec3.Dot(pose.Side, pose.Up), 9);
        }

        [Fact]
        public void AfterPassing_TheCarStaysParked()
        {
            var tracker = new CarPoseTracker(Car, Scale, 0);
            var s = Resting(new Vec3(1.2, 0.75, 0));
            tracker.Update(s, RunResult.Passed, 1.0 / 60);
            var parkedAt = tracker.Pose.Origin;
            s.Position += new Vec3(5, -40, 0);                // the solver car drives on and falls off
            tracker.Update(s, RunResult.Passed, 1.0 / 60);
            Assert.True(tracker.Pose.Parked);
            AssertNear(parkedAt, tracker.Pose.Origin);
        }

        [Fact]
        public void FallingCar_LandsOnTheFloor_AndStops()
        {
            const double floor = 0.1;
            var tracker = new CarPoseTracker(Car, Scale, floor);
            var s = Resting(new Vec3(0.5, 0.75, 0));
            s.Pitch = -0.3;
            Vec3? landedAt = null;
            for (int i = 0; i < 600; i++)
            {
                s.Position += new Vec3(0.1, -0.5, 0);         // forward and down, past the floor
                var pose = tracker.Update(s, RunResult.Failed, 1.0 / 60);
                Assert.True(pose.WheelCenters.All(c => c.Y - pose.WheelRadius >= floor - 1e-9));
                Assert.True(pose.Origin.Y >= floor - 1e-9);
                if (pose.Landed && landedAt == null)
                    landedAt = pose.Origin;
            }
            Assert.True(tracker.Pose.Landed);
            AssertNear(landedAt.Value, tracker.Pose.Origin);  // it stays where it landed
            AssertNear(Vec3.UnitY, tracker.Pose.Up);
        }

        [Fact]
        public void BareDeckRun_CarNeverDropsBelowTheFloor()
        {
            var pads = Rooms.Pads();
            var run = Rooms.Run(Rooms.BareDeck(pads), pads);
            var tracker = new CarPoseTracker(new ChallengeConfig().ToRunConfiguration(new StartSurfaceChoice(0, 1), pads[0].Across, Scale).Car, Scale, 0);
            for (int i = 0; i < 60 * 8; i++)
            {
                run.Step(1.0 / 60);
                var pose = tracker.Update(run.Coupled.Car, run.Result, 1.0 / 60);
                Assert.True(pose.Origin.IsFinite);
                Assert.True(pose.WheelCenters.Min(c => c.Y) - pose.WheelRadius >= -1e-9);
            }
            Assert.Equal(RunResult.Failed, run.Result);
            Assert.True(tracker.Pose.Landed);
        }

        [Fact]
        public void Parked_IsARestingPose()
        {
            var pose = CarPoseTracker.Parked(Car, Scale, new Vec3(0, 0.75, 0), new Vec3(0, 0, 2));
            AssertNear(new Vec3(0, 0.75, 0), pose.Origin);
            AssertNear(Vec3.UnitZ, pose.Forward);
            Assert.False(pose.Landed || pose.Parked);
        }

        [Fact]
        public void NaNState_Throws()
        {
            var s = Resting(new Vec3(0, 0.75, 0));
            s.Pitch = double.NaN;
            Assert.ThrowsAny<ArgumentException>(() => new CarPoseTracker(Car, Scale, 0).Update(s, RunResult.Running, 0));
        }
    }
}
