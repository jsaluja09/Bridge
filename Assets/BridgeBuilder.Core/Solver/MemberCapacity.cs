using System;

namespace BridgeBuilder.Core
{
    public enum FailureMode
    {
        None,
        Tension,
        Compression,
        Buckling,
        Bending,
    }

    /// <summary>One member's capacity check result: utilization is the worst single check (spec 2.7, 2.10).</summary>
    public readonly struct CapacityResult
    {
        public readonly double Utilization;
        public readonly double SignedForce;     // tension positive
        public readonly FailureMode Governing;
        public readonly double BucklingUtilization;
        public bool Failed => Utilization >= 1.0;

        public CapacityResult(double utilization, double signedForce, FailureMode governing, double bucklingUtilization)
        {
            Utilization = utilization;
            SignedForce = signedForce;
            Governing = governing;
            BucklingUtilization = bucklingUtilization;
        }
    }

    /// <summary>
    /// Closed-form capacity per member (spec 2.7): tension yield, compression yield, Euler buckling, and
    /// bending. Utilization is the maximum of the four ratios, not a sum (the spec gives no interaction rule).
    /// </summary>
    public static class MemberCapacity
    {
        /// <summary>Euler critical load, pi^2 E I / (K L)^2.</summary>
        public static double EulerCriticalLoad(double E, double I, double K, double L) =>
            Math.PI * Math.PI * E * I / ((K * L) * (K * L));

        public static double EulerCriticalLoad(double E, double I, double L) =>
            EulerCriticalLoad(E, I, Constants.Capacity.EffectiveLengthFactor, L);

        /// <summary>
        /// N is tension-positive axial force; My and Mz are the larger end moments about local y and z.
        /// bucklingLength is the member's unbraced length.
        /// </summary>
        public static CapacityResult Evaluate(double N, double My, double Mz, double E, double Iy, double Iz, double A,
                                              double Fy, double cy, double cz, double K, double bucklingLength)
        {
            if (double.IsNaN(N) || double.IsNaN(My) || double.IsNaN(Mz))
                throw new ArgumentOutOfRangeException(nameof(N), "Force is NaN");

            double pcr = EulerCriticalLoad(E, Math.Min(Iy, Iz), K, bucklingLength);
            double tension = Math.Max(N, 0) / (Fy * A);
            double compression = Math.Max(-N, 0) / (Fy * A);
            double buckling = Math.Max(-N, 0) / pcr;
            double bending = Math.Max(Math.Abs(My) / (Fy * Iy / cy), Math.Abs(Mz) / (Fy * Iz / cz));

            double worst = tension;
            var mode = tension > 0 ? FailureMode.Tension : FailureMode.None;
            if (compression > worst) { worst = compression; mode = FailureMode.Compression; }
            if (buckling > worst) { worst = buckling; mode = FailureMode.Buckling; }
            if (bending > worst) { worst = bending; mode = FailureMode.Bending; }
            return new CapacityResult(worst, N, mode, buckling);
        }

        public static CapacityResult Evaluate(double N, double My, double Mz, double E, double Iy, double Iz, double A,
                                              double Fy, double cy, double cz, double bucklingLength) =>
            Evaluate(N, My, Mz, E, Iy, Iz, A, Fy, cy, cz, Constants.Capacity.EffectiveLengthFactor, bucklingLength);

        /// <summary>Evaluates a solved member force against its section.</summary>
        public static CapacityResult Evaluate(MemberForce f, SectionProperties s, double bucklingLength)
        {
            double my = Math.Max(Math.Abs(f.MomentLocalYAtA), Math.Abs(f.MomentLocalYAtB));
            double mz = Math.Max(Math.Abs(f.MomentLocalZAtA), Math.Abs(f.MomentLocalZAtB));
            return Evaluate(f.Axial, my, mz, s.E, s.Iy, s.Iz, s.A, s.YieldStrength, s.OuterFiberY, s.OuterFiberZ, bucklingLength);
        }
    }
}
