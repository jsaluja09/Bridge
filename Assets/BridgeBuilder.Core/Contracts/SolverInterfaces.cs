using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>A square linear system assembled entry by entry, factored once, then solved many times.</summary>
    public interface ILinearSolver
    {
        /// <summary>
        /// Adds <paramref name="value"/> to the entry at (row, column). Repeated calls accumulate.
        /// Callers add every nonzero entry, so a symmetric matrix gets both (i, j) and (j, i).
        /// </summary>
        void Add(int row, int column, double value);

        void Factor();

        /// <summary>Solves the factored system for <paramref name="rhs"/> and writes the result into <paramref name="solution"/>.</summary>
        void Solve(double[] rhs, double[] solution);
    }

    public interface IStaticSolver
    {
        StaticSolveResult Solve(SolverInput input, NodalLoad[] loads);
    }

    public interface IDynamicSolver
    {
        void Reset(SolverInput input, RunConfiguration configuration);

        /// <summary>
        /// Advances one step of <paramref name="dt"/> seconds using the car's wheel loads from the
        /// previous step (lagged coupling, section 2.8).
        /// </summary>
        StepOutput Step(WheelLoad[] previousWheelLoads, double dt, Func<bool> shouldCancel);
    }

    public interface IStrokeRecognizer
    {
        RecognitionResult Recognize(IReadOnlyList<Vec3> stroke, IReadOnlyList<Vec3> snapPoints, RecognizerTolerances tolerances);
    }

    public enum StrokeKind
    {
        Line,
        Arc,
        Polyline,
        Rejected,
    }

    public enum RejectionReason
    {
        /// <summary>The stroke was accepted.</summary>
        None,
        SnapStart,
        NonPlanar,
        Unrecognized,
        Degenerate,
    }

    public class RecognitionResult
    {
        public StrokeKind Kind { get; set; }

        /// <summary><see cref="RejectionReason.None"/> unless <see cref="Kind"/> is <see cref="StrokeKind.Rejected"/>.</summary>
        public RejectionReason Reason { get; set; }

        /// <summary>
        /// Fitted path in stroke order, in the stroke's units. Line: start and end. Polyline: start,
        /// each corner, end. Arc: start, the point at mid-sweep, end. Empty when rejected.
        /// </summary>
        public Vec3[] Vertices { get; set; } = Array.Empty<Vec3>();

        public bool IsAccepted => Kind != StrokeKind.Rejected;

        public static RecognitionResult Accepted(StrokeKind kind, Vec3[] vertices)
        {
            if (kind == StrokeKind.Rejected)
            {
                throw new ArgumentException("Use Reject for a rejected stroke.", nameof(kind));
            }

            return new RecognitionResult { Kind = kind, Reason = RejectionReason.None, Vertices = vertices ?? Array.Empty<Vec3>() };
        }

        public static RecognitionResult Reject(RejectionReason reason)
        {
            if (reason == RejectionReason.None)
            {
                throw new ArgumentException("A rejected stroke needs a reason.", nameof(reason));
            }

            return new RecognitionResult { Kind = StrokeKind.Rejected, Reason = reason };
        }
    }

    /// <summary>Recognizer thresholds (section 2.1, deferred to tuning in section 7). Distances in meters, angles in degrees.</summary>
    public struct RecognizerTolerances
    {
        public RecognizerTolerances(
            double snapStartToleranceMeters,
            double lineResidualMeters,
            double planeResidualMeters,
            double arcResidualMeters,
            double cornerAngleDegrees,
            double arcSweepMinDegrees,
            double arcSweepMaxDegrees)
        {
            SnapStartToleranceMeters = snapStartToleranceMeters;
            LineResidualMeters = lineResidualMeters;
            PlaneResidualMeters = planeResidualMeters;
            ArcResidualMeters = arcResidualMeters;
            CornerAngleDegrees = cornerAngleDegrees;
            ArcSweepMinDegrees = arcSweepMinDegrees;
            ArcSweepMaxDegrees = arcSweepMaxDegrees;
        }

        public double SnapStartToleranceMeters { get; set; }

        public double LineResidualMeters { get; set; }

        public double PlaneResidualMeters { get; set; }

        public double ArcResidualMeters { get; set; }

        public double CornerAngleDegrees { get; set; }

        public double ArcSweepMinDegrees { get; set; }

        public double ArcSweepMaxDegrees { get; set; }
    }

    public interface ICarModel
    {
        /// <summary>
        /// Advances the car by <paramref name="dt"/> seconds over the deck sampled at each wheel.
        /// <paramref name="externalForceBridge"/> is an extra force on the car body in bridge-world newtons.
        /// </summary>
        CarState Step(CarState state, CarParameters parameters, DeckSample[] deck, Vec3 externalForceBridge, double dt);
    }

    /// <summary>Single-slot mailbox between the solver worker and the render thread (section 2.12).</summary>
    public interface ISnapshotStore
    {
        void Publish(SimulationSnapshot snapshot);

        /// <summary>Returns false when nothing has been published yet.</summary>
        bool TryRead(out SimulationSnapshot snapshot);
    }
}
