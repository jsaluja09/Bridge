namespace BridgeBuilder.Core
{
    public enum RunResult
    {
        Running,
        Passed,
        Failed,
    }

    /// <summary>
    /// One test of a design: gravity ramps in while the bridge settles, then the car drives across.
    /// Works on its own 3D copy, so the design is untouched. Keep calling Step after the result
    /// is known to let the collapse play out.
    /// </summary>
    public sealed class BridgeTestRun
    {
        readonly BridgeSettings _s;

        public readonly BridgeStructure Structure;
        public readonly CarSim Car;
        public float Time { get; private set; }
        public RunResult Result { get; private set; } = RunResult.Running;
        public string Message { get; private set; } = "";

        public BridgeSolver Solver => Structure.Solver;

        public BridgeTestRun(BridgeDesign design, BridgeFrame frame, BridgeSettings settings)
        {
            _s = settings;
            Structure = BridgeStructure.Build(design, frame, settings);
            Car = new CarSim(settings, Structure);
        }

        public void Step()
        {
            float dt = _s.FixedDt;
            Time += dt;
            var solver = Structure.Solver;

            bool settling = Time < _s.SettleTime;
            solver.GravityScale = settling ? Time / _s.SettleTime : 1f;
            solver.DampingRate = settling ? _s.SettleDamping : _s.Damping;
            if (!settling)
                Car.StartDriving();

            solver.ClearLoads();
            Car.PreStep(dt);
            solver.Step(dt);
            Car.PostStep();

            if (Result != RunResult.Running)
                return;

            if (solver.Exploded)
                Finish(RunResult.Failed, "The simulation blew up");
            else if (Car.State == CarState.Falling)
                Finish(RunResult.Failed, "The car fell");
            else if (Car.State == CarState.Arrived)
                Finish(RunResult.Passed, "The car made it");
            else if (Time > _s.SettleTime + _s.Timeout)
                Finish(RunResult.Failed, "Out of time");
        }

        void Finish(RunResult result, string message)
        {
            Result = result;
            Message = message;
        }

        /// <summary>Runs to a result (or a time limit). For tests.</summary>
        public RunResult RunToEnd(float maxSeconds = 60f)
        {
            while (Result == RunResult.Running && Time < maxSeconds)
                Step();
            return Result;
        }
    }
}
