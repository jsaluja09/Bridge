using System;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// A small suspension car (spec 2.8): a body on four spring-damper wheels, not a rigid-body pile. Each step
    /// it reads the deck height and slope under every wheel, computes wheel loads, and updates heave, pitch,
    /// roll and speed. Optional cruise control and crosswind slip are limited by grip (friction circle).
    /// Wheels: 0 front-left, 1 front-right, 2 rear-left, 3 rear-right. Bridge-world SI units.
    /// </summary>
    public sealed class SuspensionCar : ICarModel
    {
        readonly double _gravity;
        readonly double _grip;
        readonly double? _targetSpeed;

        /// <summary>Tire traction along travel in the last step (newtons), for tests and feedback.</summary>
        public double LastTraction { get; private set; }

        /// <summary>Sum of wheel normal loads in the last step (newtons).</summary>
        public double LastNormal { get; private set; }

        public SuspensionCar(double gravity, double grip, double? targetSpeed = null)
        {
            _gravity = gravity;
            _grip = grip;
            _targetSpeed = targetSpeed;
        }

        public static double WheelX(CarParameters p, int wheel) => wheel < 2 ? p.Wheelbase / 2 : -p.Wheelbase / 2;
        public static double WheelY(CarParameters p, int wheel) => wheel % 2 == 0 ? p.Track / 2 : -p.Track / 2;

        /// <summary>Static compression of each spring under the car's own weight.</summary>
        public static double StaticCompression(CarParameters p, double gravity) => p.MassKg * gravity / (p.WheelCount * p.SpringStiffness);

        /// <summary>A car resting in equilibrium on a flat deck at deckHeight.</summary>
        public static CarState Resting(CarParameters p, double gravity, Vec3 groundPoint, Vec3 heading)
        {
            double c = StaticCompression(p, gravity);
            var state = new CarState
            {
                Position = groundPoint + Vec3.UnitY * (p.RestLength - c),
                Heading = heading,
                WheelLoads = new double[p.WheelCount],
                Compression = new double[p.WheelCount],
            };
            for (int i = 0; i < p.WheelCount; i++)
                state.Compression[i] = c;
            return state;
        }

        /// <summary>Where wheel i's spring is anchored on the body.</summary>
        public static Vec3 WheelAnchor(CarState s, CarParameters p, int wheel)
        {
            Vec3 side = Vec3.Cross(Vec3.UnitY, s.Heading);
            double x = WheelX(p, wheel), y = WheelY(p, wheel);
            return s.Position + s.Heading * x + side * y + Vec3.UnitY * (x * Math.Tan(s.Pitch) + y * Math.Tan(s.Roll));
        }

        public CarState Step(CarState state, CarParameters p, DeckSample[] deck, Vec3 externalForceBridge, double dt)
        {
            if (!(dt > 0))
                throw new ArgumentOutOfRangeException(nameof(dt));

            var next = state.Clone();
            int wheels = p.WheelCount;
            if (next.WheelLoads.Length != wheels)
            {
                next.WheelLoads = new double[wheels];
                next.Compression = new double[wheels];
            }
            Vec3 heading = state.Heading;
            Vec3 side = Vec3.Cross(Vec3.UnitY, heading);

            double vertical = -p.MassKg * _gravity + externalForceBridge.Y;
            double along = Vec3.Dot(externalForceBridge, heading);
            double across = Vec3.Dot(externalForceBridge, side);
            double normalSum = 0;
            double front = 0, rear = 0, left = 0, right = 0;
            int frontN = 0, rearN = 0, leftN = 0, rightN = 0;
            bool anyDeck = false;

            for (int i = 0; i < wheels; i++)
            {
                DeckSample sample = default;
                bool found = false;
                foreach (var d in deck)
                {
                    if (d.WheelIndex == i)
                    {
                        sample = d;
                        found = d.HasDeck;
                        break;
                    }
                }

                double load = 0, compression = 0;
                if (found)
                {
                    anyDeck = true;
                    Vec3 anchor = WheelAnchor(state, p, i);
                    compression = p.RestLength - (anchor.Y - sample.Height);
                    if (compression > 0)
                    {
                        double rate = (compression - state.Compression[i]) / dt;
                        load = Math.Max(0, p.SpringStiffness * compression + p.DamperCoefficient * rate);
                    }
                    // The deck pushes along its normal: on a downhill slope (positive), part of it points forward.
                    double s = sample.Slope, norm = Math.Sqrt(1 + s * s);
                    vertical += load / norm;
                    along += load * s / norm;
                    normalSum += load;

                    if (i < 2) { front += sample.Height; frontN++; } else { rear += sample.Height; rearN++; }
                    if (i % 2 == 0) { left += sample.Height; leftN++; } else { right += sample.Height; rightN++; }
                }
                next.WheelLoads[i] = load;
                next.Compression[i] = Math.Max(0, compression);
            }

            // Tires: cruise control and sideways grip share one friction circle.
            double maxFriction = _grip * normalSum;
            double traction = 0;
            double? target = state.TargetSpeed ?? _targetSpeed;
            if (target.HasValue && normalSum > 0)
                traction = Math.Clamp(p.MassKg * (target.Value - state.Speed) / Math.Max(dt, 0.5) - along, -maxFriction, maxFriction);
            double lateral = 0;
            if (normalSum > 0)
            {
                double needed = -p.MassKg * state.LateralSpeed / dt - across;
                double maxLateral = Math.Sqrt(Math.Max(0, maxFriction * maxFriction - traction * traction));
                lateral = Math.Clamp(needed, -maxLateral, maxLateral);
            }
            LastTraction = traction;
            LastNormal = normalSum;

            next.Speed = state.Speed + (along + traction) / p.MassKg * dt;
            next.LateralSpeed = state.LateralSpeed + (across + lateral) / p.MassKg * dt;
            next.VerticalSpeed = state.VerticalSpeed + vertical / p.MassKg * dt;
            next.Position = state.Position + heading * (next.Speed * dt) + side * (next.LateralSpeed * dt) + Vec3.UnitY * (next.VerticalSpeed * dt);

            double blend = Math.Min(1, dt * 10);
            if (frontN > 0 && rearN > 0)
                next.Pitch += (Math.Atan((front / frontN - rear / rearN) / p.Wheelbase) - next.Pitch) * blend;
            if (leftN > 0 && rightN > 0)
                next.Roll += (Math.Atan((left / leftN - right / rightN) / p.Track) - next.Roll) * blend;
            next.Falling = !anyDeck;

            if (!next.Position.IsFinite || !double.IsFinite(next.Speed))
                throw new InvalidOperationException("Car state is not finite");
            return next;
        }
    }
}
