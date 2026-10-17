using System;

namespace BridgeBuilder.Core
{
    /// <summary>Time source for <see cref="PreviewScheduler"/>, in seconds. Tests set it directly.</summary>
    public interface IClock
    {
        /// <summary>Current time, s. Only differences between readings matter.</summary>
        double Now { get; }
    }

    /// <summary>
    /// Runs the dead-load preview (spec 2.10) once the player has paused after an edit for the
    /// debounce, and publishes it to an <see cref="ISnapshotStore"/> for the render thread (spec 2.12).
    /// A newer edit restarts the debounce and starting to draw clears the pending edit; either one
    /// also discards a preview in progress. Nothing here starts a thread: the Unity host would call <see cref="Tick"/> from its solver
    /// worker, once per solver period, and call <see cref="NotifyEdit"/> and <see cref="NotifyDrawStart"/>
    /// from the main thread.
    /// </summary>
    public sealed class PreviewScheduler
    {
        private readonly object gate = new object();
        private readonly IClock clock;
        private readonly IStaticSolver solver;
        private readonly ISnapshotStore store;

        private SolverInput pendingInput;
        private double pendingDeadline;
        private int editGeneration;
        private int publishedCount;
        private double nextTickTime;

        /// <summary>Debounces with <see cref="Constants.Preview.DebounceSeconds"/>.</summary>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="solverPeriodSeconds"/> is not positive and finite.</exception>
        /// <exception cref="InvalidOperationException">The clock reads a time that is not finite.</exception>
        public PreviewScheduler(IClock clock, IStaticSolver solver, ISnapshotStore store, double solverPeriodSeconds)
            : this(clock, solver, store, solverPeriodSeconds, Constants.Preview.DebounceSeconds)
        {
        }

        /// <param name="clock">Time source shared with the callers of <see cref="NotifyEdit"/>.</param>
        /// <param name="solver">The static solver the preview runs.</param>
        /// <param name="store">Receives each completed preview.</param>
        /// <param name="solverPeriodSeconds">Seconds between calls to <see cref="Tick"/>, which sets <see cref="NextTickTime"/>.</param>
        /// <param name="debounceSeconds">Seconds the player must pause after an edit before the preview runs.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="solverPeriodSeconds"/> is not positive and finite, or <paramref name="debounceSeconds"/> is negative or not finite.
        /// </exception>
        /// <exception cref="InvalidOperationException">The clock reads a time that is not finite.</exception>
        public PreviewScheduler(IClock clock, IStaticSolver solver, ISnapshotStore store, double solverPeriodSeconds, double debounceSeconds)
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.solver = solver ?? throw new ArgumentNullException(nameof(solver));
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            if (!(solverPeriodSeconds > 0.0) || double.IsInfinity(solverPeriodSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(solverPeriodSeconds), solverPeriodSeconds, "The solver period must be positive and finite.");
            }

            if (!(debounceSeconds >= 0.0) || double.IsInfinity(debounceSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(debounceSeconds), debounceSeconds, "The debounce must be zero or positive and finite.");
            }

            SolverPeriodSeconds = solverPeriodSeconds;
            DebounceSeconds = debounceSeconds;
            nextTickTime = ReadClock();
        }

        public double SolverPeriodSeconds { get; }

        public double DebounceSeconds { get; }

        /// <summary>True when an edit is waiting out the debounce.</summary>
        public bool HasPending
        {
            get
            {
                lock (gate)
                {
                    return pendingInput != null;
                }
            }
        }

        /// <summary>
        /// Clock time at which the host should call <see cref="Tick"/> next: one solver period after the
        /// last <see cref="Tick"/> started, or the construction time before the first.
        /// </summary>
        public double NextTickTime
        {
            get
            {
                lock (gate)
                {
                    return nextTickTime;
                }
            }
        }

        /// <summary>
        /// Records an edit now. The preview of <paramref name="roomInput"/> runs on the first
        /// <see cref="Tick"/> at least <see cref="DebounceSeconds"/> later with no newer edit or draw
        /// start in between. The scheduler keeps <paramref name="roomInput"/> and reads it from the
        /// worker, so pass a fresh input such as <see cref="BridgeModel.ToSolverInput"/> returns and do
        /// not change it afterwards. An older pending edit is replaced, and a preview in progress is discarded.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="roomInput"/> is null.</exception>
        /// <exception cref="InvalidOperationException">The clock reads a time that is not finite.</exception>
        public void NotifyEdit(SolverInput roomInput)
        {
            if (roomInput == null)
            {
                throw new ArgumentNullException(nameof(roomInput));
            }

            double now = ReadClock();
            lock (gate)
            {
                editGeneration++;
                pendingInput = roomInput;
                pendingDeadline = now + DebounceSeconds;
            }
        }

        /// <summary>The player started drawing: clears the pending edit and discards a preview in progress.</summary>
        public void NotifyDrawStart()
        {
            lock (gate)
            {
                editGeneration++;
                pendingInput = null;
            }
        }

        /// <summary>
        /// The whole worker loop body. When the pending edit has waited out the debounce, runs
        /// <see cref="DeadLoadPreview.Run(SolverInput, IStaticSolver, Func{bool})"/> on it and publishes the
        /// result, with <see cref="SimulationSnapshot.StepId"/> counting published previews from 1. The
        /// edit stops being pending as soon as its preview starts, so exceptions from the preview, such
        /// as a mechanism, reach the caller once and are not retried.
        /// </summary>
        /// <returns>True when this call published a preview.</returns>
        /// <exception cref="InvalidOperationException">The clock reads a time that is not finite.</exception>
        public bool Tick()
        {
            double now = ReadClock();
            SolverInput input;
            int generation;
            lock (gate)
            {
                nextTickTime = now + SolverPeriodSeconds;
                if (pendingInput == null || now < pendingDeadline)
                {
                    return false;
                }

                input = pendingInput;
                pendingInput = null;
                generation = editGeneration;
            }

            DeadLoadPreviewResult result = DeadLoadPreview.Run(input, solver, () => IsSuperseded(generation));
            if (result.Status == DeadLoadPreviewStatus.Cancelled)
            {
                return false;
            }

            var snapshot = new SimulationSnapshot
            {
                Displacements = result.StaticResult.Displacements,
                Rotations = result.StaticResult.Rotations,
                MemberIds = result.MemberIds,
                Utilizations = result.Utilizations,
            };

            // Checked under the lock so a draw start that lands after the preview's last poll still wins.
            lock (gate)
            {
                if (editGeneration != generation)
                {
                    return false;
                }

                snapshot.StepId = ++publishedCount;
                store.Publish(snapshot);
            }

            return true;
        }

        private bool IsSuperseded(int generation)
        {
            lock (gate)
            {
                return editGeneration != generation;
            }
        }

        private double ReadClock()
        {
            double now = clock.Now;
            if (!double.IsFinite(now))
            {
                throw new InvalidOperationException("The clock read a time that is not finite.");
            }

            return now;
        }
    }
}
