using System;
using System.Diagnostics;
using System.Threading;
using BridgeBuilder.Core;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// Runs the solver and car on a background thread at a fixed rate (spec 2.12). The render thread
    /// reads the newest snapshot; buffers are swapped under a short lock and reused, so steady-state
    /// stepping allocates nothing. Also computes dead-load previews while the player builds.
    /// </summary>
    public sealed class SimWorker : IDisposable
    {
        readonly object _lock = new object();
        readonly Thread _thread;
        volatile bool _alive = true;

        BridgeStructure _pendingRun;
        bool _pendingStop;
        BridgeStructure _pendingPreview;

        Snapshot _back = new Snapshot(), _ready = new Snapshot(), _front = new Snapshot();
        bool _hasNew;
        PreviewResult _previewReady;

        /// <summary>Measured on the device: the spec's node-cap benchmark, live.</summary>
        public volatile float StepMs;
        public volatile float StartMs;
        public volatile float PreviewMs;
        public volatile int Equations;
        public volatile int Bandwidth;
        public volatile string Error;

        public SimWorker()
        {
            _thread = new Thread(Loop) { IsBackground = true, Name = "Bridge Simulation" };
            _thread.Start();
        }

        public void StartRun(BridgeStructure structure)
        {
            lock (_lock)
            {
                _pendingRun = structure;
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

        public void RequestPreview(BridgeStructure structure)
        {
            lock (_lock)
                _pendingPreview = structure;
        }

        /// <summary>The newest snapshot, or the last one if nothing new arrived.</summary>
        public Snapshot Latest()
        {
            lock (_lock)
            {
                if (_hasNew)
                {
                    (_ready, _front) = (_front, _ready);
                    _hasNew = false;
                }
            }
            return _front;
        }

        public PreviewResult TakePreview()
        {
            lock (_lock)
            {
                var p = _previewReady;
                _previewReady = null;
                return p;
            }
        }

        void Loop()
        {
            BridgeTestRun run = null;
            var clock = Stopwatch.StartNew();
            var timer = new Stopwatch();
            double next = 0;

            while (_alive)
            {
                BridgeStructure start, preview;
                bool stop;
                lock (_lock)
                {
                    start = _pendingRun;
                    _pendingRun = null;
                    preview = _pendingPreview;
                    _pendingPreview = null;
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
                        run = new BridgeTestRun(start);
                        StartMs = (float)timer.Elapsed.TotalMilliseconds;
                        Equations = run.Frame.Equations;
                        Bandwidth = run.Frame.Bandwidth;
                        next = clock.Elapsed.TotalSeconds;
                        Publish(run);
                    }

                    if (preview != null && run == null)
                    {
                        timer.Restart();
                        var result = PreviewResult.Compute(preview);
                        PreviewMs = (float)timer.Elapsed.TotalMilliseconds;
                        lock (_lock)
                            _previewReady = result;
                    }

                    if (run != null)
                    {
                        double dt = run.Structure.Settings.FixedDt;
                        double now = clock.Elapsed.TotalSeconds;
                        int steps = 0;
                        while (now >= next && steps < 3)
                        {
                            timer.Restart();
                            run.Step();
                            StepMs = (float)timer.Elapsed.TotalMilliseconds;
                            Equations = run.Frame.Equations;
                            next += dt;
                            steps++;
                        }
                        if (now - next > 0.25)
                            next = now;      // fell far behind (a hitch): don't try to catch up
                        if (steps > 0)
                            Publish(run);
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

        void Publish(BridgeTestRun run)
        {
            run.Write(_back);
            lock (_lock)
            {
                (_back, _ready) = (_ready, _back);
                _hasNew = true;
            }
        }

        public void Dispose()
        {
            _alive = false;
            _thread.Join(200);
        }
    }
}
