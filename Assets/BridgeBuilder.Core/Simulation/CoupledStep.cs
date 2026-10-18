using System;
using System.Collections.Generic;
using System.Globalization;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// The bridge half of <see cref="CoupledStep"/>. It works in the units of its input and applies only
    /// the loads it is given, as <see cref="NewmarkIntegrator"/> does.
    /// </summary>
    public interface IBridgeIntegrator
    {
        /// <summary>Starts a run with every member standing, at rest in static equilibrium under <paramref name="initialLoads"/>.</summary>
        void Reset(SolverInput bridgeInput, NodalLoad[] initialLoads);

        /// <summary>
        /// Advances <paramref name="dt"/> seconds with <paramref name="loads"/> acting at the end of the step.
        /// The output holds one displacement per node in the input's units and the ids of the members that
        /// failed during this step.
        /// </summary>
        StepOutput Step(NodalLoad[] loads, double dt);
    }

    /// <summary><see cref="NewmarkIntegrator"/> as an <see cref="IBridgeIntegrator"/>, stepping at <see cref="Constants.Dynamics.DampingRatio"/>.</summary>
    public sealed class NewmarkBridgeIntegrator : IBridgeIntegrator
    {
        private readonly NewmarkIntegrator integrator;

        public NewmarkBridgeIntegrator()
            : this(new NewmarkIntegrator())
        {
        }

        public NewmarkBridgeIntegrator(NewmarkIntegrator integrator)
        {
            this.integrator = integrator ?? throw new ArgumentNullException(nameof(integrator));
        }

        public void Reset(SolverInput bridgeInput, NodalLoad[] initialLoads)
        {
            integrator.Reset(bridgeInput, initialLoads);
        }

        public StepOutput Step(NodalLoad[] loads, double dt)
        {
            return integrator.Step(loads, dt);
        }
    }

    /// <summary>
    /// Lagged two-way coupling of the bridge and the car (spec 2.8), on the calling thread. Each
    /// <see cref="Step"/> first solves the bridge under its own weight plus the car's previous wheel loads,
    /// then samples the moved deck under every wheel and advances the car over it.
    /// <list type="bullet">
    /// <item>Self-weight: <see cref="Reset"/> starts the integrator at rest under
    /// <see cref="DynamicSelfWeight.Loads(SolverInput, IReadOnlyCollection{int})"/> with no member removed.
    /// Every step adds the self-weight to the wheel loads, one load per node in node order. After a step
    /// that reports failed members, the self-weight is recomputed without every member that has failed
    /// since the reset, so a detached piece falls at <see cref="Constants.Loads.Gravity"/>.</item>
    /// <item>Deck: the valid road fills, each split into a fan of triangles from its first node, which is
    /// exact for convex fills. Node positions are moved by the latest bridge displacements, or left as
    /// given before the first step. Points are matched to triangles in the horizontal plane, and a point
    /// over more than one triangle uses the highest.</item>
    /// <item>Wheel loads: a wheel over the deck pushes its <see cref="WheelLoad.NormalLoad"/> into the deck
    /// along the triangle's normal, shared between the triangle's three nodes by the contact point's
    /// barycentric weights, with no moment. A wheel off the deck stands on a marked surface, which is not
    /// part of the bridge, so its load is not applied.</item>
    /// <item>Deck samples: wheel i is at <see cref="CarState.Position"/> plus
    /// <see cref="SuspensionCar.WheelOffset"/>. Over the deck it reads the triangle's height there and the
    /// triangle's gradient along the heading. Off the deck it reads the height of the nearest deck point and
    /// slope 0, as if the level marked surface carried on from the road's edge.</item>
    /// </list>
    /// The input is in room units and goes through <see cref="RoomBridgeAdapter.ToBridge(SolverInput)"/>
    /// with its own <see cref="SolverInput.ScaleFactor"/>; <see cref="RunConfiguration.ScaleFactor"/> is not
    /// read. The integrator, the car, wheel loads, and the external force are in bridge-world units.
    /// <see cref="StepOutput.Displacements"/> are converted back to room units, and rotations, member forces,
    /// failed member ids, and the bridge-world car state pass through.
    /// </summary>
    public sealed class CoupledStep : IDynamicSolver
    {
        // Absorbs rounding, so a point on an edge shared by two deck triangles lands in at least one of them.
        private const double BarycentricTolerance = 1e-9;

        private readonly IBridgeIntegrator integrator;
        private readonly ICarModel fixedCarModel;
        private readonly CarState initialCar;

        private Vec3 externalForceBridge;
        private bool ready;
        private double scaleFactor;
        private SolverInput bridgeInput;
        private CarParameters parameters;
        private ICarModel carModel;
        private RoadDeck deck;
        private List<int> failedSoFar;
        private NodalLoad[] selfWeight;
        private Vec3[] bridgeDisplacements;
        private CarState carState;
        private StepOutput lastOutput;

        /// <summary>
        /// Uses a <see cref="NewmarkBridgeIntegrator"/> and, on every reset, a new <see cref="SuspensionCar"/>
        /// with <see cref="Constants.Loads.Gravity"/> and the configuration's <see cref="RunConfiguration.Grip"/>.
        /// </summary>
        /// <param name="initialCar">The car at the start of every run, in bridge-world units. It is copied.</param>
        /// <exception cref="ArgumentNullException"><paramref name="initialCar"/> is null.</exception>
        public CoupledStep(CarState initialCar)
        {
            integrator = new NewmarkBridgeIntegrator();
            this.initialCar = CopyState(initialCar ?? throw new ArgumentNullException(nameof(initialCar)));
        }

        /// <param name="integrator">Steps the bridge in bridge-world units.</param>
        /// <param name="carModel">Steps the car. It is used as given, so grip and gravity are its own.</param>
        /// <param name="initialCar">The car at the start of every run, in bridge-world units. It is copied.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public CoupledStep(IBridgeIntegrator integrator, ICarModel carModel, CarState initialCar)
        {
            this.integrator = integrator ?? throw new ArgumentNullException(nameof(integrator));
            fixedCarModel = carModel ?? throw new ArgumentNullException(nameof(carModel));
            this.initialCar = CopyState(initialCar ?? throw new ArgumentNullException(nameof(initialCar)));
        }

        /// <summary>Extra force on the car body, bridge-world N, passed to every car step. Zero by default; no wind model is applied here.</summary>
        /// <exception cref="ArgumentException">The force is not finite.</exception>
        public Vec3 ExternalForceBridge
        {
            get
            {
                return externalForceBridge;
            }

            set
            {
                if (!IsFinite(value))
                {
                    throw new ArgumentException("The external force must be finite.", nameof(value));
                }

                externalForceBridge = value;
            }
        }

        /// <summary>
        /// Starts a run: the bridge at rest under its own weight with every member standing, and a copy of
        /// the initial car. The input is read, never modified, so pass a fresh input such as
        /// <see cref="BridgeModel.ToSolverInput"/> returns, for example from <see cref="BridgeModel.Copy"/>
        /// (spec 2.11), and do not change it during the run.
        /// </summary>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentException">
        /// The configuration has no car, the input has no valid road fill whose nodes are all in the input,
        /// or the input is invalid for <see cref="RoomBridgeAdapter"/>, <see cref="DynamicSelfWeight"/>, or the integrator.
        /// </exception>
        /// <exception cref="InvalidOperationException">The integrator rejected the structure, for example as a mechanism.</exception>
        public void Reset(SolverInput input, RunConfiguration configuration)
        {
            ready = false;
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            CarParameters car = configuration.Car ?? throw new ArgumentException("The configuration has no car.", nameof(configuration));
            SolverInput bridge = RoomBridgeAdapter.ToBridge(input);
            RoadDeck road = RoadDeck.Build(bridge);
            ICarModel model = fixedCarModel ?? new SuspensionCar(Constants.Loads.Gravity, configuration.Grip);
            NodalLoad[] weight = DynamicSelfWeight.Loads(bridge, Array.Empty<int>());
            integrator.Reset(bridge, weight);

            scaleFactor = input.ScaleFactor;
            bridgeInput = bridge;
            parameters = CopyParameters(car);
            carModel = model;
            deck = road;
            failedSoFar = new List<int>();
            selfWeight = weight;
            bridgeDisplacements = new Vec3[bridge.Nodes.Length];
            carState = CopyState(initialCar);
            lastOutput = null;
            ready = true;
        }

        /// <summary>
        /// Advances the bridge, then the car, by <paramref name="dt"/> seconds. Pass the previous output's
        /// <c>CarState.WheelLoads</c>, or an empty array on the first step. <paramref name="shouldCancel"/> is
        /// polled once, first; when it returns true nothing advances and the previous output is returned,
        /// which is null before the first step.
        /// </summary>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="dt"/> is not positive and finite, or a wheel load is negative or not finite.
        /// </exception>
        /// <exception cref="ArgumentException">A wheel load is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// <see cref="Reset"/> has not succeeded, or the integrator or the car failed or returned an invalid
        /// result. After an exception from the integrator or the car the run needs a new reset.
        /// </exception>
        public StepOutput Step(WheelLoad[] previousWheelLoads, double dt, Func<bool> shouldCancel)
        {
            if (!ready)
            {
                throw new InvalidOperationException("Reset the coupled step before stepping it.");
            }

            if (previousWheelLoads == null)
            {
                throw new ArgumentNullException(nameof(previousWheelLoads));
            }

            if (shouldCancel == null)
            {
                throw new ArgumentNullException(nameof(shouldCancel));
            }

            if (!(dt > 0.0) || double.IsInfinity(dt))
            {
                throw new ArgumentOutOfRangeException(nameof(dt), dt, "The timestep must be positive and finite.");
            }

            if (shouldCancel())
            {
                return lastOutput;
            }

            NodalLoad[] loads = Combine(selfWeight, WheelForces(previousWheelLoads));
            try
            {
                StepOutput bridgeStep = integrator.Step(loads, dt) ?? throw new InvalidOperationException("The integrator returned no output.");
                Vec3[] moved = RequireDisplacements(bridgeStep);
                int[] failed = bridgeStep.FailedMemberIds ?? throw new InvalidOperationException("The integrator returned no failed member list.");
                CarState next = carModel.Step(carState, parameters, SampleDeck(moved), externalForceBridge, dt)
                    ?? throw new InvalidOperationException("The car model returned no state.");
                StepOutput output = RoomBridgeAdapter.ToRoom(
                    new StepOutput
                    {
                        Displacements = moved,
                        Rotations = bridgeStep.Rotations,
                        MemberForces = bridgeStep.MemberForces,
                        FailedMemberIds = failed,
                        CarState = next,
                    },
                    scaleFactor);
                if (failed.Length > 0)
                {
                    failedSoFar.AddRange(failed);
                    selfWeight = DynamicSelfWeight.Loads(bridgeInput, failedSoFar);
                }

                bridgeDisplacements = moved;
                carState = next;
                lastOutput = output;
                return output;
            }
            catch
            {
                ready = false;
                throw;
            }
        }

        /// <summary>Bridge-world force on each node, in node order, from the wheels over the deck.</summary>
        private Vec3[] WheelForces(WheelLoad[] wheelLoads)
        {
            var forces = new Vec3[bridgeDisplacements.Length];
            foreach (WheelLoad wheel in wheelLoads)
            {
                if (wheel == null)
                {
                    throw new ArgumentException("Wheel loads must not contain null.", "previousWheelLoads");
                }

                if (!(wheel.NormalLoad >= 0.0) || double.IsInfinity(wheel.NormalLoad) || !IsFinite(wheel.Position))
                {
                    throw new ArgumentOutOfRangeException(
                        "previousWheelLoads",
                        wheel.NormalLoad,
                        "Wheel " + Format(wheel.WheelIndex) + " needs a finite, non-negative load at a finite position.");
                }

                if (wheel.NormalLoad == 0.0 || !deck.TryLocate(bridgeDisplacements, wheel.Position.X, wheel.Position.Z, out DeckPoint point))
                {
                    continue;
                }

                Vec3 force = deck.UpNormal(bridgeDisplacements, point.Triangle) * -wheel.NormalLoad;
                forces[deck.Corner(point.Triangle, 0)] += force * point.WeightA;
                forces[deck.Corner(point.Triangle, 1)] += force * point.WeightB;
                forces[deck.Corner(point.Triangle, 2)] += force * point.WeightC;
            }

            return forces;
        }

        private DeckSample[] SampleDeck(Vec3[] displacements)
        {
            var samples = new DeckSample[parameters.WheelCount];
            for (int i = 0; i < samples.Length; i++)
            {
                Vec3 wheel = carState.Position + SuspensionCar.WheelOffset(parameters, carState.Heading, i);
                if (!IsFinite(wheel))
                {
                    throw new InvalidOperationException("The car is at a non-finite position.");
                }

                double height;
                double slope = 0.0;
                if (deck.TryLocate(displacements, wheel.X, wheel.Z, out DeckPoint point))
                {
                    var flat = new Vec3(carState.Heading.X, 0.0, carState.Heading.Z);
                    height = point.Height;
                    slope = deck.Slope(displacements, point.Triangle, flat / flat.Length);
                }
                else
                {
                    height = deck.NearestHeight(displacements, wheel.X, wheel.Z);
                }

                samples[i] = new DeckSample { WheelIndex = i, Height = height, Slope = slope, Position = new Vec3(wheel.X, height, wheel.Z) };
            }

            return samples;
        }

        private Vec3[] RequireDisplacements(StepOutput bridgeStep)
        {
            Vec3[] displacements = bridgeStep.Displacements;
            if (displacements == null || displacements.Length != bridgeDisplacements.Length)
            {
                throw new InvalidOperationException("The integrator must return one displacement per node.");
            }

            foreach (Vec3 displacement in displacements)
            {
                if (!IsFinite(displacement))
                {
                    throw new InvalidOperationException("The integrator returned a non-finite displacement.");
                }
            }

            return (Vec3[])displacements.Clone();
        }

        private static NodalLoad[] Combine(NodalLoad[] weight, Vec3[] wheelForces)
        {
            var loads = new NodalLoad[weight.Length];
            for (int i = 0; i < weight.Length; i++)
            {
                loads[i] = new NodalLoad { NodeId = weight[i].NodeId, Force = weight[i].Force + wheelForces[i], Moment = weight[i].Moment };
            }

            return loads;
        }

        private static CarState CopyState(CarState state)
        {
            WheelLoad[] wheelLoads = state.WheelLoads ?? Array.Empty<WheelLoad>();
            var copiedLoads = new WheelLoad[wheelLoads.Length];
            for (int i = 0; i < wheelLoads.Length; i++)
            {
                WheelLoad wheel = wheelLoads[i];
                copiedLoads[i] = wheel == null ? null : new WheelLoad { WheelIndex = wheel.WheelIndex, NormalLoad = wheel.NormalLoad, Position = wheel.Position };
            }

            return new CarState
            {
                Position = state.Position,
                Heading = state.Heading,
                Speed = state.Speed,
                VerticalVelocity = state.VerticalVelocity,
                WheelLoads = copiedLoads,
                SuspensionCompression = state.SuspensionCompression == null ? Array.Empty<double>() : (double[])state.SuspensionCompression.Clone(),
            };
        }

        private static CarParameters CopyParameters(CarParameters car)
        {
            return new CarParameters
            {
                MassKg = car.MassKg,
                WheelCount = car.WheelCount,
                Wheelbase = car.Wheelbase,
                Track = car.Track,
                SpringStiffness = car.SpringStiffness,
                DamperCoefficient = car.DamperCoefficient,
                RestLength = car.RestLength,
            };
        }

        private static bool IsFinite(Vec3 v)
        {
            return double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
        }

        private static string Format(int id)
        {
            return id.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>A point on a deck triangle: its barycentric weights for the triangle's corners and its height.</summary>
        private readonly struct DeckPoint
        {
            public DeckPoint(int triangle, double weightA, double weightB, double weightC, double height)
            {
                Triangle = triangle;
                WeightA = weightA;
                WeightB = weightB;
                WeightC = weightC;
                Height = height;
            }

            public int Triangle { get; }

            public double WeightA { get; }

            public double WeightB { get; }

            public double WeightC { get; }

            public double Height { get; }
        }

        /// <summary>The valid road fills as triangles of node indices, fanned from each fill's first node.</summary>
        private sealed class RoadDeck
        {
            private readonly Vec3[] restPositions;
            private readonly int[] corners;

            private RoadDeck(Vec3[] restPositions, int[] corners)
            {
                this.restPositions = restPositions;
                this.corners = corners;
            }

            /// <summary>A road fill that names a node not in the input is skipped.</summary>
            public static RoadDeck Build(SolverInput bridgeInput)
            {
                Node[] nodes = bridgeInput.Nodes;
                var positions = new Vec3[nodes.Length];
                var indexById = new Dictionary<int, int>(nodes.Length);
                for (int i = 0; i < nodes.Length; i++)
                {
                    positions[i] = nodes[i].Position;
                    indexById[nodes[i].Id] = i;
                }

                var corners = new List<int>();
                foreach (Fill fill in bridgeInput.Fills ?? Array.Empty<Fill>())
                {
                    if (fill == null)
                    {
                        throw new ArgumentException("The fill array holds a null entry.", "input");
                    }

                    if (!fill.Road || !fill.Valid || !TryResolve(fill.NodeIds, indexById, out int[] loop))
                    {
                        continue;
                    }

                    for (int k = 1; k + 1 < loop.Length; k++)
                    {
                        corners.Add(loop[0]);
                        corners.Add(loop[k]);
                        corners.Add(loop[k + 1]);
                    }
                }

                if (corners.Count == 0)
                {
                    throw new ArgumentException("The input has no valid road fill for the car to drive on.", "input");
                }

                return new RoadDeck(positions, corners.ToArray());
            }

            /// <summary>Node index of corner <paramref name="corner"/> (0, 1, or 2) of a triangle.</summary>
            public int Corner(int triangle, int corner)
            {
                return corners[3 * triangle + corner];
            }

            /// <summary>Finds the highest triangle over (x, z). Triangles that are edge-on from above are skipped.</summary>
            public bool TryLocate(Vec3[] displacements, double x, double z, out DeckPoint point)
            {
                point = default;
                bool found = false;
                for (int t = 0; t < corners.Length / 3; t++)
                {
                    Vec3 a = Moved(displacements, t, 0);
                    Vec3 b = Moved(displacements, t, 1);
                    Vec3 c = Moved(displacements, t, 2);
                    double abX = b.X - a.X;
                    double abZ = b.Z - a.Z;
                    double acX = c.X - a.X;
                    double acZ = c.Z - a.Z;
                    double det = abX * acZ - abZ * acX;
                    if (det == 0.0 || !double.IsFinite(det))
                    {
                        continue;
                    }

                    double pX = x - a.X;
                    double pZ = z - a.Z;
                    double weightB = (pX * acZ - pZ * acX) / det;
                    double weightC = (abX * pZ - abZ * pX) / det;
                    double weightA = 1.0 - weightB - weightC;
                    if (weightA < -BarycentricTolerance || weightB < -BarycentricTolerance || weightC < -BarycentricTolerance)
                    {
                        continue;
                    }

                    double height = weightA * a.Y + weightB * b.Y + weightC * c.Y;
                    if (!found || height > point.Height)
                    {
                        point = new DeckPoint(t, weightA, weightB, weightC, height);
                        found = true;
                    }
                }

                return found;
            }

            /// <summary>Unit normal of a triangle, pointing up.</summary>
            public Vec3 UpNormal(Vec3[] displacements, int triangle)
            {
                Vec3 a = Moved(displacements, triangle, 0);
                Vec3 normal = Vec3.Cross(Moved(displacements, triangle, 1) - a, Moved(displacements, triangle, 2) - a).Normalized();
                return normal.Y < 0.0 ? -normal : normal;
            }

            /// <summary>Gradient of a triangle along the horizontal unit vector <paramref name="forward"/>, positive downhill.</summary>
            public double Slope(Vec3[] displacements, int triangle, Vec3 forward)
            {
                Vec3 a = Moved(displacements, triangle, 0);
                Vec3 b = Moved(displacements, triangle, 1);
                Vec3 c = Moved(displacements, triangle, 2);
                double abX = b.X - a.X;
                double abZ = b.Z - a.Z;
                double acX = c.X - a.X;
                double acZ = c.Z - a.Z;
                double det = abX * acZ - abZ * acX;
                double riseB = b.Y - a.Y;
                double riseC = c.Y - a.Y;
                double gradientX = (riseB * acZ - abZ * riseC) / det;
                double gradientZ = (abX * riseC - acX * riseB) / det;
                return -(gradientX * forward.X + gradientZ * forward.Z);
            }

            /// <summary>Height of the deck point nearest to (x, z) in the horizontal plane.</summary>
            public double NearestHeight(Vec3[] displacements, double x, double z)
            {
                double nearest = double.PositiveInfinity;
                double height = 0.0;
                for (int t = 0; t < corners.Length / 3; t++)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        Vec3 start = Moved(displacements, t, k);
                        Vec3 end = Moved(displacements, t, (k + 1) % 3);
                        double edgeX = end.X - start.X;
                        double edgeZ = end.Z - start.Z;
                        double lengthSquared = edgeX * edgeX + edgeZ * edgeZ;
                        double along = lengthSquared > 0.0
                            ? Math.Max(0.0, Math.Min(1.0, ((x - start.X) * edgeX + (z - start.Z) * edgeZ) / lengthSquared))
                            : 0.0;
                        double offX = start.X + along * edgeX - x;
                        double offZ = start.Z + along * edgeZ - z;
                        double distanceSquared = offX * offX + offZ * offZ;
                        if (distanceSquared < nearest)
                        {
                            nearest = distanceSquared;
                            height = start.Y + along * (end.Y - start.Y);
                        }
                    }
                }

                return height;
            }

            private static bool TryResolve(int[] nodeIds, Dictionary<int, int> indexById, out int[] loop)
            {
                loop = null;
                if (nodeIds == null || nodeIds.Length < 3)
                {
                    return false;
                }

                var indices = new int[nodeIds.Length];
                for (int i = 0; i < nodeIds.Length; i++)
                {
                    if (!indexById.TryGetValue(nodeIds[i], out indices[i]))
                    {
                        return false;
                    }
                }

                loop = indices;
                return true;
            }

            private Vec3 Moved(Vec3[] displacements, int triangle, int corner)
            {
                int node = corners[3 * triangle + corner];
                return restPositions[node] + displacements[node];
            }
        }
    }
}
