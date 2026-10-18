using System;
using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>Several cars on one bridge: CoupledStep car slots.</summary>
    public class MultiCarTests
    {
        const double Scale = 20;
        const double Dt = 1.0 / 60;

        /// <summary>
        /// A stiff bridge (the test truss, or the dense under-deck truss when it must hold a full row of cars) with
        /// room for `capacity` cars. Deck at bridge world x 0..20, z -2..2, top y 15.
        /// </summary>
        static (CoupledStep step, SpyIntegrator frame) Make(int capacity, bool dense = false)
        {
            var pads = Rooms.Pads();
            var bridge = BridgeCompiler.Compile(dense ? Rooms.UnderTruss(pads) : Rooms.Truss(pads), pads, Scale);
            SpyIntegrator spy = null;
            var car = new SuspensionCar(Constants.Loads.Gravity, Constants.Car.DefaultGrip, Constants.Car.CruiseSpeed);
            var step = new CoupledStep(car, pads, bridge.Decks, input => spy = new SpyIntegrator(input), carCapacity: capacity);
            step.Reset(bridge.Input, new ChallengeConfig().ToRunConfiguration(new StartSurfaceChoice(0, 1), Vec3.UnitZ, Scale));
            return (step, spy);
        }

        static Vec3 OnDeck(double x, double z) => new Vec3(x, Rooms.Top * Scale, z);

        static double TotalDown(Vec3[] loads) => -loads.Sum(f => f.Y);

        [Fact]
        public void DefaultCapacity_IsOneCar()
        {
            var (step, _) = Make(1);
            Assert.Equal(1, step.CarCapacity);
            Assert.NotNull(step.Car);
            Assert.Equal(-1, step.AddCar(step.RestingCar(OnDeck(10, 0), Vec3.UnitX)));
        }

        static double CarWeight => Constants.Human.CarMassKg * Constants.Loads.Gravity;

        [Fact]
        public void TwoCarsOnTheDeck_EachLoadTheBridgeWithItsWeight()
        {
            var (step, frame) = Make(2);
            step.RemoveCar(0);
            Assert.Equal(0, step.AddCar(step.RestingCar(OnDeck(8, -1), Vec3.UnitX)));
            Assert.Equal(1, step.AddCar(step.RestingCar(OnDeck(12, 1), -Vec3.UnitX)));

            step.Step(null, Dt, null);              // cars find the deck; their weight lands next step
            step.Step(null, Dt, null);

            double carsOnBridge = TotalDown(frame.StepLoads[1]) - TotalDown(frame.StepLoads[0]);
            Assert.Equal(2 * CarWeight, carsOnBridge, 6);
        }

        [Fact]
        public void ExplicitWheelLoads_StillDriveTheFirstCar()
        {
            var (step, frame) = Make(1);
            step.RemoveCar(0);
            step.AddCar(step.RestingCar(OnDeck(10, 0), Vec3.UnitX));
            step.Step(new WheelLoad[0], Dt, null);  // finds the deck
            step.Step(new[] { new WheelLoad(0, 5000), new WheelLoad(3, 1234) }, Dt, null);

            Assert.Equal(6234, TotalDown(frame.StepLoads[1]) - TotalDown(frame.StepLoads[0]), 6);
        }

        [Fact]
        public void RowOfParkedCars_StaysSteady()
        {
            // Many cars on light deck joints once bounced themselves apart (added-mass instability).
            var (step, frame) = Make(12, dense: true);
            step.RemoveCar(0);
            for (int i = 0; i < 12; i++)
            {
                var car = step.RestingCar(OnDeck(2 + (i / 2) * 3.25, i % 2 == 0 ? 1 : -1), Vec3.UnitX);
                car.TargetSpeed = 0;
                step.AddCar(car);
            }
            for (int i = 0; i < 600; i++)
                step.Step(null, Dt, null);

            for (int s = 0; s < 12; s++)
                Assert.InRange(step.CarAt(s).WheelLoads.Sum(), 0.97 * CarWeight, 1.03 * CarWeight);
            Assert.All(step.Frame.Active, standing => Assert.True(standing));
            double last = TotalDown(frame.StepLoads[599]) - TotalDown(frame.StepLoads[0]);
            Assert.Equal(12 * CarWeight, last, 3);
        }

        [Fact]
        public void RemovedCar_StopsLoadingTheBridge()
        {
            var (step, frame) = Make(2);
            step.RemoveCar(0);
            step.AddCar(step.RestingCar(OnDeck(10, 0), Vec3.UnitX));
            step.Step(null, Dt, null);
            step.Step(null, Dt, null);
            Assert.True(TotalDown(frame.StepLoads[1]) > TotalDown(frame.StepLoads[0]) + 1000);

            step.RemoveCar(0);
            step.Step(null, Dt, null);

            Assert.Equal(TotalDown(frame.StepLoads[0]), TotalDown(frame.StepLoads[2]), 6);
            Assert.Null(step.Car);
        }

        [Fact]
        public void FreedSlot_IsReused()
        {
            var (step, _) = Make(3);
            Assert.Equal(1, step.AddCar(step.RestingCar(OnDeck(5, 0), Vec3.UnitX)));
            Assert.Equal(2, step.AddCar(step.RestingCar(OnDeck(10, 0), Vec3.UnitX)));
            step.RemoveCar(1);
            Assert.Null(step.CarAt(1));
            Assert.Equal(1, step.AddCar(step.RestingCar(OnDeck(15, 0), Vec3.UnitX)));
            Assert.Equal(-1, step.AddCar(step.RestingCar(OnDeck(15, 0), Vec3.UnitX)));
        }

        [Fact]
        public void EachCar_KeepsItsOwnTargetSpeed()
        {
            var (step, _) = Make(2);
            var parked = step.RestingCar(OnDeck(10, 1), Vec3.UnitX);
            parked.TargetSpeed = 0;
            step.AddCar(parked);
            for (int i = 0; i < 120; i++)
                step.Step(null, Dt, null);

            Assert.True(step.CarAt(0).Speed > 3, $"cruising car at {step.CarAt(0).Speed} m/s");
            Assert.True(Math.Abs(step.CarAt(1).Speed) < 0.05, $"parked car at {step.CarAt(1).Speed} m/s");
        }

        [Fact]
        public void TargetSpeed_SurvivesClone() =>
            Assert.Equal(3.5, new CarState { TargetSpeed = 3.5 }.Clone().TargetSpeed);

        [Fact]
        public void BadUse_Throws()
        {
            var car = new SuspensionCar(Constants.Loads.Gravity, 0.9);
            Assert.Throws<ArgumentOutOfRangeException>(() => new CoupledStep(car, null, null, carCapacity: 0));
            var fresh = new CoupledStep(car, null, null, carCapacity: 2);
            Assert.Throws<InvalidOperationException>(() => fresh.AddCar(new CarState()));
            var (step, _) = Make(2);
            Assert.Throws<ArgumentNullException>(() => step.AddCar(null));
        }

        [Fact]
        public void ManyCars_AreDeterministicAndFinite()
        {
            CoupledStep Run()
            {
                var pads = Rooms.Pads();
                var bridge = BridgeCompiler.Compile(Rooms.Truss(pads), pads, Scale);
                var step = new CoupledStep(new SuspensionCar(Constants.Loads.Gravity, 0.9, Constants.Car.CruiseSpeed), pads, bridge.Decks, carCapacity: 10);
                step.Reset(bridge.Input, new ChallengeConfig().ToRunConfiguration(new StartSurfaceChoice(0, 1), Vec3.UnitZ, Scale));
                for (int i = 1; i < 10; i++)
                    step.AddCar(step.RestingCar(OnDeck(2 * i, i % 2 == 0 ? 1 : -1), i % 2 == 0 ? Vec3.UnitX : -Vec3.UnitX));
                for (int i = 0; i < 120; i++)
                {
                    step.Step(null, Dt, null);
                    Assert.All(step.Positions, p => Assert.True(p.IsFinite));
                }
                return step;
            }

            var a = Run();
            var b = Run();
            Assert.Equal(a.Positions, b.Positions);
            for (int s = 0; s < 10; s++)
            {
                Assert.True(a.CarAt(s).Position.IsFinite);
                Assert.Equal(a.CarAt(s).Position, b.CarAt(s).Position);
                Assert.Equal(a.CarAt(s).WheelLoads, b.CarAt(s).WheelLoads);
            }
        }
    }
}
