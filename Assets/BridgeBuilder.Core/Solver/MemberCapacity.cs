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
    /// Closed-form capacity per member (spec 2.7): tension, compression (crushing), Euler buckling, and
    /// bending. Utilization is the maximum of the four ratios, not a sum (the spec gives no interaction rule).
    /// A material may fail at a different stress when pulled than when pushed (a cable, concrete).
    /// </summary>
    public static class MemberCapacity
    {
        /// <summary>Euler critical load, pi^2 E I / (K L)^2.</summary>
        public static double EulerCriticalLoad(double E, double I, double K, double L) =>
            Math.PI * Math.PI * E * I / ((K * L) * (K * L));

        public static double EulerCriticalLoad(double E, double I, double L) =>
            EulerCriticalLoad(E, I, Constants.Capacity.EffectiveLengthFactor, L);

        /// <summary>The axial stress at which the section fails when pulled.</summary>
        public static double TensionStrength(SectionProperties s) => s.TensionStrength > 0 ? s.TensionStrength : s.YieldStrength;

        /// <summary>The axial stress at which the section crushes when pushed.</summary>
        public static double CompressionStrength(SectionProperties s) => s.CompressionStrength > 0 ? s.CompressionStrength : s.YieldStrength;

        /// <summary>As the full overload, with Fy for bending, tension and compression alike.</summary>
        public static CapacityResult Evaluate(double N, double My, double Mz, double E, double Iy, double Iz, double A,
                                              double Fy, double cy, double cz, double K, double bucklingLength) =>
            Evaluate(N, My, Mz, E, Iy, Iz, A, Fy, Fy, Fy, cy, cz, K, bucklingLength);

        /// <summary>
        /// N is tension-positive axial force; My and Mz are the larger end moments about local y and z.
        /// Fy is the bending strength, Ft and Fc the axial strengths in tension and compression.
        /// bucklingLength is the member's unbraced length.
        /// </summary>
        public static CapacityResult Evaluate(double N, double My, double Mz, double E, double Iy, double Iz, double A,
                                              double Fy, double Ft, double Fc, double cy, double cz, double K, double bucklingLength)
        {
            if (double.IsNaN(N) || double.IsNaN(My) || double.IsNaN(Mz))
                throw new ArgumentOutOfRangeException(nameof(N), "Force is NaN");

            double pcr = EulerCriticalLoad(E, Math.Min(Iy, Iz), K, bucklingLength);
            double tension = Math.Max(N, 0) / (Ft * A);
            double compression = Math.Max(-N, 0) / (Fc * A);
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
            return Evaluate(f.Axial, my, mz, s.E, s.Iy, s.Iz, s.A, s.YieldStrength, TensionStrength(s), CompressionStrength(s),
                            s.OuterFiberY, s.OuterFiberZ, Constants.Capacity.EffectiveLengthFactor, bucklingLength);
        }
    }
}
