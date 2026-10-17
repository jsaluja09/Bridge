using System;

namespace BridgeBuilder.Core
{
    public interface IClock
    {
        double Now { get; }
    }

    /// <summary>
    /// Runs the dead-load preview once the player has paused for the debounce time after an edit, and cancels
    /// a pending run when drawing starts again (spec 2.10). Tick is the whole loop body; the Unity host calls it
    /// (from a worker or the main thread). The clock is injected so tests set the time.
    /// </summary>
    public sealed class PreviewScheduler
    {
        readonly IClock _clock;
        readonly Action _runPreview;
        readonly double _debounce;
        double _pausedAt;
        bool _pending;

        public bool Pending => _pending;

        public PreviewScheduler(IClock clock, Action runPreview) : this(clock, runPreview, Constants.Preview.DebounceSeconds) { }

        public PreviewScheduler(IClock clock, Action runPreview, double debounceSeconds)
        {
            _clock = clock;
            _runPreview = runPreview;
            _debounce = debounceSeconds;
        }

        /// <summary>An edit finished and the player stopped drawing: start the debounce.</summary>
        public void NotifyPaused()
        {
            _pausedAt = _clock.Now;
            _pending = true;
        }

        /// <summary>The player started drawing: drop any pending preview.</summary>
        public void NotifyDrawStarted() => _pending = false;

        public void Tick()
        {
            if (!_pending || _clock.Now - _pausedAt < _debounce - 1e-12)
                return;
            _pending = false;
            _runPreview();
        }
    }
}
