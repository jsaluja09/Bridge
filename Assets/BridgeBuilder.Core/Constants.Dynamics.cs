namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        public static class Dynamics
        {
            /// <summary>Spec 2.8: Newmark average acceleration. DERIVED.</summary>
            public const double NewmarkBeta = 0.25;

            /// <summary>Spec 2.8: Newmark average acceleration. DERIVED.</summary>
            public const double NewmarkGamma = 0.5;

            /// <summary>Spec 2.8 and 8: "light damping"; no value given. GUESS.</summary>
            public const double DampingRatio = 0.02;

            /// <summary>Spec 2.12 and 8: "about 60 Hz". GUESS.</summary>
            public const double SolverRateHz = 60;

            /// <summary>Spec 2.8: Rayleigh damping hits DampingRatio at these two frequencies, Hz. GUESS.</summary>
            public const double RayleighLowHz = 1;

            /// <summary>Spec 2.8: see RayleighLowHz. GUESS.</summary>
            public const double RayleighHighHz = 20;

            /// <summary>Spec 2.7: members removed per step at most, so a collapse unfolds over frames. GUESS.</summary>
            public const int MaxFailuresPerStep = 3;
        }
    }
}
