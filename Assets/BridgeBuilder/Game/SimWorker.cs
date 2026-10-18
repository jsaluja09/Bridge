using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using BridgeBuilder.Core;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// Runs the bridge and car on a background thread at a fixed rate (spec 2.12), and the dead-load preview
    /// (spec 2.10). The render thread takes the newest snapshot; three snapshot buffers are swapped under a short
    /// lock and reused, so steady stepping allocates little. A newer preview request cancels an older one.
    /// </summary>
    public sealed class SimWorker : IDisposable
    {
        readonly object _lock = new object();
        readonly Thread _thread;
        volatile bool _alive = true;

        CompiledBridge _pendingRun;
        RunConfiguration _pendingConfig;
        double? _pendingFloor;
        bool _pendingStop;
        CompiledBridge _pendingPreview;
        volatile int _previewTicket;

        RunSnapshot _back = new RunSnapshot(), _ready = new RunSnapshot(), _front = new RunSnapshot();
        double[] _backBuckling = new double[0], _readyBuckling = new double[0], _frontBuckling = new double[0];
        CarPose _backCar = new CarPose(), _readyCar = new CarPose(), _frontCar = new CarPose();
        bool _hasNew;
        readonly List<(CompiledBridge bridge, FailureReport report)> _failures = new List<(CompiledBridge, FailureReport)>();
        (CompiledBridge bridge, PreviewResult result) _previewReady;

        // The live solver benchmark (spec 2.12): measured on the headset.
        public volatile float StepMs;
        public volatile float StartMs;
        public volatile float PreviewMs;
        public volatile int Members;
        public volatile string Error;

        public SimWorker()
        {
            _thread = new Thread(Loop) { IsBackground = true, Name = "Bridge Simulation" };
            _thread.Start();
        }

        public void StartRun(CompiledBridge bridge, RunConfiguration config, double floorRoomHeight)
        {
            lock (_lock)
            {
                _pendingRun = bridge;
                _pendingConfig = config;
                _pendingFloor = floorRoomHeight;
                _pendingStop = false;
            }
        }

        public void StopRun()
        {
            lock (_lock)
            {
                _pendingRun = null;
                _pendingStop = true;
            }
        }

        public void RequestPreview(CompiledBridge bridge)
        {
            lock (_lock)
            {
                _pendingPreview = bridge;
                _previewTicket++;
            }
        }

        /// <summary>The newest run snapshot (Bridge is null until the run has started).</summary>
        public RunSnapshot Latest()
        {
            lock (_lock)
            {
                if (_hasNew)
                {
                    (_ready, _front) = (_front, _ready);
                    (_readyBuckling, _frontBuckling) = (_frontBuckling, _readyBuckling);
                    (_readyCar, _frontCar) = (_frontCar, _readyCar);
                    _hasNew = false;
                }
            }
            return _front;
        }

        /// <summary>Buckling utilization per member for the snapshot Latest() last returned (W4-07 bowing).</summary>
        public double[] LatestBuckling => _frontBuckling;

        /// <summary>Where to draw the car for the snapshot Latest() last returned (W4-08).</summary>
        public CarPose LatestCarPose => _frontCar;

        /// <summary>Moves the failure reports (W4-05) for this run since the last call into the list.</summary>
        public void TakeFailures(CompiledBridge bridge, List<FailureReport> into)
        {
            lock (_lock)
            {
                foreach (var (b, report) in _failures)
                {
                    if (b == bridge)
                        into.Add(report);
                }
                _failures.Clear();
            }
        }

        public bool TakePreview(out CompiledBridge bridge, out PreviewResult result)
        {
            lock (_lock)
            {
                (bridge, result) = _previewReady;
                _previewReady = default;
            }
            return result != null;
        }

        void Loop()
        {
            BridgeRun run = null;
            FailureTracker tracker = null;
            CarPoseTracker car = null;
            long step = 0;
            var clock = Stopwatch.StartNew();
            var timer = new Stopwatch();
            double next = 0, dt = 1.0 / Constants.Dynamics.SolverRateHz;

            while (_alive)
            {
                CompiledBridge start, preview;
                RunConfiguration config;
                double? floor;
                bool stop;
                int ticket;
                lock (_lock)
                {
                    start = _pendingRun;
                    config = _pendingConfig;
                    floor = _pendingFloor;
                    _pendingRun = null;
                    preview = _pendingPreview;
                    _pendingPreview = null;
                    ticket = _previewTicket;
                    stop = _pendingStop;
                    _pendingStop = false;
                }

                try
                {
                    if (stop)
                        run = null;

                    if (start != null)
                    {
                        timer.Restart();
                        run = new BridgeRun(start, config, floor);
                        StartMs = (float)timer.Elapsed.TotalMilliseconds;
                        tracker = new FailureTracker(start.Input, start.Input.ScaleFactor > 0 ? start.Input.ScaleFactor : 1.0);
                        step = 0;
                        Report(run, tracker.Observe(run.Coupled.Frame, step));   // broke while settling
                        car = new CarPoseTracker(config.Car, config.ScaleFactor, floor ?? double.NegativeInfinity);
                        car.Update(run.Coupled.Car, run.Result, 0);
                        Members = start.Input.Members.Count;
                        next = clock.Elapsed.TotalSeconds;
                        Publish(run, car);
                    }

                    if (preview != null && run == null)
                    {
                        timer.Restart();
                        var result = DeadLoadPreview.Run(preview.Input, new BandedStaticSolver(), () => ticket != _previewTicket);
                        PreviewMs = (float)timer.Elapsed.TotalMilliseconds;
                        if (result.Status == PreviewStatus.Completed)
                        {
                            lock (_lock)
                                _previewReady = (preview, result);
                        }
                    }

                    if (run != null)
                    {
                        double now = clock.Elapsed.TotalSeconds;
                        int steps = 0;
                        while (now >= next && steps < 3)
                        {
                            timer.Restart();
                            run.Step(dt);
                            StepMs = (float)timer.Elapsed.TotalMilliseconds;
                            Report(run, tracker.Observe(run.Coupled.Frame, ++step));
                            car.Update(run.Coupled.Car, run.Result, dt);
                            next += dt;
                            steps++;
                        }
                        if (now - next > 0.25)
                            next = now;      // fell far behind (a hitch): don't try to catch up
                        if (steps > 0)
                            Publish(run, car);
                        if (run.Result != RunResult.Running && run.Time > 30)
                            run = null;      // the collapse has played out
                    }
                }
                catch (Exception e)
                {
                    Error = e.Message;
                    UnityEngine.Debug.LogException(e);
                    run = null;
                }

                Thread.Sleep(run != null ? 1 : 5);
            }
        }

        void Publish(BridgeRun run, CarPoseTracker car)
        {
            run.Write(_back);
            car.Pose.CopyTo(_backCar);
            if (run.Coupled.Frame is FrameDynamics frame)
            {
                if (_backBuckling.Length != frame.BucklingUtilization.Length)
                    _backBuckling = new double[frame.BucklingUtilization.Length];
                Array.Copy(frame.BucklingUtilization, _backBuckling, _backBuckling.Length);
            }
            lock (_lock)
            {
                (_back, _ready) = (_ready, _back);
                (_backBuckling, _readyBuckling) = (_readyBuckling, _backBuckling);
                (_backCar, _readyCar) = (_readyCar, _backCar);
                _hasNew = true;
            }
        }

        void Report(BridgeRun run, FailureReport report)
        {
            if (report == null)
                return;
            lock (_lock)
                _failures.Add((run.Bridge, report));
        }

        public void Dispose()
        {
            _alive = false;
            _thread.Join(200);
        }
    }
}
