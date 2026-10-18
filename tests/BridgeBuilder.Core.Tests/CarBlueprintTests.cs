using System;
using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class CarBlueprintTests
    {
        static readonly CarParameters Standard = Constants.Car.Standard(Constants.Human.CarMassKg);

        static bool Finite(CarPart p) =>
            new[] { p.Along, p.Up, p.Across, p.SizeAlong, p.SizeUp, p.SizeAcross }.All(double.IsFinite);

        [Fact]
        public void Wheels_SitAtTheSolversWheelPositions_OnTheGround()
        {
            var parts = CarBlueprint.Build(Standard, Constants.Car.Length);
            double r = CarBlueprint.WheelRadius(Standard);
            var tires = parts.Where(p => p.Color == CarPartColor.Tire).ToList();
            Assert.Equal(4, tires.Count);
            foreach (var t in tires)
            {
                Assert.Equal(SuspensionCar.WheelX(Standard, t.Wheel), t.Along, 9);
                Assert.Equal(SuspensionCar.WheelY(Standard, t.Wheel), t.Across, 9);
                Assert.Equal(0, t.Up - t.SizeUp / 2, 9);             // bottom of the tire touches the ground
                Assert.Equal(r, t.Up, 9);
                Assert.Equal(CarPartShape.Wheel, t.Shape);
            }
            Assert.Equal(new[] { 0, 1, 2, 3 }, tires.Select(t => t.Wheel).OrderBy(w => w));
        }

        [Fact]
        public void Parts_StayInsideTheCarsOutline_AndAboveTheGround()
        {
            double length = Constants.Car.Length;
            var parts = CarBlueprint.Build(Standard, length);
            double halfWidth = Standard.Track / 2 + CarBlueprint.WheelWidth(Standard);
            foreach (var p in parts)
            {
                Assert.True(Finite(p), p.Name);
                Assert.True(p.SizeAlong > 0 && p.SizeUp > 0 && p.SizeAcross > 0, p.Name);
                Assert.True(Math.Abs(p.Along) + p.SizeAlong / 2 <= length / 2 + 1e-9, p.Name);
                Assert.True(Math.Abs(p.Across) + p.SizeAcross / 2 <= halfWidth + 1e-9, p.Name);
                Assert.True(p.Up - p.SizeUp / 2 >= -1e-9, p.Name);
            }
        }

        [Fact]
        public void LooksLikeACar_CabinOnTopOfTheBody_LightsAtTheEnds()
        {
            var parts = CarBlueprint.Build(Standard, Constants.Car.Length);
            var body = parts.Single(p => p.Name == "Body");
            var cabin = parts.Single(p => p.Name == "Cabin");
            Assert.Equal(body.Up + body.SizeUp / 2, cabin.Up - cabin.SizeUp / 2, 9);
            Assert.True(cabin.SizeAlong < body.SizeAlong && cabin.SizeAcross < body.SizeAcross);

            var head = parts.Where(p => p.Color == CarPartColor.Headlight).ToList();
            var tail = parts.Where(p => p.Color == CarPartColor.Taillight).ToList();
            Assert.Equal(2, head.Count);
            Assert.Equal(2, tail.Count);
            Assert.All(head, h => Assert.True(h.Along > body.SizeAlong / 2 - 1e-9));
            Assert.All(tail, t => Assert.True(t.Along < -body.SizeAlong / 2 + 1e-9));
            Assert.Contains(parts, p => p.Color == CarPartColor.Glass);
            Assert.Equal(4, parts.Count(p => p.Name.StartsWith("Hub")));
            // Everything that turns with a wheel names its wheel; the spoke makes the spin visible.
            for (int w = 0; w < 4; w++)
                Assert.Equal(new[] { "Hub" + w, "Spoke" + w, "Wheel" + w }, parts.Where(p => p.Wheel == w).Select(p => p.Name).OrderBy(n => n));
        }

        [Fact]
        public void BiggerCar_GetsABiggerBlueprint()
        {
            var truck = CarPresets.TruckParameters();
            Assert.True(CarBlueprint.WheelRadius(truck) > CarBlueprint.WheelRadius(Standard));
            var small = CarBlueprint.Build(Standard, Constants.Car.Length).Single(p => p.Name == "Body");
            double truckLength = truck.Wheelbase * Constants.Car.Length / Constants.Car.Wheelbase;   // same proportions
            var big = CarBlueprint.Build(truck, truckLength).Single(p => p.Name == "Body");
            Assert.True(big.SizeAlong > small.SizeAlong && big.SizeAcross > small.SizeAcross);
        }

        [Fact]
        public void ImpossibleSizes_Throw()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CarBlueprint.Build(Standard, Standard.Wheelbase * 0.5));
            var flat = Constants.Car.Standard(1000);
            flat.Track = 0;
            Assert.Throws<ArgumentOutOfRangeException>(() => CarBlueprint.Build(flat, Constants.Car.Length));
        }
    }
}
