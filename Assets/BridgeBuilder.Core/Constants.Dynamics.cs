namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>Bridge dynamics (spec 2.8 and 2.12), read by <see cref="NewmarkIntegrator"/>.</summary>
        public static class Dynamics
        {
            /// <summary>Spec 2.8 ("Newmark average-acceleration or similar"). DERIVED. Newmark beta of the average-acceleration method, which is unconditionally stable for a linear system.</summary>
            public const double NewmarkBeta = 0.25;

            /// <summary>Spec 2.8. DERIVED. Newmark gamma of the average-acceleration method; one half adds no numerical damping.</summary>
            public const double NewmarkGamma = 0.5;

            /// <summary>Spec 2.8 ("light damping") and section 8. GUESS. Viscous damping ratio, the fraction of critical damping; the spec gives no value.</summary>
            public const double DampingRatio = 0.02;

            /// <summary>Spec 2.12 and section 8 ("~60 Hz solver rate"). GUESS. Fixed rate of the solver step, Hz.</summary>
            public const double SolverRateHz = 60.0;
        }
    }
}
