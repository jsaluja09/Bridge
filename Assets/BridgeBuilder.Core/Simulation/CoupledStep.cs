using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>What CoupledStep needs from the bridge integrator (FrameDynamics in the game; a spy in tests).</summary>
    public interface IFrameIntegrator
    {
        int NodeCount { get; }
        bool[] Active { get; }
        bool[] InFrame { get; }
        bool[] NodeInFrame { get; }
        MemberForce[] Forces { get; }
        double[] Utilization { get; }
        FailureMode[] Mode { get; }
        FailureMode[] BrokenMode { get; }     // why each broken member failed (kept after rebuilds)
        Vec3 Position(int node);
        Vec3 Velocity(int node);
        Vec3 Displacement(int node);
        double NodeMass(int node);
        IReadOnlyList<int> MembersAt(int node);
        int MemberNodeA(int member);
        int MemberNodeB(int member);
        List<int> Rebuild(bool firstTime);
        void SolveStatic(Vec3[] loads);
        void Step(Vec3[] loads, double dt);
        List<int> RemoveFailures(int maxPerStep);
        List<int> DetachAfterFailures();
    }

    /// <summary>
    /// Lagged two-way coupling (spec 2.8), implementing IDynamicSolver: each step the bridge solves with the
    /// cars' previous wheel loads, then every car reads the new deck height and slope at each wheel and updates.
    /// Members that fail are removed (spec 2.7); pieces cut off from every support continue as debris, and cars
    /// can ride a collapsing deck down. Runs on whatever thread calls it. Bridge-world internally;
    /// displacements out are room units.
    ///
    /// Cars live in fixed slots so a slot index stays valid while traffic comes and goes. Reset puts the first
    /// car in slot 0 on the start pad; AddCar and RemoveCar manage the rest. The IDynamicSolver wheel loads
    /// drive slot 0; every other car uses its own loads from the previous step.
    /// </summary>
    public sealed class CoupledStep : IDynamicSolver
    {
        struct Contact
        {
            public int Deck, Tri;          // Deck -1: on a pad or nothing (no bridge load)
            public double Wa, Wb, Wc;
        }

        readonly ICarModel _car;
        readonly IReadOnlyList<Pad> _pads;
        readonly IReadOnlyList<DeckGeometry> _decks;
        readonly Func<SolverInput, IFrameIntegrator> _factory;
        readonly double _gravity;

        SolverInput _input;
        RunConfiguration _config;
        IFrameIntegrator _frame;
        DebrisWorld _debris;
        Vec3[] _rest;
        Vec3[] _static;
        Vec3[] _loads;
        StepOutput _last;
        double _scale;
        readonly CarState[] _cars;          // per slot; null = empty
        readonly Contact[][] _contacts;     // per slot, per wheel: where last step's load lands
        readonly DeckSample[][] _samples;   // per slot, reused every step

        public Vec3[] Positions { get; private set; }     // bridge world, current
        public IFrameIntegrator Frame => _frame;
        public DebrisWorld Debris => _debris;

        /// <summary>The car in slot 0 (the single test car), or null once removed.</summary>
        public CarState Car => _cars[0];

        public int CarCapacity => _cars.Length;

        /// <summary>The car in a slot, or null if the slot is empty. Bridge world.</summary>
        public CarState CarAt(int slot) => _cars[slot];

        /// <summary>The real floor, room units, where debris lands (a challenge override replaces it).</summary>
        public double? FloorRoomHeight { get; set; }

        /// <summary>Crosswind on the car, bridge-world newtons (the caller's wind model).</summary>
        public Vec3 ExternalCarForce { get; set; }

        /// <summary>Extra constant node loads, e.g. wind on members, bridge-world newtons per node.</summary>
        public Vec3[] ExtraStaticLoads { get; set; }

        public CoupledStep(ICarModel car, IReadOnlyList<Pad> pads, IReadOnlyList<DeckGeometry> decks,
                           Func<SolverInput, IFrameIntegrator> integratorFactory = null, double gravity = Constants.Loads.Gravity,
                           int carCapacity = 1)
        {
            if (carCapacity < 1)
                throw new ArgumentOutOfRangeException(nameof(carCapacity));
            _car = car;
            _pads = pads ?? Array.Empty<Pad>();
            _decks = decks ?? Array.Empty<DeckGeometry>();
            _factory = integratorFactory ?? (input => new FrameDynamics(input));
            _gravity = gravity;
            _cars = new CarState[carCapacity];
            _contacts = new Contact[carCapacity][];
            _samples = new DeckSample[carCapacity][];
        }

        /// <summary>Puts a car in the first free slot. Returns the slot, or -1 when every slot is taken.</summary>
        public int AddCar(CarState state)
        {
            if (_config == null)
                throw new InvalidOperationException("Reset before adding cars");
            for (int slot = 0; slot < _cars.Length; slot++)
            {
                if (_cars[slot] != null)
                    continue;
                _cars[slot] = state ?? throw new ArgumentNullException(nameof(state));
                int wheels = _config.Car.WheelCount;
                if (_contacts[slot] == null || _contacts[slot].Length != wheels)
                {
                    _contacts[slot] = new Contact[wheels];
                    _samples[slot] = new DeckSample[wheels];
                }
                for (int w = 0; w < wheels; w++)
                    _contacts[slot][w].Deck = -1;
                return slot;
            }
            return -1;
        }

        public void RemoveCar(int slot) => _cars[slot] = null;

        /// <summary>A car at rest on its springs with its wheels at groundPoint (bridge world), facing heading.</summary>
        public CarState RestingCar(Vec3 groundPoint, Vec3 heading) => SuspensionCar.Resting(_config.Car, _gravity, groundPoint, heading);

        public void Reset(SolverInput input, RunConfiguration configuration)
        {
            _input = input;
            _config = configuration;
            _scale = input.ScaleFactor > 0 ? input.ScaleFactor : configuration.ScaleFactor;
            _frame = _factory(input);
            int n = _frame.NodeCount;
            _rest = new Vec3[n];
            for (int i = 0; i < n; i++)
                _rest[i] = input.Nodes[i].Position * _scale;

            InitialFailures.Clear();
            double floor = (configuration.GroundHeightOverride ?? FloorRoomHeight ?? LowestPad() - 1) * _scale;
            _debris = new DebrisWorld(_frame, _pads, _scale, floor, _gravity);

            _static = new Vec3[n];
            UpdateStaticLoads();
            _loads = new Vec3[n];

            // Start from equilibrium under self-weight: whatever can't carry itself breaks before the car moves.
            _debris.Add(_frame.Rebuild(true));
            for (int i = 0; i < 40; i++)
            {
                _frame.SolveStatic(_static);
                var failed = _frame.RemoveFailures(Constants.Dynamics.MaxFailuresPerStep);
                if (failed.Count == 0)
                    break;
                InitialFailures.AddRange(failed);
                _debris.Add(_frame.DetachAfterFailures());
                UpdateStaticLoads();
            }
            Positions = new Vec3[n];
            UpdatePositions();

            var pad = _pads.Count > configuration.StartSurfaceId ? _pads[configuration.StartSurfaceId] : null;
            Vec3 ground = pad != null ? (pad.FrontCenter - pad.Forward * (pad.Depth / 2)) * _scale : Vec3.Zero;
            Vec3 heading = pad?.Forward ?? Vec3.UnitX;
            Array.Clear(_cars, 0, _cars.Length);
            AddCar(RestingCar(ground, heading));
            _last = new StepOutput { Car = _cars[0].Clone(), Displacements = new Vec3[n], MemberForces = _frame.Forces };
        }

        /// <summary>Members that broke while the bridge settled under its own weight.</summary>
        public List<int> InitialFailures { get; } = new List<int>();

        public StepOutput Step(WheelLoad[] previousWheelLoads, double dt, Func<bool> shouldCancel)
        {
            if (shouldCancel != null && shouldCancel())
                return _last;

            // 1. The bridge solves with the cars' previous wheel loads.
            Array.Copy(_static, _loads, _loads.Length);
            _debris.ClearForces();
            for (int slot = 0; slot < _cars.Length; slot++)
            {
                var car = _cars[slot];
                if (car == null)
                    continue;
                if (slot == 0 && previousWheelLoads != null)
                {
                    foreach (var wl in previousWheelLoads)
                        LoadWheel(slot, wl.WheelIndex, wl.NormalLoad);
                }
                else
                {
                    for (int w = 0; w < car.WheelLoads.Length; w++)
                        LoadWheel(slot, w, car.WheelLoads[w]);
                }
            }
            _frame.Step(_loads, dt);
            var failed = _frame.RemoveFailures(Constants.Dynamics.MaxFailuresPerStep);
            if (failed.Count > 0)
            {
                _debris.Add(_frame.DetachAfterFailures());
                UpdateStaticLoads();
            }
            _debris.Step(dt);
            UpdatePositions();

            // 2. Each car reads the new deck under each wheel and updates.
            for (int slot = 0; slot < _cars.Length; slot++)
            {
                var car = _cars[slot];
                if (car == null)
                    continue;
                var samples = _samples[slot];
                for (int w = 0; w < samples.Length; w++)
                    samples[w] = Sample(slot, w, car, SuspensionCar.WheelAnchor(car, _config.Car, w));
                _cars[slot] = _car.Step(car, _config.Car, samples, ExternalCarForce, dt);
            }

            var output = new StepOutput { Displacements = new Vec3[Positions.Length], MemberForces = _frame.Forces, Car = _cars[0]?.Clone() };
            for (int i = 0; i < Positions.Length; i++)
                output.Displacements[i] = (Positions[i] - _rest[i]) / _scale;
            foreach (int k in failed)
                output.FailedMemberIds.Add(_input.Members[k].Id);
            _last = output;
            return output;
        }

        /// <summary>
        /// Self-weight of the members still standing, plus the extra loads (bridge-core DynamicSelfWeight): a broken
        /// member stops loading the nodes it hung from instead of leaving its weight on the bridge.
        /// </summary>
        void UpdateStaticLoads()
        {
            var weight = DeadLoadPreview.SelfWeight(_input, _gravity, _scale, _frame.Active);
            for (int i = 0; i < _static.Length; i++)
                _static[i] = weight[i].Force + (ExtraStaticLoads != null ? ExtraStaticLoads[i] : Vec3.Zero);
        }

        /// <summary>Spreads one wheel's load over the deck triangle it touched last step.</summary>
        void LoadWheel(int slot, int wheel, double load)
        {
            var c = _contacts[slot][wheel];
            if (c.Deck < 0 || load <= 0)
                return;
            var tri = _decks[c.Deck].Triangles;
            Load(tri[c.Tri], new Vec3(0, -load * c.Wa, 0));
            Load(tri[c.Tri + 1], new Vec3(0, -load * c.Wb, 0));
            Load(tri[c.Tri + 2], new Vec3(0, -load * c.Wc, 0));
        }

        void Load(int node, Vec3 force)
        {
            if (_frame.NodeInFrame[node])
                _loads[node] += force;
            else
                _debris.AddForce(node, force);
        }

        void UpdatePositions()
        {
            for (int i = 0; i < Positions.Length; i++)
                Positions[i] = _debris.Contains(i) ? _debris.Position(i) : _rest[i] + _frame.Displacement(i);
        }

        double LowestPad()
        {
            double low = double.MaxValue;
            foreach (var p in _pads)
                low = Math.Min(low, p.Height);
            return low == double.MaxValue ? 0 : low;
        }

        /// <summary>The highest deck triangle or pad top under a wheel that it could be resting on.</summary>
        DeckSample Sample(int slot, int wheel, CarState car, Vec3 anchor)
        {
            var sample = new DeckSample { WheelIndex = wheel, Position = anchor };
            double reach = anchor.Y + _config.Car.RestLength * 0.5;
            double best = double.MinValue;
            Vec3 heading = car.Heading;
            ref var contact = ref _contacts[slot][wheel];
            contact.Deck = -1;

            foreach (var pad in _pads)
            {
                double top = pad.Height * _scale;
                if (top <= reach && top > best && pad.Over(anchor / _scale))
                {
                    best = top;
                    sample.Slope = 0;
                    contact.Deck = -1;
                }
            }

            for (int d = 0; d < _decks.Count; d++)
            {
                var deck = _decks[d];
                for (int t = 0; t < deck.Triangles.Length; t += 3)
                {
                    if (Broken(deck, t))
                        continue;
                    Vec3 a = Positions[deck.Triangles[t]], b = Positions[deck.Triangles[t + 1]], c = Positions[deck.Triangles[t + 2]];
                    if (!Geometry.BarycentricXZ(anchor, a, b, c, out double wa, out double wb, out double wc))
                        continue;
                    double h = wa * a.Y + wb * b.Y + wc * c.Y;
                    if (h > reach || h <= best)
                        continue;
                    best = h;
                    Vec3 n = Vec3.Cross(b - a, c - a);
                    if (n.Y < 0)
                        n = -n;
                    sample.Slope = n.Y > 1e-9 ? Vec3.Dot(n, heading) / n.Y : 0;
                    contact = new Contact { Deck = d, Tri = t, Wa = wa, Wb = wb, Wc = wc };
                }
            }

            sample.HasDeck = best > double.MinValue;
            sample.Height = sample.HasDeck ? best : 0;
            return sample;
        }

        bool Broken(DeckGeometry deck, int t)
        {
            for (int e = 0; e < 3; e++)
            {
                int k = deck.TriangleEdges[t + e];
                if (k >= 0 && !_frame.Active[k])
                    return true;
            }
            return false;
        }
    }
}
