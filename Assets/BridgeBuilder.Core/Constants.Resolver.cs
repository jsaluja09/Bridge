namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>The shape resolver (W4-01, spec 11.5). Room meters; residuals are RMS distance over stroke length.</summary>
        public static class Resolver
        {
            /// <summary>Best normalized residual above this and the stroke fits no shape. GUESS.</summary>
            public const double ResolverRejectTolerance = 0.02;
            /// <summary>Score added per step of complexity (line 0, arc 1, polyline = its segment count), so near ties go to the simpler shape. GUESS.</summary>
            public const double ResolverSimplicityBias = 0.004;
            /// <summary>Shorter strokes are TooShort. GUESS.</summary>
            public const double ResolverMinStrokeLength = 0.04;
            /// <summary>Most corners a polyline may have between its ends (a W has 3). GUESS.</summary>
            public const int ResolverMaxPolylineCorners = 6;
            /// <summary>A curved or cornered stroke must lie within this of one plane, meters. GUESS.</summary>
            public const double ResolverPlanarityTolerance = 0.015;
            /// <summary>Each straight piece of a polyline must stay within this of the hand's path, meters. GUESS.</summary>
            public const double ResolverSegmentStraightness = 0.012;
            /// <summary>Spacing the stroke is resampled at before fitting, meters. GUESS.</summary>
            public const double ResolverSampleSpacing = 0.01;
            /// <summary>
            /// The smoothed stroke may be at most this many times longer than the shape it resolves to; more means it
            /// wandered or went back over itself (a scribble). GUESS.
            /// </summary>
            public const double ResolverMaxPathRatio = 1.3;
            /// <summary>Samples on each side in the smoothing average. GUESS.</summary>
            public const int ResolverSmoothingWindow = 2;
        }
    }
}
