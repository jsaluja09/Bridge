using System;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Where to draw the car this frame (room units). Origin is the blueprint origin: on the ground under the
    /// middle of the car at rest. Forward, Up and Side are the body axes; Side matches CarPart.Across.
    /// </summary>
    public sealed class CarPose
    {
        public Vec3 Origin;
        public Vec3 Forward = Vec3.UnitX;
        public Vec3 Up = Vec3.UnitY;
        public Vec3 Side = new Vec3(0, 0, -1);
        public readonly Vec3[] WheelCenters = new Vec3[4];
        public double WheelRadius;
        public double SpinAngle;          // radians, the same for every wheel
        public bool Landed;               // fell and came to rest on the floor
        public bool Parked;               // reached the far side and stopped

        public void CopyTo(CarPose other)
        {
            other.Origin = Origin;
            other.Forward = Forward;
            other.Up = Up;
            other.Side = Side;
            Array.Copy(WheelCenters, other.WheelCenters, WheelCenters.Length);
            other.WheelRadius = WheelRadius;
            other.SpinAngle = SpinAngle;
            other.Landed = Landed;
            other.Parked = Parked;
        }
    }

    /// <summary>
    /// Turns the suspension car's state into a drawable pose (W4-08): body axes from heading, pitch and roll,
    /// each wheel at its spring's ground contact, wheels spun by distance travelled. Once the car passes it
    /// stays parked where it arrived; once it falls it comes to rest on the floor instead of falling forever.
    /// Feed it every solver step with the solver's fixed dt.
    /// </summary>
    public sealed class CarPoseTracker
    {
        readonly CarParameters _car;
        readonly double _scale;
        readonly double _floor;           // room
        readonly double _radius;          // bridge world
        readonly double _ride;            // body reference height above the ground at rest, bridge world
        readonly CarPose _pose = new CarPose();
        bool _frozen;

        public CarPoseTracker(CarParameters car, double scale, double floorRoomY, double gravity = Constants.Loads.Gravity)
        {
            if (!(scale > 0))
                throw new ArgumentOutOfRangeException(nameof(scale));
            _car = car;
            _scale = scale;
            _floor = floorRoomY;
            _radius = CarBlueprint.WheelRadius(car);
            _ride = car.RestLength - SuspensionCar.StaticCompression(car, gravity);
            _pose.WheelRadius = _radius / scale;
        }

        public CarPose Pose => _pose;

        /// <summary>A car standing still on flat ground at groundRoom, facing heading (for the build phase).</summary>
        public static CarPose Parked(CarParameters car, double scale, Vec3 groundRoom, Vec3 heading)
        {
            var tracker = new CarPoseTracker(car, scale, double.NegativeInfinity);
            var state = SuspensionCar.Resting(car, Constants.Loads.Gravity, groundRoom * scale, Flat(heading));
            tracker.Update(state, RunResult.Running, 0);
            return tracker._pose;
        }

        public CarPose Update(CarState s, RunResult result, double dt)
        {
            if (_frozen)
                return _pose;

            Vec3 heading = Flat(s.Heading);
            Vec3 side = Vec3.Cross(Vec3.UnitY, heading);
            Vec3 forward = (heading + Vec3.UnitY * Math.Tan(s.Pitch)).Normalized();
            Vec3 tiltedSide = (side + Vec3.UnitY * Math.Tan(s.Roll)).Normalized();
            Vec3 up = Vec3.Cross(forward, tiltedSide).Normalized();

            _pose.Forward = forward;
            _pose.Up = up;
            _pose.Side = Vec3.Cross(up, forward).Normalized();
            _pose.Origin = (s.Position - up * _ride) / _scale;
            for (int w = 0; w < 4; w++)
            {
                double spring = _car.RestLength - (w < s.Compression.Length ? s.Compression[w] : 0);
                Vec3 contact = SuspensionCar.WheelAnchor(s, _car, w) - Vec3.UnitY * spring;
                _pose.WheelCenters[w] = (contact + up * _radius) / _scale;
            }
            _pose.SpinAngle = Wrap(_pose.SpinAngle + s.Speed * dt / _radius);
            Check(_pose);

            if (result == RunResult.Passed)
            {
                _pose.Parked = true;
                _frozen = true;
            }
            else if (LowestPoint() <= _floor)
            {
                Land(heading);
                _frozen = true;
            }
            return _pose;
        }

        /// <summary>Settles the car flat on the floor where it came down.</summary>
        void Land(Vec3 heading)
        {
            Vec3 side = Vec3.Cross(Vec3.UnitY, heading);
            double r = _radius / _scale;
            Vec3 center = new Vec3(_pose.Origin.X, _floor, _pose.Origin.Z);
            _pose.Forward = heading;
            _pose.Up = Vec3.UnitY;
            _pose.Side = side;
            _pose.Origin = center;
            for (int w = 0; w < 4; w++)
            {
                _pose.WheelCenters[w] = center + heading * (SuspensionCar.WheelX(_car, w) / _scale)
                                        + side * (SuspensionCar.WheelY(_car, w) / _scale) + Vec3.UnitY * r;
            }
            _pose.Landed = true;
        }

        double LowestPoint()
        {
            double low = _pose.Origin.Y;
            foreach (var c in _pose.WheelCenters)
                low = Math.Min(low, c.Y - _pose.WheelRadius);
            return low;
        }

        static Vec3 Flat(Vec3 v)
        {
            var h = new Vec3(v.X, 0, v.Z);
            return h.LengthSquared > 1e-12 ? h.Normalized() : Vec3.UnitX;
        }

        static double Wrap(double angle)
        {
            const double turn = 2 * Math.PI;
            angle %= turn;
            return angle < 0 ? angle + turn : angle;
        }

        static void Check(CarPose p)
        {
            bool ok = p.Origin.IsFinite && p.Forward.IsFinite && p.Up.IsFinite && p.Side.IsFinite && double.IsFinite(p.SpinAngle);
            foreach (var c in p.WheelCenters)
                ok &= c.IsFinite;
            if (!ok)
                throw new ArgumentOutOfRangeException(nameof(p), "Car pose is NaN or Infinity");
        }
    }
}
