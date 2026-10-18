using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    public enum RunResult
    {
        Running,
        Passed,
        Failed,
    }

    public enum MemberState : byte
    {
        Broken,
        Standing,
        Falling,
    }

    /// <summary>Everything the render thread needs from one simulation step (room units).</summary>
    public sealed class RunSnapshot
    {
        public CompiledBridge Bridge;
        public long StepId;
        public Vec3[] Positions = Array.Empty<Vec3>();
        public double[] Utilization = Array.Empty<double>();
        public MemberState[] State = Array.Empty<MemberState>();
        public Vec3 CarPosition;          // ground contact point
        public Vec3 CarForward;
        public Vec3 CarUp;
        public RunResult Result;
        public string Message = "";
        public string Lesson = "";        // why the first member failed, in plain words
        public double Time;
        public int MembersLost;
        public double PeakUtilization;
        public double WorstUtilization;
    }

    /// <summary>
    /// One test of a bridge (spec 2.8, 2.9, 2.11): the coupled bridge and car on a copy of the design, plus the
    /// wind, pass or fail, members lost, and the lesson for students. With RunConfiguration.Traffic set, a
    /// stream of cars (or a jam) replaces the single car: the test passes when the traffic is through (or the
    /// jam has held) and fails if any car falls. Keep stepping after the result to let a collapse play out.
    /// Not thread-aware; the Unity host steps it on its worker thread.
    /// </summary>
    public sealed class BridgeRun
    {
        readonly CoupledStep _coupled;
        readonly RunConfiguration _config;
        readonly SuspensionCar _car;
        readonly Traffic _traffic;         // null for the single test car
        readonly Pad _far;
        readonly Vec3 _gap;
        readonly double _scale;
        WheelLoad[] _wheelLoads = Array.Empty<WheelLoad>();
        readonly HashSet<int> _lost = new HashSet<int>();
        double _airborne;
        long _step;

        public Traffic Traffic => _traffic;

        public CompiledBridge Bridge { get; }
        public RunResult Result { get; private set; } = RunResult.Running;
        public string Message { get; private set; } = "";
        public string Lesson { get; private set; } = "";
        public double Time { get; private set; }
        public double PeakUtilization { get; private set; }
        public double WorstUtilization { get; private set; }
        public int MembersLost => _lost.Count;
        public CoupledStep Coupled => _coupled;

        public BridgeRun(CompiledBridge bridge, RunConfiguration config, double? floorRoomHeight = null)
        {
            Bridge = bridge;
            _config = config;
            _scale = config.ScaleFactor;
            _car = new SuspensionCar(Constants.Loads.Gravity, config.Grip, Constants.Car.CruiseSpeed);
            int capacity = config.Traffic != null ? Constants.Traffic.MaxCars : 1;
            _coupled = new CoupledStep(_car, bridge.Pads, bridge.Decks, carCapacity: capacity) { FloorRoomHeight = floorRoomHeight };

            var start = bridge.Pads[config.StartSurfaceId];
            _far = bridge.Pads[1 - config.StartSurfaceId];
            _gap = start.Forward;
            ApplyWind();
            _coupled.Reset(bridge.Input, config);
            if (config.Traffic != null)
            {
                var lanes = LanePlan.Plan(bridge, config.StartSurfaceId, _scale);
                _traffic = new Traffic(_coupled, lanes, config.Traffic, LowestPadY(), config.Grip);
            }
            foreach (int k in _coupled.InitialFailures)
                Lose(k);
            TrackUtilization();
        }

        /// <summary>Bridge-world height of the lower pad.</summary>
        double LowestPadY() => Math.Min(_far.Height, Bridge.Pads[_config.StartSurfaceId].Height) * _scale;

        /// <summary>Wind as static drag on members and a steady side force on the car (HG-car-and-wind: GUESS model).</summary>
        void ApplyWind()
        {
            double v = _config.WindSpeed;
            if (v <= 0 || _config.WindDirection.LengthSquared <= 0)
                return;
            Vec3 w = _config.WindDirection.Normalized();
            double q = 0.5 * Constants.Car.AirDensity * v * v;
            var input = Bridge.Input;
            var index = Bridge.NodeIndex;
            var loads = new Vec3[input.Nodes.Count];
            foreach (var m in input.Members)
            {
                Vec3 a = input.Nodes[index[m.NodeA]].Position * _scale, b = input.Nodes[index[m.NodeB]].Position * _scale;
                double len = Vec3.Distance(a, b);
                if (len <= 0)
                    continue;
                Vec3 axis = (b - a) / len;
                Vec3 cross = w - axis * Vec3.Dot(w, axis);          // only the cross-flow part pushes
                Vec3 f = cross * (q * Constants.Car.MemberDragCoefficient * Constants.Car.MemberDiameter * len);
                loads[index[m.NodeA]] += f * 0.5;
                loads[index[m.NodeB]] += f * 0.5;
            }
            _coupled.ExtraStaticLoads = loads;

            Vec3 side = Vec3.Cross(Vec3.UnitY, _gap);
            double area = Constants.Car.SideArea * Math.Abs(Vec3.Dot(w, side)) + Constants.Car.FrontArea * Math.Abs(Vec3.Dot(w, _gap));
            _coupled.ExternalCarForce = w * (q * Constants.Car.DragCoefficient * area);
        }

        public void Step(double dt)
        {
            Time += dt;
            _step++;
            if (_traffic != null)
                StepTraffic(dt);
            else
                StepSingleCar(dt);
        }

        void StepSingleCar(double dt)
        {
            var output = _coupled.Step(_wheelLoads, dt, null);
            var car = output.Car;
            _wheelLoads = new WheelLoad[car.WheelLoads.Length];
            for (int i = 0; i < _wheelLoads.Length; i++)
                _wheelLoads[i] = new WheelLoad(i, car.WheelLoads[i]);
            TrackMembers();

            if (Result != RunResult.Running)
                return;
            _airborne = car.Falling ? _airborne + dt : 0;
            Vec3[] edge = _far.Corners();

            if (Scoring.ReachedFarSurface(car.Position, edge[0], edge[1], _gap, _scale) && !car.Falling)
                Finish(RunResult.Passed, "The car made it across!");
            else if (_airborne > Constants.Car.FallSeconds || car.Position.Y < LowestPadY() - 5)
                Finish(RunResult.Failed, "The car fell.");
            else if (Time > Constants.Car.TimeoutSeconds)
                Finish(RunResult.Failed, "The car got stuck.");
        }

        void StepTraffic(double dt)
        {
            _traffic.BeforeStep();
            _coupled.Step(null, dt, null);             // every car loads the bridge with its own last wheel loads
            _traffic.AfterStep(dt);
            TrackMembers();

            if (Result != RunResult.Running)
                return;
            if (_traffic.Fell > 0)
                Finish(RunResult.Failed, _traffic.Fell == 1 ? "A car fell." : $"{_traffic.Fell} cars fell.");
            else if (_traffic.Finished)
                Finish(RunResult.Passed, _config.Traffic.Jam ? "The bridge held the traffic jam!" : $"All {_traffic.Crossed} cars made it across!");
            else if (Time > Constants.Traffic.TimeoutSeconds)
                Finish(RunResult.Failed, "The traffic got stuck.");
        }

        void TrackMembers()
        {
            var frame = _coupled.Frame;
            for (int k = 0; k < Bridge.Input.Members.Count; k++)
            {
                if (!frame.Active[k])
                    Lose(k);
            }
            TrackUtilization();
        }

        void Lose(int member)
        {
            if (!_lost.Add(member) || Lesson.Length > 0)
                return;
            var mode = _coupled.Frame.BrokenMode[member];
            if (Bridge.Input.Members[member].HiddenBrace)
                Lesson = "The deck tore: a road needs support underneath or beside it.";
            else
                Lesson = FeedbackMapping.Explain(mode);
        }

        void TrackUtilization()
        {
            var frame = _coupled.Frame;
            double worst = 0;
            for (int k = 0; k < Bridge.Input.Members.Count; k++)
            {
                if (frame.InFrame[k] && !Bridge.Input.Members[k].HiddenBrace)
                    worst = Math.Max(worst, frame.Utilization[k]);
            }
            WorstUtilization = worst;
            if (Result == RunResult.Running)
                PeakUtilization = Math.Max(PeakUtilization, Math.Min(1, worst));
        }

        void Finish(RunResult result, string message)
        {
            Result = result;
            Message = message;
        }

        public void Write(RunSnapshot snap)
        {
            var frame = _coupled.Frame;
            int n = Bridge.Input.Nodes.Count, m = Bridge.Input.Members.Count;
            if (snap.Positions.Length != n)
                snap.Positions = new Vec3[n];
            if (snap.Utilization.Length != m)
            {
                snap.Utilization = new double[m];
                snap.State = new MemberState[m];
            }
            snap.Bridge = Bridge;
            snap.StepId = _step;
            var pos = _coupled.Positions;
            for (int i = 0; i < n; i++)
                snap.Positions[i] = pos[i] / _scale;
            for (int k = 0; k < m; k++)
            {
                snap.Utilization[k] = frame.Utilization[k];
                snap.State[k] = !frame.Active[k] ? MemberState.Broken : frame.InFrame[k] ? MemberState.Standing : MemberState.Falling;
            }

            var car = _coupled.Car;
            if (car != null)
            {
                Vec3 side = Vec3.Cross(Vec3.UnitY, car.Heading);
                Vec3 forward = (car.Heading * Math.Cos(car.Pitch) + Vec3.UnitY * Math.Sin(car.Pitch)).Normalized();
                Vec3 up = (Vec3.UnitY * Math.Cos(car.Roll) - side * Math.Sin(car.Roll)).Normalized();
                Vec3 ride = Vec3.UnitY * (_config.Car.RestLength - Constants.Car.StaticSag);
                snap.CarPosition = (car.Position - ride) / _scale;
                snap.CarForward = forward;
                snap.CarUp = up;
            }
            snap.Result = Result;
            snap.Message = Message;
            snap.Lesson = Lesson;
            snap.Time = Time;
            snap.MembersLost = MembersLost;
            snap.PeakUtilization = PeakUtilization;
            snap.WorstUtilization = WorstUtilization;
        }

        /// <summary>Runs to a result. For tests.</summary>
        public RunResult RunToEnd(double dt = 1.0 / 60, double maxSeconds = 40)
        {
            while (Result == RunResult.Running && Time < maxSeconds)
                Step(dt);
            return Result;
        }
    }
}
