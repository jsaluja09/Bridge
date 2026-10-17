using System;
using System.Linq;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class SuspensionCarTests
    {
        private const double Gravity = 9.80665;
        private const double Grip = 0.1;
        private const double Dt = 0.01;
        private const double Mass = 1000.0;
        private const double Spring = 20000.0;
        private const double Speed = 10.0;
        private const double DownhillSlope = 0.2;
        private const double GentleSlope = 0.02;
        private const double SteepSlope = 0.35;
        private const double Drop = 0.05;
        private const int SettleSteps = 3000;
        private const int BounceSteps = 200;

        [Fact]
        public void FlatDeck_AtRestLength_WheelLoadsSumToWeight()
        {
            CarParameters car = TestCar();
            var model = new SuspensionCar(Gravity, Grip);
            CarState state = CarOnDeck(car, 0.0, 0.0, 0.0);
            DeckSample[] deck = Deck(car, state, 0.0);

            CarState touching = model.Step(state, car, deck, Vec3.Zero, Dt);
            Assert.All(touching.SuspensionCompression, c => Assert.Equal(0.0, c));
            Assert.All(touching.WheelLoads, w => Assert.Equal(0.0, w.NormalLoad));

            for (int step = 0; step < SettleSteps; step++)
            {
                state = model.Step(state, car, deck, Vec3.Zero, Dt);
                AssertFinite(state);
            }

            Assert.Equal(Mass * Gravity, state.WheelLoads.Sum(w => w.NormalLoad), 1e-6);
            Assert.All(state.WheelLoads, w => Assert.Equal(Mass * Gravity / 4, w.NormalLoad, 1e-6));
            Assert.All(state.SuspensionCompression, c => Assert.Equal(StaticCompression(0.0), c, 1e-9));
        }

        [Fact]
        public void LoweredDeck_IncreasesCompression()
        {
            CarParameters car = TestCar();
            var model = new SuspensionCar(Gravity, Grip);
            CarState state = CarOnDeck(car, 0.0, StaticCompression(0.0), 0.0);
            CarState atRest = model.Step(state, car, Deck(car, state, 0.0), Vec3.Zero, Dt);
            Assert.All(atRest.SuspensionCompression, c => Assert.Equal(StaticCompression(0.0), c, 1e-12));

            DeckSample[] lowered = Deck(car, atRest, -Drop);
            CarState dropped = model.Step(atRest, car, lowered, Vec3.Zero, Dt);
            CarState current = dropped;
            double[] peak = (double[])dropped.SuspensionCompression.Clone();
            for (int step = 0; step < BounceSteps; step++)
            {
                current = model.Step(current, car, lowered, Vec3.Zero, Dt);
                AssertFinite(current);
                for (int i = 0; i < peak.Length; i++)
                {
                    peak[i] = Math.Max(peak[i], current.SuspensionCompression[i]);
                }
            }

            // The struts extend while the deck drops away, then compress past the at-rest value when the body lands on it.
            for (int i = 0; i < car.WheelCount; i++)
            {
                Assert.True(dropped.SuspensionCompression[i] < atRest.SuspensionCompression[i], "Wheel " + i + " did not extend as the deck dropped.");
                Assert.True(peak[i] > atRest.SuspensionCompression[i], "Wheel " + i + " peak compression " + peak[i] + " did not pass the at-rest " + atRest.SuspensionCompression[i] + ".");
            }
        }

        [Fact]
        public void Slope_AcceleratesDownhill()
        {
            CarParameters car = TestCar();
            var model = new SuspensionCar(Gravity, Grip);
            foreach (double slope in new[] { DownhillSlope, GentleSlope })
            {
                CarState next = DownhillStep(model, car, slope);
                Assert.True(double.IsFinite(next.Speed), "Non-finite speed on slope " + slope + ".");
                Assert.True(next.Speed > Speed, "Speed " + next.Speed + " did not increase on slope " + slope + ".");
            }
        }

        [Fact]
        public void Grip_LimitsLongitudinalForce()
        {
            CarParameters car = TestCar();
            var model = new SuspensionCar(Gravity, Grip);
            foreach (double slope in new[] { -SteepSlope, SteepSlope })
            {
                SuspensionCarStep step = SteepStep(model, car, slope);
                double totalNormal = 0.0;
                Vec3 total = Vec3.Zero;
                for (int i = 0; i < car.WheelCount; i++)
                {
                    double normal = step.State.WheelLoads[i].NormalLoad;
                    Vec3 force = step.TireForces[i];
                    AssertFinite(force, "tire force " + i);
                    Assert.True(Horizontal(force) <= Grip * normal + 1e-6, "Wheel " + i + " horizontal force " + Horizontal(force) + " exceeds grip on slope " + slope + ".");
                    Assert.True(force.Length <= Grip * normal + 1e-6, "Wheel " + i + " force " + force.Length + " exceeds grip on slope " + slope + ".");
                    totalNormal += normal;
                    total += force;
                }

                Assert.True(totalNormal > 0.0);
                Assert.True(Horizontal(total) <= Grip * totalNormal + 1e-6, "Total horizontal force " + Horizontal(total) + " exceeds grip on slope " + slope + ".");
            }

            SuspensionCarStep climb = SteepStep(model, car, -SteepSlope);
            double climbNormal = climb.State.WheelLoads.Sum(w => w.NormalLoad);
            Vec3 climbForce = climb.TireForces.Aggregate(Vec3.Zero, (sum, f) => sum + f);
            Assert.Equal(Grip * climbNormal, climbForce.Length, 1e-6);
            Assert.True(climb.State.Speed < Speed, "The grip-limited climb should slow the car.");
        }

        [Fact]
        public void Step_IsDeterministic()
        {
            CarParameters car = TestCar();
            var force = new Vec3(-350.0, 120.0, 80.0);
            var model = new SuspensionCar(Gravity, Grip);
            (CarState state, DeckSample[] deck) = DeterminismInputs(model, car, force);

            CarState first = model.Step(state, car, deck, force, Dt);
            CarState second = model.Step(state, car, deck, force, Dt);
            AssertFinite(first);
            AssertSameState(first, second);

            var otherModel = new SuspensionCar(Gravity, Grip);
            (CarState otherState, DeckSample[] otherDeck) = DeterminismInputs(otherModel, car, force);
            AssertSameState(first, otherModel.Step(otherState, car, otherDeck, force, Dt));
        }

        [Fact]
        public void Step_RejectsNonPositiveDt()
        {
            CarParameters car = TestCar();
            var model = new SuspensionCar(Gravity, Grip);
            CarState state = CarOnDeck(car, 0.0, StaticCompression(0.0), Speed);
            DeckSample[] deck = Deck(car, state, 0.0);

            foreach (double dt in new[] { 0.0, -0.01 })
            {
                ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => model.Step(state, car, deck, Vec3.Zero, dt));
                Assert.Equal("dt", error.ParamName);
            }
        }

        [Fact]
        public void Step_NoNaN()
        {
            CarParameters car = TestCar();
            var model = new SuspensionCar(Gravity, Grip);

            AssertFinite(DownhillStep(model, car, DownhillSlope));
            foreach (double slope in new[] { -SteepSlope, SteepSlope })
            {
                SuspensionCarStep steep = SteepStep(model, car, slope);
                AssertFinite(steep.State);
                Assert.All(steep.TireForces, f => AssertFinite(f, "tire force"));
            }
        }

        [Fact]
        public void Step_RejectsInvalidInputs()
        {
            CarParameters car = TestCar();
            var model = new SuspensionCar(Gravity, Grip);
            CarState state = CarOnDeck(car, 0.0, StaticCompression(0.0), Speed);
            DeckSample[] deck = Deck(car, state, 0.0);

            Assert.Throws<ArgumentOutOfRangeException>(() => new SuspensionCar(Gravity, -Grip));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SuspensionCar(double.NaN, Grip));
            Assert.Throws<ArgumentOutOfRangeException>(() => model.Step(state, car, deck, Vec3.Zero, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => model.Step(state, car, deck, Vec3.Zero, double.PositiveInfinity));
            Assert.Throws<ArgumentNullException>(() => model.Step(state, car, null, Vec3.Zero, Dt));
            Assert.Throws<ArgumentException>(() => model.Step(state, car, deck.Take(3).ToArray(), Vec3.Zero, Dt));
            Assert.Throws<ArgumentException>(() => model.Step(state, car, new[] { deck[0], deck[0], deck[2], deck[3] }, Vec3.Zero, Dt));
            Assert.Throws<ArgumentException>(() => model.Step(state, car, deck, new Vec3(double.PositiveInfinity, 0.0, 0.0), Dt));

            CarParameters oddWheels = TestCar();
            oddWheels.WheelCount = 3;
            Assert.Throws<ArgumentOutOfRangeException>(() => model.Step(state, oddWheels, deck.Take(3).ToArray(), Vec3.Zero, Dt));

            DeckSample[] reversed = deck.Reverse().ToArray();
            AssertSameState(model.Step(state, car, deck, Vec3.Zero, Dt), model.Step(state, car, reversed, Vec3.Zero, Dt));
        }

        private static CarParameters TestCar()
        {
            return new CarParameters
            {
                MassKg = Mass,
                WheelCount = 4,
                Wheelbase = 2.5,
                Track = 1.5,
                SpringStiffness = Spring,
                DamperCoefficient = 2000.0,
                RestLength = 0.3,
            };
        }

        /// <summary>Static strut compression of the test car on a deck of this slope.</summary>
        private static double StaticCompression(double slope)
        {
            return Mass * Gravity / Math.Sqrt(1.0 + slope * slope) / (4 * Spring);
        }

        /// <summary>
        /// The car at the origin heading +X, moving with a deck of this slope that passes through height 0
        /// there, with every strut at <paramref name="compression"/>.
        /// </summary>
        private static CarState CarOnDeck(CarParameters car, double slope, double compression, double speed)
        {
            return new CarState
            {
                Position = new Vec3(0.0, (car.RestLength - compression) * Math.Sqrt(1.0 + slope * slope), 0.0),
                Heading = Vec3.UnitX,
                Speed = speed,
                VerticalVelocity = -speed * slope,
            };
        }

        /// <summary>Samples a straight deck, at height <paramref name="heightAtOrigin"/> above the origin, under each wheel of the car.</summary>
        private static DeckSample[] Deck(CarParameters car, CarState state, double heightAtOrigin, double slope = 0.0)
        {
            var deck = new DeckSample[car.WheelCount];
            for (int i = 0; i < car.WheelCount; i++)
            {
                Vec3 point = state.Position + SuspensionCar.WheelOffset(car, state.Heading, i);
                double height = heightAtOrigin - slope * Vec3.Dot(point, state.Heading);
                deck[i] = new DeckSample { WheelIndex = i, Height = height, Slope = slope, Position = new Vec3(point.X, height, point.Z) };
            }

            return deck;
        }

        private static CarState DownhillStep(SuspensionCar model, CarParameters car, double slope)
        {
            CarState state = CarOnDeck(car, slope, StaticCompression(slope), Speed);
            return model.Step(state, car, Deck(car, state, 0.0, slope), Vec3.Zero, Dt);
        }

        private static SuspensionCarStep SteepStep(SuspensionCar model, CarParameters car, double slope)
        {
            CarState state = CarOnDeck(car, slope, StaticCompression(slope), Speed);
            return model.Advance(state, car, Deck(car, state, 0.0, slope), Vec3.Zero, Dt);
        }

        private static (CarState State, DeckSample[] Deck) DeterminismInputs(SuspensionCar model, CarParameters car, Vec3 force)
        {
            const double slope = 0.1;
            CarState start = CarOnDeck(car, slope, StaticCompression(slope), Speed);
            CarState moved = model.Step(start, car, Deck(car, start, 0.0, slope), force, Dt);
            DeckSample[] deck = Deck(car, moved, -0.004, slope);
            deck[1].Height -= 0.01;
            return (moved, deck);
        }

        private static double Horizontal(Vec3 v)
        {
            return Math.Sqrt(v.X * v.X + v.Z * v.Z);
        }

        private static void AssertFinite(Vec3 v, string what)
        {
            Assert.True(double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z), "Non-finite " + what + ": " + v + ".");
        }

        private static void AssertFinite(CarState state)
        {
            AssertFinite(state.Position, "position");
            AssertFinite(state.Heading, "heading");
            Assert.True(double.IsFinite(state.Speed), "Non-finite speed.");
            Assert.True(double.IsFinite(state.VerticalVelocity), "Non-finite vertical velocity.");
            Assert.Equal(4, state.WheelLoads.Length);
            Assert.Equal(4, state.SuspensionCompression.Length);
            for (int i = 0; i < state.WheelLoads.Length; i++)
            {
                Assert.Equal(i, state.WheelLoads[i].WheelIndex);
                Assert.True(double.IsFinite(state.WheelLoads[i].NormalLoad), "Non-finite load at wheel " + i + ".");
                AssertFinite(state.WheelLoads[i].Position, "contact point " + i);
                Assert.True(double.IsFinite(state.SuspensionCompression[i]), "Non-finite compression at wheel " + i + ".");
            }
        }

        private static void AssertSameState(CarState expected, CarState actual)
        {
            Assert.Equal(expected.Position, actual.Position);
            Assert.Equal(expected.Heading, actual.Heading);
            Assert.Equal(expected.Speed, actual.Speed);
            Assert.Equal(expected.VerticalVelocity, actual.VerticalVelocity);
            Assert.Equal(expected.SuspensionCompression, actual.SuspensionCompression);
            Assert.Equal(expected.WheelLoads.Length, actual.WheelLoads.Length);
            for (int i = 0; i < expected.WheelLoads.Length; i++)
            {
                Assert.Equal(expected.WheelLoads[i].WheelIndex, actual.WheelLoads[i].WheelIndex);
                Assert.Equal(expected.WheelLoads[i].NormalLoad, actual.WheelLoads[i].NormalLoad);
                Assert.Equal(expected.WheelLoads[i].Position, actual.WheelLoads[i].Position);
            }
        }
    }
}
