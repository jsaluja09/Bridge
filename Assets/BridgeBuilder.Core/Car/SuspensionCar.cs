using System;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Suspension car (spec section 2.8): a body on one spring-damper strut per wheel that touches the
    /// deck only at wheel contact points. Each step reads the deck height and slope under every wheel,
    /// then updates the suspension and the speed. Gravity and grip come from the constructor because
    /// <see cref="ICarModel.Step"/> has no argument for them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The body is a point mass that moves in the vertical plane of its heading, and its heave is
    /// dynamic. <see cref="CarState"/> has no pitch or roll rate, so the body pitches with the mean deck
    /// slope under its wheels and stays level across the track. Struts act along the deck normal, so a
    /// strut's force is its wheel's normal load. A strut pushes but never pulls, and a wheel whose strut
    /// would be longer than <see cref="CarParameters.RestLength"/> is off the deck.
    /// </para>
    /// <para>
    /// The driver holds the current speed and never brakes: the tires push forward along the deck,
    /// up to grip times the total normal load, against a climb or a headwind, and the car speeds up
    /// downhill. <see cref="CarState"/> has no lateral velocity, so the part of the external force
    /// across the heading is not applied.
    /// </para>
    /// </remarks>
    public sealed class SuspensionCar : ICarModel
    {
        /// <param name="gravity">Gravitational acceleration, m/s^2.</param>
        /// <param name="grip">Tire-to-deck friction coefficient.</param>
        /// <exception cref="ArgumentOutOfRangeException">A value is negative, NaN, or infinite.</exception>
        public SuspensionCar(double gravity, double grip)
        {
            if (!(gravity >= 0.0) || double.IsInfinity(gravity))
            {
                throw new ArgumentOutOfRangeException(nameof(gravity), gravity, "Gravity must be non-negative and finite.");
            }

            if (!(grip >= 0.0) || double.IsInfinity(grip))
            {
                throw new ArgumentOutOfRangeException(nameof(grip), grip, "Grip must be non-negative and finite.");
            }

            Gravity = gravity;
            Grip = grip;
        }

        /// <summary>Gravitational acceleration, m/s^2.</summary>
        public double Gravity { get; }

        /// <summary>Tire-to-deck friction coefficient.</summary>
        public double Grip { get; }

        /// <summary>Advances the car by <paramref name="dt"/> seconds, as <see cref="Advance"/> does.</summary>
        public CarState Step(CarState state, CarParameters parameters, DeckSample[] deck, Vec3 externalForceBridge, double dt)
        {
            return Advance(state, parameters, deck, externalForceBridge, dt).State;
        }

        /// <summary>
        /// Advances the car by <paramref name="dt"/> seconds over <paramref name="deck"/>, which holds one
        /// sample per wheel in any order, and also returns the tire forces. The step reads
        /// <see cref="CarState.Position"/>, <see cref="CarState.Heading"/>, <see cref="CarState.Speed"/>,
        /// <see cref="CarState.VerticalVelocity"/>, and <see cref="CarState.SuspensionCompression"/>, and
        /// returns a new state without changing its inputs.
        /// </summary>
        /// <remarks>
        /// The returned compressions and wheel loads are the ones this deck produced. The returned position
        /// and velocities are after the step. Each wheel load sits at the wheel's new contact point, with
        /// the deck height carried along the sampled slope. <see cref="CarState.Speed"/> is horizontal.
        /// The damper reads the change in compression since the previous step, so the first state should
        /// have an empty <see cref="CarState.SuspensionCompression"/>, and <paramref name="dt"/> should
        /// stay fixed between steps.
        /// </remarks>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="dt"/> is not positive and finite, or a parameter, state, or deck value is out of range.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The deck does not hold exactly one sample per wheel, the compressions do not match the wheel count,
        /// the heading has no horizontal part, or the external force is not finite.
        /// </exception>
        /// <exception cref="InvalidOperationException">The step produced a non-finite value.</exception>
        public SuspensionCarStep Advance(CarState state, CarParameters parameters, DeckSample[] deck, Vec3 externalForceBridge, double dt)
        {
            if (!(dt > 0.0) || double.IsInfinity(dt))
            {
                throw new ArgumentOutOfRangeException(nameof(dt), dt, "The time step must be positive and finite.");
            }

            RequireValid(parameters);
            Vec3 forward = RequireValid(state, parameters.WheelCount);
            DeckSample[] samples = SamplesByWheel(deck, parameters.WheelCount);
            if (!IsFinite(externalForceBridge))
            {
                throw new ArgumentException("The external force must be finite.", nameof(externalForceBridge));
            }

            int wheelCount = parameters.WheelCount;
            double meanSlope = 0.0;
            foreach (DeckSample sample in samples)
            {
                meanSlope += sample.Slope;
            }

            meanSlope /= wheelCount;
            double angle = Math.Atan(meanSlope);
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            Vec3 alongDeck = forward * cos - Vec3.UnitY * sin;
            Vec3 deckNormal = forward * sin + Vec3.UnitY * cos;
            double normalVelocity = state.Speed * sin + state.VerticalVelocity * cos;
            double tangentVelocity = state.Speed * cos - state.VerticalVelocity * sin;
            double[] previous = state.SuspensionCompression ?? Array.Empty<double>();

            var compression = new double[wheelCount];
            var normalLoads = new double[wheelCount];
            double totalNormal = 0.0;
            for (int i = 0; i < wheelCount; i++)
            {
                double mountHeight = state.Position.Y - meanSlope * LongitudinalOffset(parameters, i);
                double strutCompression = parameters.RestLength - (mountHeight - samples[i].Height) * cos;
                if (strutCompression > 0.0)
                {
                    double rate = previous.Length == wheelCount && previous[i] > 0.0
                        ? (strutCompression - previous[i]) / dt
                        : -normalVelocity;
                    double load = Math.Max(0.0, parameters.SpringStiffness * strutCompression + parameters.DamperCoefficient * rate);
                    compression[i] = strutCompression;
                    normalLoads[i] = load;
                    totalNormal += load;
                }
            }

            double weight = parameters.MassKg * Gravity;
            double gravityAlongDeck = weight * sin;
            double externalAlongDeck = Vec3.Dot(externalForceBridge, alongDeck);
            double drive = Math.Min(Math.Max(-(gravityAlongDeck + externalAlongDeck), 0.0), Grip * totalNormal);
            double normalAcceleration = (totalNormal - weight * cos + Vec3.Dot(externalForceBridge, deckNormal)) / parameters.MassKg;
            double tangentAcceleration = (drive + gravityAlongDeck + externalAlongDeck) / parameters.MassKg;
            double newNormalVelocity = normalVelocity + normalAcceleration * dt;
            double newTangentVelocity = tangentVelocity + tangentAcceleration * dt;
            double speed = newTangentVelocity * cos + newNormalVelocity * sin;
            double verticalVelocity = newNormalVelocity * cos - newTangentVelocity * sin;
            double travel = speed * dt;
            double rise = verticalVelocity * dt;
            if (!double.IsFinite(totalNormal) || !double.IsFinite(speed) || !double.IsFinite(verticalVelocity)
                || !double.IsFinite(travel) || !double.IsFinite(rise))
            {
                throw NonFiniteResult();
            }

            Vec3 position = state.Position + forward * travel + Vec3.UnitY * rise;
            if (!IsFinite(position))
            {
                throw NonFiniteResult();
            }

            var wheelLoads = new WheelLoad[wheelCount];
            var tireForces = new Vec3[wheelCount];
            for (int i = 0; i < wheelCount; i++)
            {
                Vec3 contact = position + Offset(parameters, forward, i);
                double contactHeight = samples[i].Height - samples[i].Slope * travel;
                Vec3 tireForce = totalNormal > 0.0 ? alongDeck * (drive * normalLoads[i] / totalNormal) : Vec3.Zero;
                if (!double.IsFinite(contactHeight) || !IsFinite(contact) || !IsFinite(tireForce))
                {
                    throw NonFiniteResult();
                }

                wheelLoads[i] = new WheelLoad
                {
                    WheelIndex = i,
                    NormalLoad = normalLoads[i],
                    Position = new Vec3(contact.X, contactHeight, contact.Z),
                };
                tireForces[i] = tireForce;
            }

            var next = new CarState
            {
                Position = position,
                Heading = forward,
                Speed = speed,
                VerticalVelocity = verticalVelocity,
                WheelLoads = wheelLoads,
                SuspensionCompression = compression,
            };
            return new SuspensionCarStep(next, tireForces);
        }

        /// <summary>
        /// Horizontal offset of a wheel's contact point from <see cref="CarState.Position"/>, m. Wheels 2k
        /// and 2k+1 form axle k, counted from the front. Axles are spread evenly over the wheelbase, and a
        /// single axle sits at the center. Wheel 2k is at -Track/2 and wheel 2k+1 at +Track/2 along
        /// <c>Cross(heading, UnitY)</c>. Wheelbase and track are measured horizontally.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="parameters"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A parameter value or <paramref name="wheelIndex"/> is out of range.</exception>
        /// <exception cref="ArgumentException"><paramref name="heading"/> has no finite, nonzero horizontal part.</exception>
        public static Vec3 WheelOffset(CarParameters parameters, Vec3 heading, int wheelIndex)
        {
            RequireValid(parameters);
            Vec3 forward = HorizontalUnit(heading, nameof(heading));
            if (wheelIndex < 0 || wheelIndex >= parameters.WheelCount)
            {
                throw new ArgumentOutOfRangeException(nameof(wheelIndex), wheelIndex, "The wheel index must be below WheelCount.");
            }

            return Offset(parameters, forward, wheelIndex);
        }

        private static Vec3 Offset(CarParameters parameters, Vec3 forward, int wheelIndex)
        {
            double lateral = (wheelIndex % 2 == 0 ? -0.5 : 0.5) * parameters.Track;
            return forward * LongitudinalOffset(parameters, wheelIndex) + Vec3.Cross(forward, Vec3.UnitY) * lateral;
        }

        private static double LongitudinalOffset(CarParameters parameters, int wheelIndex)
        {
            int axles = parameters.WheelCount / 2;
            if (axles == 1)
            {
                return 0.0;
            }

            return parameters.Wheelbase * (0.5 - (double)(wheelIndex / 2) / (axles - 1));
        }

        private static void RequireValid(CarParameters parameters)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            RequirePositive(parameters.MassKg, nameof(CarParameters.MassKg));
            if (parameters.WheelCount < 2 || parameters.WheelCount % 2 != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(parameters), parameters.WheelCount, "WheelCount must be even and at least 2.");
            }

            RequireNonNegative(parameters.Wheelbase, nameof(CarParameters.Wheelbase));
            RequireNonNegative(parameters.Track, nameof(CarParameters.Track));
            RequirePositive(parameters.SpringStiffness, nameof(CarParameters.SpringStiffness));
            RequireNonNegative(parameters.DamperCoefficient, nameof(CarParameters.DamperCoefficient));
            RequirePositive(parameters.RestLength, nameof(CarParameters.RestLength));
        }

        private static Vec3 RequireValid(CarState state, int wheelCount)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (!IsFinite(state.Position) || !double.IsFinite(state.Speed) || !double.IsFinite(state.VerticalVelocity))
            {
                throw new ArgumentOutOfRangeException(nameof(state), "The car position and velocities must be finite.");
            }

            double[] compression = state.SuspensionCompression;
            if (compression != null && compression.Length != 0)
            {
                if (compression.Length != wheelCount)
                {
                    throw new ArgumentException("SuspensionCompression must be empty or hold one value per wheel.", nameof(state));
                }

                foreach (double value in compression)
                {
                    if (!double.IsFinite(value))
                    {
                        throw new ArgumentOutOfRangeException(nameof(state), value, "Suspension compression must be finite.");
                    }
                }
            }

            return HorizontalUnit(state.Heading, nameof(state));
        }

        private static DeckSample[] SamplesByWheel(DeckSample[] deck, int wheelCount)
        {
            if (deck == null)
            {
                throw new ArgumentNullException(nameof(deck));
            }

            if (deck.Length != wheelCount)
            {
                throw new ArgumentException("The deck needs exactly one sample per wheel.", nameof(deck));
            }

            var byWheel = new DeckSample[wheelCount];
            foreach (DeckSample sample in deck)
            {
                if (sample == null)
                {
                    throw new ArgumentException("Deck samples must not be null.", nameof(deck));
                }

                if (sample.WheelIndex < 0 || sample.WheelIndex >= wheelCount || byWheel[sample.WheelIndex] != null)
                {
                    throw new ArgumentException("Each wheel index needs exactly one deck sample.", nameof(deck));
                }

                if (!double.IsFinite(sample.Height) || !double.IsFinite(sample.Slope))
                {
                    throw new ArgumentOutOfRangeException(nameof(deck), "Deck heights and slopes must be finite.");
                }

                byWheel[sample.WheelIndex] = sample;
            }

            return byWheel;
        }

        private static Vec3 HorizontalUnit(Vec3 heading, string paramName)
        {
            var horizontal = new Vec3(heading.X, 0.0, heading.Z);
            double length = horizontal.Length;
            if (!(length > 0.0) || double.IsInfinity(length))
            {
                throw new ArgumentException("The heading needs a finite, nonzero horizontal part.", paramName);
            }

            return horizontal / length;
        }

        private static void RequirePositive(double value, string name)
        {
            if (!(value > 0.0) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException("parameters", value, name + " must be positive and finite.");
            }
        }

        private static void RequireNonNegative(double value, string name)
        {
            if (!(value >= 0.0) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException("parameters", value, name + " must be non-negative and finite.");
            }
        }

        private static bool IsFinite(Vec3 v)
        {
            return double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
        }

        private static InvalidOperationException NonFiniteResult()
        {
            return new InvalidOperationException("The car step produced a non-finite value. The time step may be too large for the suspension.");
        }
    }

    /// <summary>One <see cref="SuspensionCar"/> step: the new state and the tire forces that produced it.</summary>
    public sealed class SuspensionCarStep
    {
        internal SuspensionCarStep(CarState state, Vec3[] tireForces)
        {
            State = state;
            TireForces = tireForces;
        }

        public CarState State { get; }

        /// <summary>
        /// Force the deck applied to each tire along the deck surface, bridge-world N, indexed by wheel.
        /// Its magnitude is at most grip times that wheel's <see cref="WheelLoad.NormalLoad"/>.
        /// </summary>
        public Vec3[] TireForces { get; }
    }
}
