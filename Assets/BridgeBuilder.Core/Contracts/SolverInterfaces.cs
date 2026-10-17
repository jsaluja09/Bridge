using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    // Interfaces shared by every later issue. Later issues implement these and must not edit this file.

    public interface ILinearSolver
    {
        void Add(int row, int column, double value);
        void Factor();
        void Solve(double[] rhs, double[] solution);
    }

    public interface IStaticSolver
    {
        StaticSolveResult Solve(SolverInput input, NodalLoad[] loads);
    }

    public interface IDynamicSolver
    {
        void Reset(SolverInput input, RunConfiguration configuration);
        StepOutput Step(WheelLoad[] previousWheelLoads, double dt, Func<bool> shouldCancel);
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
        None,
        SnapStart,
        NonPlanar,
        Unrecognized,
        Degenerate,
    }

    public struct RecognizerTolerances
    {
        public double SnapStartToleranceMeters;
        public double LineResidualMeters;
        public double PlaneResidualMeters;
        public double ArcResidualMeters;
        public double CornerAngleDegrees;
        public double ArcSweepMinDegrees;
        public double ArcSweepMaxDegrees;
    }

    public sealed class RecognitionResult
    {
        public StrokeKind Kind { get; set; }
        public RejectionReason RejectionReason { get; set; }
        public List<Vec3> Corners { get; set; } = new List<Vec3>();   // line: ends; polyline: ends and corners
        public Vec3 ArcCenter { get; set; }
        public Vec3 ArcNormal { get; set; }
        public double ArcSweepDegrees { get; set; }
    }

    public interface IStrokeRecognizer
    {
        RecognitionResult Recognize(IReadOnlyList<Vec3> stroke, IReadOnlyList<Vec3> snapPoints, RecognizerTolerances tolerances);
    }

    public interface ICarModel
    {
        CarState Step(CarState state, CarParameters parameters, DeckSample[] deck, Vec3 externalForceBridge, double dt);
    }

    public interface ISnapshotStore
    {
        void Publish(SimulationSnapshot snapshot);
        bool TryRead(out SimulationSnapshot snapshot);
    }
}
