using System;
using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>W2-02: suspension car model. Test-only literals, not the standard car.</summary>
    public class SuspensionCarTests
    {
        const double G = 9.80665;

        static CarParameters P() => new CarParameters
        {
            MassKg = 1000, WheelCount = 4, Wheelbase = 2.5, Track = 1.5, SpringStiffness = 20000, DamperCoefficient = 2000, RestLength = 0.3,
        };

        static DeckSample[] Flat(double height = 0, double slope = 0) =>
            Enumerable.Range(0, 4).Select(i => new DeckSample { WheelIndex = i, Height = height, Slope = slope, HasDeck = true }).ToArray();

        static CarState Resting() => SuspensionCar.Resting(P(), G, Vec3.Zero, Vec3.UnitX);

        [Fact]
        public void FlatDeck_AtRestLength_WheelLoadsSumToWeight()
        {
            var next = new SuspensionCar(G, 0.9).Step(Resting(), P(), Flat(), Vec3.Zero, 0.01);
            Assert.True(Math.Abs(next.WheelLoads.Sum() - 1000 * G) <= 1e-6);
            Assert.All(next.WheelLoads, l => Assert.True(double.IsFinite(l)));
        }

        [Fact]
        public void LoweredDeck_IncreasesCompression()
        {
            // The body sits 5 cm lower relative to the front wheels' deck, so those springs compress more.
            var state = Resting();
            var deck = Flat();
            deck[0].Height = 0.05;
            deck[1].Height = 0.05;
            var next = new SuspensionCar(G, 0.9).Step(state, P(), deck, Vec3.Zero, 0.01);
            Assert.True(next.Compression[0] > state.Compression[0]);
            Assert.True(next.Compression[1] > state.Compression[1]);
        }

        [Fact]
        public void Slope_AcceleratesDownhill()
        {
            var next = new SuspensionCar(G, 0.9).Step(Resting(), P(), Flat(slope: 0.2), Vec3.Zero, 0.01);
            Assert.True(next.Speed > 0);
            Assert.True(double.IsFinite(next.Speed));
        }

        [Fact]
        public void Grip_LimitsLongitudinalForce()
        {
            var car = new SuspensionCar(G, 0.1, targetSpeed: 0);
            car.Step(Resting(), P(), Flat(slope: 1.0), Vec3.Zero, 0.01);
            Assert.True(Math.Abs(car.LastTraction) <= 0.1 * car.LastNormal + 1e-6);
            Assert.True(double.IsFinite(car.LastTraction));
        }

        [Fact]
        public void Step_IsDeterministic()
        {
            var a = new SuspensionCar(G, 0.9).Step(Resting(), P(), Flat(slope: 0.1), new Vec3(10, 0, 50), 0.01);
            var b = new SuspensionCar(G, 0.9).Step(Resting(), P(), Flat(slope: 0.1), new Vec3(10, 0, 50), 0.01);
            Assert.Equal(a.WheelLoads, b.WheelLoads);
            Assert.Equal(a.Compression, b.Compression);
            Assert.Equal(a.Speed, b.Speed);
        }

        [Fact]
        public void Step_RejectsNonPositiveDt()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SuspensionCar(G, 0.9).Step(Resting(), P(), Flat(), Vec3.Zero, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SuspensionCar(G, 0.9).Step(Resting(), P(), Flat(), Vec3.Zero, -0.01));
        }

        [Fact]
        public void Step_NoNaN()
        {
            var down = new SuspensionCar(G, 0.9).Step(Resting(), P(), Flat(slope: 0.2), Vec3.Zero, 0.01);
            var steep = new SuspensionCar(G, 0.1, 0).Step(Resting(), P(), Flat(slope: 1.0), Vec3.Zero, 0.01);
            foreach (var s in new[] { down, steep })
            {
                Assert.All(s.WheelLoads, v => Assert.True(double.IsFinite(v)));
                Assert.All(s.Compression, v => Assert.True(double.IsFinite(v)));
                Assert.True(double.IsFinite(s.Speed));
            }
        }
    }
}
