using System;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Result of one member capacity check (spec 2.7 and 2.10). Each ratio is demand over
    /// capacity: 0 when unloaded, 1 at capacity.
    /// </summary>
    public readonly struct MemberCapacityResult
    {
        internal MemberCapacityResult(
            double signedForce,
            double tensionUtilization,
            double compressionUtilization,
            double bucklingUtilization,
            double bendingUtilization,
            double criticalLoad)
        {
            SignedForce = signedForce;
            TensionUtilization = tensionUtilization;
            CompressionUtilization = compressionUtilization;
            BucklingUtilization = bucklingUtilization;
            BendingUtilization = bendingUtilization;
            CriticalLoad = criticalLoad;
            Utilization = Math.Max(
                Math.Max(tensionUtilization, compressionUtilization),
                Math.Max(bucklingUtilization, bendingUtilization));
        }

        /// <summary>Signed axial force N, tension positive (spec 2.10). Equals the axial force that was checked.</summary>
        public double SignedForce { get; }

        /// <summary>Tension yield: <c>max(N, 0) / (Fy * A)</c>.</summary>
        public double TensionUtilization { get; }

        /// <summary>Compression yield: <c>max(-N, 0) / (Fy * A)</c>.</summary>
        public double CompressionUtilization { get; }

        /// <summary>Euler buckling: <c>max(-N, 0) / Pcr</c>.</summary>
        public double BucklingUtilization { get; }

        /// <summary>Bending yield: <c>max(|My| / (Fy * Iy / Cy), |Mz| / (Fy * Iz / Cz))</c>.</summary>
        public double BendingUtilization { get; }

        /// <summary>Euler critical load Pcr about the weak axis, N.</summary>
        public double CriticalLoad { get; }

        /// <summary>
        /// The worst of the tension, compression, buckling, and bending ratios, not their sum. The
        /// spec gives no interaction equation, so the checks are not combined.
        /// </summary>
        public double Utilization { get; }

        /// <summary>True when <see cref="Utilization"/> is 1 or more.</summary>
        public bool Failed => Utilization >= 1.0;
    }

    /// <summary>
    /// One closed-form capacity test per member (spec 2.7): tension yield, compression yield,
    /// Euler buckling, and bending yield, each checked separately. Inputs are bridge-world SI
    /// units: N, N*m, m, Pa. Checking a member never removes it or re-solves the structure.
    /// </summary>
    public static class MemberCapacity
    {
        /// <summary>Euler critical load <c>pi^2 * E * I / (K * L)^2</c>, N.</summary>
        /// <param name="youngsModulus">E, Pa.</param>
        /// <param name="secondMoment">I, m^4.</param>
        /// <param name="effectiveLengthFactor">K.</param>
        /// <param name="length">Member length L, m.</param>
        /// <exception cref="ArgumentOutOfRangeException">An argument is not positive and finite.</exception>
        public static double EulerCriticalLoad(double youngsModulus, double secondMoment, double effectiveLengthFactor, double length)
        {
            RequirePositive(youngsModulus, nameof(youngsModulus), "Young's modulus");
            RequirePositive(secondMoment, nameof(secondMoment), "The second moment of area");
            RequirePositive(effectiveLengthFactor, nameof(effectiveLengthFactor), "The effective-length factor");
            RequirePositive(length, nameof(length), "The member length");
            double effectiveLength = effectiveLengthFactor * length;
            return Math.PI * Math.PI * youngsModulus * secondMoment / (effectiveLength * effectiveLength);
        }

        /// <summary>Euler critical load with K = <see cref="Constants.Capacity.EffectiveLengthFactor"/>, N.</summary>
        /// <exception cref="ArgumentOutOfRangeException">An argument is not positive and finite.</exception>
        public static double EulerCriticalLoad(double youngsModulus, double secondMoment, double length)
        {
            return EulerCriticalLoad(youngsModulus, secondMoment, Constants.Capacity.EffectiveLengthFactor, length);
        }

        /// <summary>
        /// Checks one member. Buckling uses the weak axis, <c>I = min(Iy, Iz)</c>. Bending pairs
        /// <paramref name="momentY"/> with <see cref="SectionProperties.Iy"/> and
        /// <see cref="SectionProperties.Cy"/>, and <paramref name="momentZ"/> with
        /// <see cref="SectionProperties.Iz"/> and <see cref="SectionProperties.Cz"/>.
        /// </summary>
        /// <param name="axial">Signed axial force, N, tension positive.</param>
        /// <param name="momentY">Bending moment about local y, N*m. The sign is ignored.</param>
        /// <param name="momentZ">Bending moment about local z, N*m. The sign is ignored.</param>
        /// <param name="section">Supplies E, A, Iy, Iz, Cy, Cz, and the yield strength.</param>
        /// <param name="length">Member length in bridge-world meters.</param>
        /// <param name="effectiveLengthFactor">Euler effective-length factor K.</param>
        /// <exception cref="ArgumentNullException"><paramref name="section"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// A force or moment is not finite, or a section property, the length, or K is not positive and finite.
        /// </exception>
        public static MemberCapacityResult Evaluate(
            double axial,
            double momentY,
            double momentZ,
            SectionProperties section,
            double length,
            double effectiveLengthFactor)
        {
            if (section == null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            RequireFinite(axial, nameof(axial), "The axial force");
            RequireFinite(momentY, nameof(momentY), "The moment about local y");
            RequireFinite(momentZ, nameof(momentZ), "The moment about local z");
            RequirePositive(section.E, nameof(section), "SectionProperties.E");
            RequirePositive(section.A, nameof(section), "SectionProperties.A");
            RequirePositive(section.Iy, nameof(section), "SectionProperties.Iy");
            RequirePositive(section.Iz, nameof(section), "SectionProperties.Iz");
            RequirePositive(section.Cy, nameof(section), "SectionProperties.Cy");
            RequirePositive(section.Cz, nameof(section), "SectionProperties.Cz");
            RequirePositive(section.YieldStrength, nameof(section), "SectionProperties.YieldStrength");

            double yieldStrength = section.YieldStrength;
            double criticalLoad = EulerCriticalLoad(section.E, Math.Min(section.Iy, section.Iz), effectiveLengthFactor, length);
            double squashLoad = yieldStrength * section.A;
            double tension = Math.Max(axial, 0.0);
            double compression = Math.Max(-axial, 0.0);
            double bending = Math.Max(
                Math.Abs(momentY) / (yieldStrength * section.Iy / section.Cy),
                Math.Abs(momentZ) / (yieldStrength * section.Iz / section.Cz));

            return new MemberCapacityResult(
                axial,
                tension / squashLoad,
                compression / squashLoad,
                compression / criticalLoad,
                bending,
                criticalLoad);
        }

        /// <summary>Checks one member with K = <see cref="Constants.Capacity.EffectiveLengthFactor"/>.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="section"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A force or moment is not finite, or a section property or the length is not positive and finite.</exception>
        public static MemberCapacityResult Evaluate(double axial, double momentY, double momentZ, SectionProperties section, double length)
        {
            return Evaluate(axial, momentY, momentZ, section, length, Constants.Capacity.EffectiveLengthFactor);
        }

        /// <summary>
        /// Checks one member from solver output. Each bending plane uses the larger magnitude of its
        /// end-A and end-B moments: <see cref="MemberForce.MomentLocalYAtA"/> and
        /// <see cref="MemberForce.MomentLocalYAtB"/> against Iy and Cy,
        /// <see cref="MemberForce.MomentLocalZAtA"/> and <see cref="MemberForce.MomentLocalZAtB"/>
        /// against Iz and Cz. <paramref name="force"/> is not modified.
        /// </summary>
        /// <param name="force">Member end actions. <see cref="MemberForce.Axial"/> is tension positive.</param>
        /// <param name="section">The member's section.</param>
        /// <param name="length">Member length in bridge-world meters.</param>
        /// <param name="effectiveLengthFactor">Euler effective-length factor K.</param>
        /// <exception cref="ArgumentNullException"><paramref name="force"/> or <paramref name="section"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// A force or moment is not finite, or a section property, the length, or K is not positive and finite.
        /// </exception>
        public static MemberCapacityResult Evaluate(MemberForce force, SectionProperties section, double length, double effectiveLengthFactor)
        {
            if (force == null)
            {
                throw new ArgumentNullException(nameof(force));
            }

            RequireFinite(force.Axial, nameof(force), "MemberForce.Axial");
            RequireFinite(force.MomentLocalYAtA, nameof(force), "MemberForce.MomentLocalYAtA");
            RequireFinite(force.MomentLocalYAtB, nameof(force), "MemberForce.MomentLocalYAtB");
            RequireFinite(force.MomentLocalZAtA, nameof(force), "MemberForce.MomentLocalZAtA");
            RequireFinite(force.MomentLocalZAtB, nameof(force), "MemberForce.MomentLocalZAtB");

            double momentY = Math.Max(Math.Abs(force.MomentLocalYAtA), Math.Abs(force.MomentLocalYAtB));
            double momentZ = Math.Max(Math.Abs(force.MomentLocalZAtA), Math.Abs(force.MomentLocalZAtB));
            return Evaluate(force.Axial, momentY, momentZ, section, length, effectiveLengthFactor);
        }

        /// <summary>Checks one member from solver output with K = <see cref="Constants.Capacity.EffectiveLengthFactor"/>.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="force"/> or <paramref name="section"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A force or moment is not finite, or a section property or the length is not positive and finite.</exception>
        public static MemberCapacityResult Evaluate(MemberForce force, SectionProperties section, double length)
        {
            return Evaluate(force, section, length, Constants.Capacity.EffectiveLengthFactor);
        }

        private static void RequireFinite(double value, string parameterName, string description)
        {
            if (!double.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(parameterName, value, description + " must be finite.");
            }
        }

        private static void RequirePositive(double value, string parameterName, string description)
        {
            if (!(value > 0.0) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(parameterName, value, description + " must be positive and finite.");
            }
        }
    }
}
