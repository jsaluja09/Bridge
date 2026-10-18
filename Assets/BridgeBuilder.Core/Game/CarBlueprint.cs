using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    public enum CarPartShape
    {
        Box,
        Wheel,      // a cylinder whose axis runs across the car
    }

    public enum CarPartColor
    {
        Body,
        Cabin,
        Glass,
        Tire,
        Hub,
        Headlight,
        Taillight,
        Bumper,
    }

    /// <summary>
    /// One primitive of the toy car, in the car's own frame (bridge-world meters): Along points forward, Up points
    /// up, Across points along SuspensionCar's wheel side (positive for wheels 0 and 2). The origin is on the ground
    /// under the middle of the car.
    /// </summary>
    public readonly struct CarPart
    {
        public readonly string Name;
        public readonly CarPartShape Shape;
        public readonly CarPartColor Color;
        public readonly double Along, Up, Across;               // center
        public readonly double SizeAlong, SizeUp, SizeAcross;   // full extents
        public readonly int Wheel;                              // 0..3 for wheel parts (SuspensionCar order), else -1

        public CarPart(string name, CarPartShape shape, CarPartColor color, double along, double up, double across,
                       double sizeAlong, double sizeUp, double sizeAcross, int wheel = -1)
        {
            Name = name;
            Shape = shape;
            Color = color;
            Along = along;
            Up = up;
            Across = across;
            SizeAlong = sizeAlong;
            SizeUp = sizeUp;
            SizeAcross = sizeAcross;
            Wheel = wheel;
        }
    }

    /// <summary>
    /// The primitive toy car (W4-08): a low wide body, a small cabin with windows, bumpers, lights and four
    /// oversized wheels at the solver's wheel positions. Sized from the car's wheelbase, track and length.
    /// </summary>
    public static class CarBlueprint
    {
        public static double WheelRadius(CarParameters p) => p.Wheelbase * Constants.CarVisual.WheelRadiusPerWheelbase;

        public static double WheelWidth(CarParameters p) => p.Track * Constants.CarVisual.WheelWidthPerTrack;

        public static List<CarPart> Build(CarParameters p, double length)
        {
            if (!(p.Wheelbase > 0) || !(p.Track > 0) || !(length > p.Wheelbase))
                throw new ArgumentOutOfRangeException(nameof(p), "The car needs a positive wheelbase and track, and a length longer than its wheelbase");
            double r = WheelRadius(p);
            double bodyWidth = p.Track * Constants.CarVisual.BodyWidthPerTrack;
            double bodyHeight = length * Constants.CarVisual.BodyHeightPerLength;
            double bodyBottom = r * Constants.CarVisual.BodyClearancePerWheelRadius;
            double bodyMid = bodyBottom + bodyHeight / 2;
            double bodyTop = bodyBottom + bodyHeight;
            double rise = length * Constants.CarVisual.HoodRisePerLength;
            double cabinLength = length * Constants.CarVisual.CabinLengthPerLength;
            double cabinHeight = length * Constants.CarVisual.CabinHeightPerLength;
            double cabinWidth = bodyWidth * Constants.CarVisual.CabinWidthPerBody;
            double cabinCenter = -length * Constants.CarVisual.CabinRearwardPerLength;
            double lamp = length * Constants.CarVisual.LampSizePerLength;
            double bumper = length * Constants.CarVisual.BumperDepthPerLength;
            double bodyLength = length - 2 * bumper;
            double front = bodyLength / 2, rear = -bodyLength / 2;
            double cabinFront = cabinCenter + cabinLength / 2, cabinRear = cabinCenter - cabinLength / 2;

            var parts = new List<CarPart>
            {
                new CarPart("Body", CarPartShape.Box, CarPartColor.Body, 0, bodyMid, 0, bodyLength, bodyHeight, bodyWidth),
                // Hood and trunk: raised panels in front of and behind the cabin.
                new CarPart("Hood", CarPartShape.Box, CarPartColor.Body, (front + cabinFront) / 2, bodyTop + rise / 2, 0,
                            front - cabinFront, rise, bodyWidth * 0.96),
                new CarPart("Trunk", CarPartShape.Box, CarPartColor.Body, (rear + cabinRear) / 2, bodyTop + rise / 2, 0,
                            cabinRear - rear, rise, bodyWidth * 0.96),
                new CarPart("Cabin", CarPartShape.Box, CarPartColor.Cabin, cabinCenter, bodyTop + cabinHeight / 2, 0,
                            cabinLength, cabinHeight, cabinWidth),
                // Windows: thin glass slabs on the cabin's faces.
                new CarPart("Windshield", CarPartShape.Box, CarPartColor.Glass, cabinFront, bodyTop + cabinHeight * 0.55, 0,
                            cabinHeight * 0.12, cabinHeight * 0.7, cabinWidth * 0.9),
                new CarPart("RearWindow", CarPartShape.Box, CarPartColor.Glass, cabinRear, bodyTop + cabinHeight * 0.55, 0,
                            cabinHeight * 0.12, cabinHeight * 0.6, cabinWidth * 0.85),
                new CarPart("SideWindows", CarPartShape.Box, CarPartColor.Glass, cabinCenter, bodyTop + cabinHeight * 0.55, 0,
                            cabinLength * 0.82, cabinHeight * 0.55, cabinWidth * 1.02),
                new CarPart("FrontBumper", CarPartShape.Box, CarPartColor.Bumper, front + bumper / 2, bodyBottom + bodyHeight * 0.3, 0,
                            bumper, bodyHeight * 0.45, bodyWidth),
                new CarPart("RearBumper", CarPartShape.Box, CarPartColor.Bumper, rear - bumper / 2, bodyBottom + bodyHeight * 0.3, 0,
                            bumper, bodyHeight * 0.45, bodyWidth),
            };
            foreach (double side in new[] { 1.0, -1.0 })
            {
                double across = side * (bodyWidth / 2 - lamp);
                parts.Add(new CarPart(side > 0 ? "HeadlightA" : "HeadlightB", CarPartShape.Box, CarPartColor.Headlight,
                                      front + lamp * 0.1, bodyMid + bodyHeight * 0.15, across, lamp * 0.4, lamp * 0.6, lamp * 1.2));
                parts.Add(new CarPart(side > 0 ? "TaillightA" : "TaillightB", CarPartShape.Box, CarPartColor.Taillight,
                                      rear - lamp * 0.1, bodyMid + bodyHeight * 0.15, across, lamp * 0.4, lamp * 0.6, lamp * 1.2));
            }

            double width = WheelWidth(p);
            for (int w = 0; w < 4; w++)
            {
                double along = SuspensionCar.WheelX(p, w), across = SuspensionCar.WheelY(p, w);
                parts.Add(new CarPart("Wheel" + w, CarPartShape.Wheel, CarPartColor.Tire, along, r, across, 2 * r, 2 * r, width, w));
                // Hub cap on the outside face of the tire.
                parts.Add(new CarPart("Hub" + w, CarPartShape.Wheel, CarPartColor.Hub, along, r, across + Math.Sign(across) * width * 0.5,
                                      r, r, width * 0.15, w));
                // A bar across the hub, so you can see the wheel turn.
                parts.Add(new CarPart("Spoke" + w, CarPartShape.Box, CarPartColor.Hub, along, r, across + Math.Sign(across) * width * 0.55,
                                      r * 1.6, r * 0.22, width * 0.1, w));
            }
            return parts;
        }
    }
}
