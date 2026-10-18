using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// One lane of traffic, bridge world. Progress is distance along Heading from Entry, where new cars start in
    /// the middle of their entry pad; DeckStart and DeckEnd are where the bridge begins and ends in progress, and
    /// a car past Exit (the middle of the far pad) has crossed.
    /// </summary>
    public sealed class Lane
    {
        public Vec3 Entry;
        public Vec3 Heading;
        public double DeckStart;
        public double DeckEnd;
        public double Exit;

        public double Progress(Vec3 p) => Vec3.Dot(p - Entry, Heading);
    }

    /// <summary>Where the lanes go: two-way when the road fits two lanes, otherwise single file from the start pad.</summary>
    public static class LanePlan
    {
        public static Lane[] Plan(CompiledBridge bridge, int startPadId, double scale)
        {
            var start = bridge.Pads[startPadId];
            var far = bridge.Pads[1 - startPadId];
            RoadAcross(bridge, start, out double lo, out double hi);
            double center = (lo + hi) / 2, width = hi - lo;

            if (width * scale >= 2 * Constants.Traffic.LaneWidthMeters)
            {
                // Drive on the right: start.Across is the right-hand side heading away from the start pad.
                return new[]
                {
                    Make(start, far, start, center + width / 4, scale),
                    Make(far, start, start, center - width / 4, scale),
                };
            }
            return new[] { Make(start, far, start, center, scale) };
        }

        /// <summary>True if the road is wide enough for two-way traffic.</summary>
        public static bool TwoWay(Lane[] lanes) => lanes.Length == 2;

        /// <summary>The deck's extent across the road (room units, along start.Across from its front center); 0..0 without a deck.</summary>
        static void RoadAcross(CompiledBridge bridge, Pad start, out double lo, out double hi)
        {
            lo = double.MaxValue;
            hi = double.MinValue;
            foreach (var deck in bridge.Decks)
            {
                foreach (int i in deck.Triangles)
                {
                    double a = Vec3.Dot(bridge.Input.Nodes[i].Position - start.FrontCenter, start.Across);
                    lo = Math.Min(lo, a);
                    hi = Math.Max(hi, a);
                }
            }
            if (lo > hi)
                lo = hi = 0;
        }

        /// <summary>A lane from the middle of `from` to `to`, at `lateral` across the road (room units from start's center).</summary>
        static Lane Make(Pad from, Pad to, Pad start, double lateral, double scale)
        {
            Vec3 middle = from.FrontCenter - from.Forward * (from.Depth / 2);
            Vec3 entry = middle + start.Across * (lateral - Vec3.Dot(middle - start.FrontCenter, start.Across));
            Vec3 heading = from.Forward;
            double deckEnd = Vec3.Dot(to.FrontCenter - entry, heading);
            return new Lane
            {
                Entry = entry * scale,
                Heading = heading,
                DeckStart = from.Depth / 2 * scale,
                DeckEnd = deckEnd * scale,
                Exit = (deckEnd + to.Depth / 2) * scale,
            };
        }
    }

    /// <summary>What traffic a test runs: a number of cars per lane, or a jam that fills the bridge and holds.</summary>
    public sealed class TrafficSpec
    {
        public int CarsPerLane = 3;
        public bool Jam;
    }

    /// <summary>
    /// Traffic over the bridge, driving the cars in a CoupledStep. Call BeforeStep, then CoupledStep.Step, then
    /// AfterStep. Cars enter their lane's pad when it is clear, cruise, slow for the car ahead, and leave once they
    /// reach the middle of the far pad. In a jam the lead car stops at the end of the deck, the queue fills the
    /// bridge behind it, and the test lasts until the full bridge has held for Constants.Traffic.JamHoldSeconds.
    /// </summary>
    public sealed class Traffic
    {
        readonly CoupledStep _step;
        readonly Lane[] _lanes;
        readonly TrafficSpec _spec;
        readonly double _lowestY;
        readonly double _brake;            // planned braking, m/s^2
        readonly int[] _laneOf;            // per slot, -1 = empty
        readonly double[] _airborne;       // per slot, seconds without a wheel on anything
        readonly bool[] _fell;             // per slot, already counted as fallen
        readonly int[] _quota, _spawned;   // per lane
        readonly List<int>[] _queue;       // per lane, slots ordered front to back (reused every step)
        bool _lanesFull;                   // no lane can take another car (all spawned, or queued from the deck back to the entry)

        public int Spawned { get; private set; }
        public int Crossed { get; private set; }
        public int Fell { get; private set; }
        public int Quota { get; }
        public double JamHeldSeconds { get; private set; }
        public IReadOnlyList<Lane> Lanes => _lanes;

        public bool Finished => _spec.Jam
            ? JamHeldSeconds >= Constants.Traffic.JamHoldSeconds
            : Crossed + Fell >= Quota;

        /// <summary>
        /// How far the traffic has got toward Finished, 0..1. Crossing: the cars across, plus each car still driving
        /// by its share of its lane, over the quota. A jam: filling the bridge earns Constants.Traffic.JamFillShare
        /// and holding it full the rest.
        /// </summary>
        public double Progress
        {
            get
            {
                if (Quota == 0)
                    return 0;
                if (_spec.Jam)
                {
                    double fill = JamHeldSeconds > 0 ? 1 : (double)Spawned / Quota;
                    double hold = Math.Min(1, JamHeldSeconds / Constants.Traffic.JamHoldSeconds);
                    return Constants.Traffic.JamFillShare * fill + (1 - Constants.Traffic.JamFillShare) * hold;
                }
                double through = Crossed;
                for (int s = 0; s < _laneOf.Length; s++)
                {
                    int l = _laneOf[s];
                    var car = l >= 0 ? _step.CarAt(s) : null;
                    if (car == null || _fell[s] || _lanes[l].Exit <= 0)
                        continue;
                    through += Math.Clamp(_lanes[l].Progress(car.Position) / _lanes[l].Exit, 0, 1);
                }
                return Math.Min(1, through / Quota);
            }
        }

        /// <summary>The lane of the car in a slot, or -1.</summary>
        public int LaneOf(int slot) => _laneOf[slot];

        /// <param name="lowestY">Bridge-world height of the lower pad: a car this far below it has fallen for sure.</param>
        /// <param name="grip">Tire grip on this road; icy roads mean longer gaps.</param>
        public Traffic(CoupledStep step, Lane[] lanes, TrafficSpec spec, double lowestY, double grip)
        {
            _step = step;
            _lanes = lanes;
            _spec = spec;
            _lowestY = lowestY;
            _brake = Constants.Traffic.BrakeShareOfGrip * grip * Constants.Loads.Gravity;
            int slots = step.CarCapacity;
            _laneOf = new int[slots];
            _airborne = new double[slots];
            _fell = new bool[slots];
            _quota = new int[lanes.Length];
            _spawned = new int[lanes.Length];
            _queue = new List<int>[lanes.Length];
            for (int s = 0; s < slots; s++)
            {
                step.RemoveCar(s);              // traffic owns every slot, including the reset's car
                _laneOf[s] = -1;
            }
            int share = slots / lanes.Length;
            for (int l = 0; l < lanes.Length; l++)
            {
                _queue[l] = new List<int>(share);
                _quota[l] = Math.Min(share, spec.Jam ? CarsToFill(lanes[l]) : spec.CarsPerLane);
                Quota += _quota[l];
            }
        }

        /// <summary>Cars that fill a lane's deck bumper to bumper, plus one waiting on the entry pad.</summary>
        public static int CarsToFill(Lane lane)
        {
            double pitch = Constants.Car.Length + Constants.Traffic.MinGapMeters;
            return (int)Math.Ceiling((lane.DeckEnd - lane.DeckStart) / pitch) + 1;
        }

        /// <summary>Spawns cars into clear lanes and sets every car's target speed.</summary>
        public void BeforeStep()
        {
            Order();
            _lanesFull = true;
            for (int l = 0; l < _lanes.Length; l++)
            {
                var queue = _queue[l];
                bool clear = queue.Count == 0 || Gap(l, queue[queue.Count - 1]) >= Constants.Traffic.MinGapMeters;
                // A car just spawned at rest also blocks the entry; the lane is only backed up once its queue reaches the deck.
                bool backedUp = !clear && _lanes[l].Progress(_step.CarAt(queue[0]).Position) + Constants.Car.Length / 2 >= _lanes[l].DeckStart;
                if (_spawned[l] < _quota[l] && clear)
                    Spawn(l);
                _lanesFull &= _spawned[l] >= _quota[l] || backedUp;
            }
            Order();
            for (int l = 0; l < _lanes.Length; l++)
            {
                var queue = _queue[l];
                for (int i = 0; i < queue.Count; i++)
                {
                    var car = _step.CarAt(queue[i]);
                    double gap, leadSpeed = 0;
                    if (i > 0)
                    {
                        gap = Gap(l, queue[i - 1], queue[i]);
                        leadSpeed = _step.CarAt(queue[i - 1]).Speed;
                    }
                    else
                    {
                        // A jam's lead car treats the stop line as a parked car one minimum gap beyond it.
                        gap = _spec.Jam ? StopLine(l) - _lanes[l].Progress(car.Position) + Constants.Traffic.MinGapMeters : double.MaxValue;
                    }
                    car.TargetSpeed = FollowingSpeed(gap, car.Speed, leadSpeed, _brake);
                }
            }
        }

        /// <summary>Counts crossings and falls, and frees the slots of cars that are gone.</summary>
        public void AfterStep(double dt)
        {
            bool allStopped = true;
            for (int s = 0; s < _laneOf.Length; s++)
            {
                int l = _laneOf[s];
                var car = _step.CarAt(s);
                if (l < 0 || car == null)
                    continue;
                _airborne[s] = car.Falling ? _airborne[s] + dt : 0;
                if (!_fell[s] && (_airborne[s] > Constants.Car.FallSeconds || car.Position.Y < _lowestY - 5))
                {
                    _fell[s] = true;
                    Fell++;
                }
                if (_fell[s])
                {
                    if (car.Position.Y < _lowestY - 20)
                        Free(s);                     // long gone below the bridge
                    continue;
                }
                if (_lanes[l].Progress(car.Position) >= _lanes[l].Exit)
                {
                    Crossed++;
                    Free(s);
                    continue;
                }
                allStopped &= Math.Abs(car.Speed) < Constants.Traffic.StoppedSpeed;
            }

            if (_spec.Jam && allStopped && _lanesFull)
                JamHeldSeconds += dt;
        }

        /// <summary>
        /// Safe following speed: the fastest a car can go and still stop a minimum gap behind the car ahead if that
        /// car brakes as hard, after its own response delay. Never above cruise, never below zero.
        /// </summary>
        /// <param name="gap">Bumper-to-bumper distance to the car (or stop line) ahead, m.</param>
        /// <param name="speed">This car's speed, m/s.</param>
        /// <param name="leadSpeed">The car ahead's speed (0 for a stop line), m/s.</param>
        /// <param name="brake">Planned braking deceleration, m/s^2.</param>
        public static double FollowingSpeed(double gap, double speed, double leadSpeed, double brake)
        {
            double room = gap - Constants.Traffic.MinGapMeters - Math.Max(0, speed) * Constants.Traffic.ResponseSeconds;
            double lead = Math.Max(0, leadSpeed);
            double squared = lead * lead + 2 * brake * room;
            return squared > 0 ? Math.Min(Constants.Car.CruiseSpeed, Math.Sqrt(squared)) : 0;
        }

        /// <summary>Where a jam's lead car stops: its front bumper at the end of the deck.</summary>
        double StopLine(int lane) => _lanes[lane].DeckEnd - Constants.Car.Length / 2;

        /// <summary>Bumper-to-bumper gap from a car to the one in front of it.</summary>
        double Gap(int lane, int ahead, int behind) =>
            _lanes[lane].Progress(_step.CarAt(ahead).Position) - _lanes[lane].Progress(_step.CarAt(behind).Position) - Constants.Car.Length;

        /// <summary>Gap between the last car in a lane and a new car at the entry.</summary>
        double Gap(int lane, int last) => _lanes[lane].Progress(_step.CarAt(last).Position) - Constants.Car.Length;

        void Spawn(int lane)
        {
            var car = _step.RestingCar(_lanes[lane].Entry, _lanes[lane].Heading);
            car.TargetSpeed = Constants.Car.CruiseSpeed;
            int slot = _step.AddCar(car);
            if (slot < 0)
                return;
            _laneOf[slot] = lane;
            _airborne[slot] = 0;
            _fell[slot] = false;
            _spawned[lane]++;
            Spawned++;
        }

        void Free(int slot)
        {
            _step.RemoveCar(slot);
            _laneOf[slot] = -1;
        }

        /// <summary>Each lane's cars that are still driving, front to back (insertion sort: a lane holds a handful).</summary>
        void Order()
        {
            foreach (var q in _queue)
                q.Clear();
            for (int s = 0; s < _laneOf.Length; s++)
            {
                int l = _laneOf[s];
                if (l < 0 || _fell[s] || _step.CarAt(s) == null)
                    continue;
                var q = _queue[l];
                double p = _lanes[l].Progress(_step.CarAt(s).Position);
                int at = q.Count;
                while (at > 0 && _lanes[l].Progress(_step.CarAt(q[at - 1]).Position) < p)
                    at--;
                q.Insert(at, s);
            }
        }
    }
}
