using System;
using System.Collections.Generic;
using System.Numerics;

namespace BridgeBuilder.Core
{
    public enum RunResult
    {
        Running,
        Passed,
        Failed,
    }

    /// <summary>
    /// One test of a compiled bridge: the frame solver, the debris world for broken-off pieces, and the car.
    /// Starts from static equilibrium under self-weight and wind, then steps at a fixed rate. Keep stepping
    /// after a result to let a collapse play out. Runs on a copy, so the design is untouched (spec 2.11).
    /// </summary>
    public sealed class BridgeTestRun
    {
        public readonly BridgeStructure Structure;
        public readonly BridgeSolver Frame;
        public readonly DebrisWorld Debris;
        public readonly CarSim Car;
        public readonly Vector3[] Positions;     // bridge world, current

        readonly Vector3[] _loads;
        readonly List<int> _broken = new List<int>();
        readonly Action<int, Vector3> _addLoad;

        public RunResult Result { get; private set; } = RunResult.Running;
        public string Message { get; private set; } = "";
        public float Time { get; private set; }
        public int BrokenCount { get; private set; }
        public float PeakUtilization { get; private set; }
        public float WorstUtilization { get; private set; }

        public BridgeTestRun(BridgeStructure structure)
        {
            Structure = structure;
            Frame = new BridgeSolver(structure);
            Debris = new DebrisWorld(structure);
            Positions = new Vector3[structure.NodeCount];
            _loads = new Vector3[structure.NodeCount];
            _addLoad = AddLoad;

            Transfer(Frame.Rebuild(true));
            SettleUnderDeadLoad();
            if (!Frame.PrepareDynamics(structure.Settings.FixedDt))
                Finish(RunResult.Failed, "The simulation blew up");
            UpdatePositions();
            Car = new CarSim(structure);
        }

        /// <summary>Static solve, breaking whatever can't carry its own weight, until it holds.</summary>
        void SettleUnderDeadLoad()
        {
            for (int iteration = 0; iteration < 40; iteration++)
            {
                if (!Frame.SolveStatic(Structure.StaticLoad))
                    break;
                Frame.UpdateUtilization();
                TrackUtilization();
                _broken.Clear();
                if (Frame.BreakFailures(_broken) == 0)
                    break;
                BrokenCount += _broken.Count;
                Transfer(Frame.Rebuild(false));
            }
        }

        void Transfer(List<int> detached)
        {
            if (detached.Count == 0)
                return;
            Debris.Add(detached, i => Structure.Rest[i] + Frame.Displacement(i), Frame.Velocity, Frame.Active);
        }

        void AddLoad(int node, Vector3 force)
        {
            if (Frame.NodeInFrame[node])
                _loads[node] += force;
            else
                Debris.AddForce(node, force);
        }

        public void Step()
        {
            var s = Structure.Settings;
            float dt = s.FixedDt;
            Time += dt;

            Array.Copy(Structure.StaticLoad, _loads, _loads.Length);
            Debris.ClearForces();
            Car.ApplyLoads(_addLoad);

            Frame.Step(_loads);
            Frame.UpdateUtilization();
            _broken.Clear();
            if (Frame.BreakFailures(_broken) > 0)
            {
                BrokenCount += _broken.Count;
                Transfer(Frame.Rebuild(false));
                Frame.PrepareDynamics(dt);
            }

            Debris.Step(dt);
            UpdatePositions();
            Car.Step(dt, Positions, Frame.Active);
            TrackUtilization();

            if (Result != RunResult.Running)
                return;
            if (Frame.Singular || !Finite())
                Finish(RunResult.Failed, "The simulation blew up");
            else if (Car.State == CarState.Falling)
                Finish(RunResult.Failed, "The car fell");
            else if (Car.State == CarState.Arrived)
                Finish(RunResult.Passed, "The car made it");
            else if (Time > s.Timeout)
                Finish(RunResult.Failed, "Out of time");
        }

        void UpdatePositions()
        {
            for (int i = 0; i < Positions.Length; i++)
                Positions[i] = Debris.Contains(i) ? Debris.Position(i) : Structure.Rest[i] + Frame.Displacement(i);
        }

        void TrackUtilization()
        {
            float worst = 0f;
            for (int k = 0; k < Structure.ElementCount; k++)
            {
                if (Frame.InFrame[k] && Structure.Kind[k] == ElementKind.Line)
                    worst = MathF.Max(worst, Frame.Utilization[k]);
            }
            WorstUtilization = worst;
            if (Result == RunResult.Running)
                PeakUtilization = MathF.Max(PeakUtilization, MathF.Min(worst, 1f));
        }

        bool Finite()
        {
            foreach (var p in Positions)
            {
                if (float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsNaN(p.Z) || float.IsInfinity(p.X) || float.IsInfinity(p.Y) || float.IsInfinity(p.Z))
                    return false;
            }
            return true;
        }

        void Finish(RunResult result, string message)
        {
            Result = result;
            Message = message;
        }

        /// <summary>Runs to a result. For tests and fixtures.</summary>
        public RunResult RunToEnd(float maxSeconds = 60f)
        {
            while (Result == RunResult.Running && Time < maxSeconds)
                Step();
            return Result;
        }

        /// <summary>Stars (spec 2.9): pass = 1, no members lost = 2, plus peak under 80% and a quarter of the budget left = 3.</summary>
        public int Stars(float budgetUsedFraction)
        {
            if (Result != RunResult.Passed)
                return 0;
            int stars = 1;
            if (BrokenCount == 0)
                stars++;
            if (stars == 2 && PeakUtilization < 0.8f && budgetUsedFraction <= 0.75f)
                stars++;
            return stars;
        }

        public void Write(Snapshot snap)
        {
            snap.Ensure(Structure.NodeCount, Structure.ElementCount);
            snap.Structure = Structure;
            for (int i = 0; i < Positions.Length; i++)
                snap.Positions[i] = Structure.ToRoom(Positions[i]);
            for (int k = 0; k < Structure.ElementCount; k++)
            {
                snap.Utilization[k] = Frame.Utilization[k];
                snap.State[k] = !Frame.Active[k] ? ElementState.Broken : Frame.InFrame[k] ? ElementState.Frame : ElementState.Debris;
            }
            var car = Car;
            snap.CarPosition = Structure.ToRoom(car.Position - car.Up * Structure.Settings.RideHeight);
            snap.CarForward = car.Forward;
            snap.CarUp = car.Up;
            snap.CarState = car.State;
            snap.Result = Result;
            snap.Message = Message;
            snap.Time = Time;
            snap.BrokenCount = BrokenCount;
            snap.PeakUtilization = PeakUtilization;
            snap.WorstUtilization = WorstUtilization;
        }
    }

    public enum ElementState : byte
    {
        Broken,
        Frame,
        Debris,
    }

    /// <summary>What the render thread reads each frame: room-space positions and per-element state (spec 2.12).</summary>
    public sealed class Snapshot
    {
        public BridgeStructure Structure;
        public Vector3[] Positions = Array.Empty<Vector3>();
        public float[] Utilization = Array.Empty<float>();
        public ElementState[] State = Array.Empty<ElementState>();
        public Vector3 CarPosition, CarForward, CarUp;
        public CarState CarState;
        public RunResult Result;
        public string Message = "";
        public float Time;
        public int BrokenCount;
        public float PeakUtilization;
        public float WorstUtilization;

        public void Ensure(int nodes, int elements)
        {
            if (Positions.Length != nodes)
                Positions = new Vector3[nodes];
            if (Utilization.Length != elements)
            {
                Utilization = new float[elements];
                State = new ElementState[elements];
            }
        }
    }

    /// <summary>Dead-load preview (spec 2.10): one static solve under self-weight, colored like a test.</summary>
    public sealed class PreviewResult
    {
        public BridgeStructure Structure;
        public float[] Utilization;
        public bool[] Detached;           // per element: not connected to any support

        public static PreviewResult Compute(BridgeStructure st)
        {
            var solver = new BridgeSolver(st);
            solver.Rebuild(true);
            var loads = new Vector3[st.NodeCount];
            for (int i = 0; i < st.NodeCount; i++)
                loads[i] = new Vector3(0f, -st.Mass[i] * st.Settings.Gravity, 0f);
            solver.SolveStatic(loads);
            solver.UpdateUtilization();

            var result = new PreviewResult
            {
                Structure = st,
                Utilization = (float[])solver.Utilization.Clone(),
                Detached = new bool[st.ElementCount],
            };
            for (int k = 0; k < st.ElementCount; k++)
                result.Detached[k] = !solver.InFrame[k];
            return result;
        }
    }
}
