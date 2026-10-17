using System;
using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>
    /// W4-09: Acceptance tests for the car presets.
    ///
    /// Each preset must:
    ///   1. Produce no NaN in wheel loads or speed over 60 coupled steps on fixture 5 (the truss).
    ///   2. The heavy truck must produce a higher peak deck utilization than the sports car.
    /// </summary>
    public class CarPresetsTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Runs a BridgeRun for <paramref name="challenge"/> on the Rooms.Truss fixture (fixture 5)
        /// for up to <paramref name="steps"/> steps and returns the peak utilization.
        /// </summary>
        static (double peakUtil, double[] lastWheelLoads, double finalSpeed) RunPreset(
            ChallengeConfig challenge, int steps = 60)
        {
            var pads  = Rooms.Pads();
            var model = Rooms.Truss(pads);
            var run   = Rooms.Run(model, pads, challenge);

            for (int i = 0; i < steps; i++)
                run.Step(1.0 / 60);

            var wheelLoads = run.Coupled.Car.WheelLoads;
            double speed   = run.Coupled.Car.Speed;
            return (run.PeakUtilization, wheelLoads, speed);
        }

        // ── Preset API surface ───────────────────────────────────────────────

        [Fact]
        public void Presets_ReturnDistinctChallengeConfigs()
        {
            var sports = CarPresets.SportsCar();
            var sedan  = CarPresets.Sedan();
            var truck  = CarPresets.HeavyTruck();

            Assert.NotSame(sports, sedan);
            Assert.NotSame(sedan,  truck);
            Assert.NotEqual(sports.CarWeightKg, truck.CarWeightKg);
        }

        [Fact]
        public void Presets_ExposeName()
        {
            Assert.NotEmpty(CarPresets.SportsCar().Name);
            Assert.NotEmpty(CarPresets.Sedan().Name);
            Assert.NotEmpty(CarPresets.HeavyTruck().Name);
        }

        [Fact]
        public void Presets_CarParametersHavePositiveDimensions()
        {
            foreach (var p in new[] { CarPresets.SportsCarParameters(), CarPresets.SedanParameters(), CarPresets.TruckParameters() })
            {
                Assert.True(p.MassKg            > 0, "MassKg must be positive");
                Assert.True(p.Wheelbase         > 0, "Wheelbase must be positive");
                Assert.True(p.Track             > 0, "Track must be positive");
                Assert.True(p.SpringStiffness   > 0, "SpringStiffness must be positive");
                Assert.True(p.DamperCoefficient > 0, "DamperCoefficient must be positive");
                Assert.True(p.RestLength        > 0, "RestLength must be positive");
            }
        }

        // ── Acceptance: no NaN ───────────────────────────────────────────────

        [Fact]
        public void SportsCar_60Steps_NoNaN()
        {
            var (_, loads, speed) = RunPreset(CarPresets.SportsCar());

            Assert.All(loads, l => Assert.True(double.IsFinite(l),
                $"Sports car wheel load is not finite: {l}"));
            Assert.True(double.IsFinite(speed),
                $"Sports car speed is not finite: {speed}");
        }

        [Fact]
        public void Sedan_60Steps_NoNaN()
        {
            var (_, loads, speed) = RunPreset(CarPresets.Sedan());

            Assert.All(loads, l => Assert.True(double.IsFinite(l),
                $"Sedan wheel load is not finite: {l}"));
            Assert.True(double.IsFinite(speed),
                $"Sedan speed is not finite: {speed}");
        }

        [Fact]
        public void HeavyTruck_60Steps_NoNaN()
        {
            var (_, loads, speed) = RunPreset(CarPresets.HeavyTruck());

            Assert.All(loads, l => Assert.True(double.IsFinite(l),
                $"Truck wheel load is not finite: {l}"));
            Assert.True(double.IsFinite(speed),
                $"Truck speed is not finite: {speed}");
        }

        // ── Acceptance: truck > sports car utilization ───────────────────────

        [Fact]
        public void HeavyTruck_PeakUtilization_ExceedsSportsCar()
        {
            // Run for longer so the car is actually on the bridge and loading it.
            var (truckUtil,  _, _) = RunPreset(CarPresets.HeavyTruck(),  steps: 120);
            var (sportsUtil, _, _) = RunPreset(CarPresets.SportsCar(),   steps: 120);

            Assert.True(
                truckUtil > sportsUtil,
                $"Expected truck ({truckUtil:F4}) > sports car ({sportsUtil:F4}) peak utilization on fixture 5.");
        }

        // ── Constants sanity ─────────────────────────────────────────────────

        [Fact]
        public void Constants_TruckMassExceedsSportsMass()
        {
            Assert.True(
                Constants.CarPresets.TruckMassKg > Constants.CarPresets.SportsMassKg,
                "Truck must be heavier than the sports car.");
        }

        [Fact]
        public void Constants_AllPositive()
        {
            Assert.True(Constants.CarPresets.SportsMassKg       > 0);
            Assert.True(Constants.CarPresets.SportsWheelbase    > 0);
            Assert.True(Constants.CarPresets.SportsTrack        > 0);
            Assert.True(Constants.CarPresets.SedanMassKg        > 0);
            Assert.True(Constants.CarPresets.SedanWheelbase     > 0);
            Assert.True(Constants.CarPresets.SedanTrack         > 0);
            Assert.True(Constants.CarPresets.TruckMassKg        > 0);
            Assert.True(Constants.CarPresets.TruckWheelbase     > 0);
            Assert.True(Constants.CarPresets.TruckTrack         > 0);
        }

        // ── Integration with ChallengeConfig ─────────────────────────────────

        [Fact]
        public void ChallengeConfig_ToRunConfiguration_RespectsPresetMass()
        {
            var start  = new StartSurfaceChoice(0, 1);
            var config = CarPresets.HeavyTruck().ToRunConfiguration(start, Vec3.UnitZ,
                Constants.Scale.RoomToBridge);

            Assert.Equal(Constants.CarPresets.TruckMassKg, config.Car.MassKg);
        }
    }
}
