using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class ChallengeConfigTests
    {
        private const int SurfaceA = 3;
        private const int SurfaceB = 8;
        private const double ScaleFactor = 20.0;

        private const BindingFlags PublicMembers = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

        [Fact]
        public void Challenge_HoldsCarWeightWindGripBudget()
        {
            var challenge = new ChallengeConfig(carMassKg: 1000, windSpeed: 15, grip: 0.8, materialBudget: 500);
            Assert.Equal(1000.0, challenge.CarMassKg);
            Assert.Equal(15.0, challenge.WindSpeed);
            Assert.Equal(0.8, challenge.Grip);
            Assert.Equal(500.0, challenge.MaterialBudget);

            RunConfiguration run = challenge.ToRunConfiguration(StartFromA(), TestCar(), Vec3.UnitZ, ScaleFactor);
            Assert.Equal(1000.0, run.Car.MassKg);
            Assert.Equal(15.0, run.WindSpeed);
            Assert.Equal(Vec3.UnitZ, run.WindDirection);
            Assert.Equal(0.8, run.Grip);
            Assert.Equal(500.0, run.MaterialBudget);

            foreach (Type type in new[] { typeof(ChallengeConfig), typeof(StartSurfaceChoice) })
            {
                Assert.DoesNotContain(type.GetMembers(PublicMembers), m => m.Name.Contains("Force", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(type.GetProperties(PublicMembers), p => p.PropertyType == typeof(Vec3));
                Assert.DoesNotContain(type.GetFields(PublicMembers), f => f.FieldType == typeof(Vec3));
                foreach (MethodInfo method in type.GetMethods(PublicMembers))
                {
                    Assert.NotEqual(typeof(Vec3), method.ReturnType);
                    Assert.DoesNotContain(method.GetParameters(), p => p.Name.Contains("Force", StringComparison.OrdinalIgnoreCase));
                }
            }
        }

        [Fact]
        public void StartSurface_SelectsTheOtherAsFar()
        {
            var fromA = new StartSurfaceChoice(SurfaceA, SurfaceB, SurfaceA);
            Assert.Equal(SurfaceA, fromA.StartSurfaceId);
            Assert.Equal(SurfaceB, fromA.FarSurfaceId);

            var fromB = new StartSurfaceChoice(SurfaceA, SurfaceB, SurfaceB);
            Assert.Equal(SurfaceB, fromB.StartSurfaceId);
            Assert.Equal(SurfaceA, fromB.FarSurfaceId);

            ChallengeConfig challenge = TestChallenge(null);
            Assert.Equal(SurfaceA, challenge.ToRunConfiguration(fromA, TestCar(), Vec3.UnitZ, ScaleFactor).StartSurfaceId);
            Assert.Equal(SurfaceB, challenge.ToRunConfiguration(fromB, TestCar(), Vec3.UnitZ, ScaleFactor).StartSurfaceId);
        }

        [Fact]
        public void StartSurface_RejectsUnknownId()
        {
            Assert.Throws<ArgumentException>(() => new StartSurfaceChoice(SurfaceA, SurfaceB, 5));
            Assert.Throws<ArgumentException>(() => new StartSurfaceChoice(SurfaceA, SurfaceB, 0));
            Assert.Throws<ArgumentException>(() => new StartSurfaceChoice(SurfaceA, SurfaceB, -SurfaceA));
        }

        [Fact]
        public void GroundOverride_CopiedOntoRunConfiguration()
        {
            ChallengeConfig withOverride = TestChallenge(1.25);
            Assert.Equal(1.25, withOverride.GroundHeightOverride);
            RunConfiguration overridden = withOverride.ToRunConfiguration(StartFromA(), TestCar(), Vec3.UnitZ, ScaleFactor);
            Assert.True(overridden.GroundHeightOverride.HasValue);
            Assert.Equal(1.25, overridden.GroundHeightOverride.Value);

            ChallengeConfig withoutOverride = TestChallenge(null);
            Assert.Null(withoutOverride.GroundHeightOverride);
            RunConfiguration floor = withoutOverride.ToRunConfiguration(StartFromA(), TestCar(), Vec3.UnitZ, ScaleFactor);
            Assert.False(floor.GroundHeightOverride.HasValue);
        }

        [Fact]
        public void FreePlay_IsAbsent()
        {
            string[] forbidden = { "FreePlay", "SetPlayerConditions" };
            MethodInfo[] methods = typeof(ChallengeConfig).Assembly.GetExportedTypes()
                .SelectMany(type => type.GetMethods(PublicMembers))
                .ToArray();
            Assert.Contains(methods, m => m.DeclaringType == typeof(ChallengeConfig) && m.Name == nameof(ChallengeConfig.ToRunConfiguration));
            foreach (MethodInfo method in methods)
            {
                Assert.False(
                    forbidden.Contains(method.Name, StringComparer.OrdinalIgnoreCase),
                    method.DeclaringType.FullName + "." + method.Name + " is free play, a later upgrade (spec section 9).");
            }
        }

        [Fact]
        public void StartSurface_RejectsMatchingSurfaceIds()
        {
            Assert.Throws<ArgumentException>(() => new StartSurfaceChoice(SurfaceA, SurfaceA, SurfaceA));
        }

        [Fact]
        public void Challenge_RejectsInvalidValues()
        {
            foreach (double bad in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => new ChallengeConfig(bad, 15, 0.8, 500));
            }

            foreach (double bad in new[] { -1.0, double.NaN, double.PositiveInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => new ChallengeConfig(1000, bad, 0.8, 500));
                Assert.Throws<ArgumentOutOfRangeException>(() => new ChallengeConfig(1000, 15, bad, 500));
                Assert.Throws<ArgumentOutOfRangeException>(() => new ChallengeConfig(1000, 15, 0.8, bad));
            }

            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => new ChallengeConfig(1000, 15, 0.8, 500, bad));
            }

            var calm = new ChallengeConfig(1000, 0, 0, 0, -0.5);
            Assert.Equal(0.0, calm.WindSpeed);
            Assert.Equal(-0.5, calm.GroundHeightOverride);
        }

        [Fact]
        public void RunConfiguration_CopiesCarShapeAndScaleWithoutChangingTheArgument()
        {
            CarParameters car = TestCar();
            RunConfiguration run = TestChallenge(null).ToRunConfiguration(StartFromA(), car, Vec3.UnitZ, ScaleFactor);

            Assert.NotSame(car, run.Car);
            Assert.Equal(1500.0, car.MassKg);
            Assert.Equal(1000.0, run.Car.MassKg);
            Assert.Equal(car.WheelCount, run.Car.WheelCount);
            Assert.Equal(car.Wheelbase, run.Car.Wheelbase);
            Assert.Equal(car.Track, run.Car.Track);
            Assert.Equal(car.SpringStiffness, run.Car.SpringStiffness);
            Assert.Equal(car.DamperCoefficient, run.Car.DamperCoefficient);
            Assert.Equal(car.RestLength, run.Car.RestLength);
            Assert.Equal(ScaleFactor, run.ScaleFactor);

            ChallengeConfig challenge = TestChallenge(null);
            Assert.Throws<ArgumentNullException>(() => challenge.ToRunConfiguration(null, car, Vec3.UnitZ, ScaleFactor));
            Assert.Throws<ArgumentNullException>(() => challenge.ToRunConfiguration(StartFromA(), null, Vec3.UnitZ, ScaleFactor));
            foreach (double bad in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => challenge.ToRunConfiguration(StartFromA(), car, Vec3.UnitZ, bad));
            }
        }

        private static ChallengeConfig TestChallenge(double? groundHeightOverride)
        {
            return new ChallengeConfig(1000, 15, 0.8, 500, groundHeightOverride);
        }

        private static StartSurfaceChoice StartFromA()
        {
            return new StartSurfaceChoice(SurfaceA, SurfaceB, SurfaceA);
        }

        private static CarParameters TestCar()
        {
            return new CarParameters
            {
                MassKg = 1500,
                WheelCount = 4,
                Wheelbase = 2.6,
                Track = 1.5,
                SpringStiffness = 35000,
                DamperCoefficient = 3500,
                RestLength = 0.3,
            };
        }
    }
}
