using System;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// The solver-side room-to-bridge adapter (spec section 2.6). The recognizer, snapping, and
    /// rendering stay in room units; the solver runs in bridge-world units, which are room units times
    /// a positive scale factor. Only node coordinates and node translations change: coordinates are
    /// multiplied on the way in and translations are divided on the way back. Rotations, section
    /// properties, forces, and car parameters pass through unchanged, since the spec does not say
    /// whether cross-sections scale. Every value returned is finite; an input that is not finite, or
    /// that would overflow when scaled, is rejected.
    /// </summary>
    public static class RoomBridgeAdapter
    {
        /// <summary>A room position in bridge-world units at <see cref="Constants.Scale.RoomToBridge"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The scaled position is not finite.</exception>
        public static Vec3 ToBridgePosition(Vec3 roomPosition)
        {
            return ToBridgePosition(roomPosition, Constants.Scale.RoomToBridge);
        }

        /// <summary>A room position multiplied by <paramref name="scale"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="scale"/> is not positive and finite, or the scaled position is not finite.
        /// </exception>
        public static Vec3 ToBridgePosition(Vec3 roomPosition, double scale)
        {
            RequireScale(scale, nameof(scale));
            return Multiply(roomPosition, scale, nameof(roomPosition));
        }

        /// <summary>A room distance or coordinate in bridge-world units at <see cref="Constants.Scale.RoomToBridge"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The scaled value is not finite.</exception>
        public static double ToBridgeLength(double roomLength)
        {
            return ToBridgeLength(roomLength, Constants.Scale.RoomToBridge);
        }

        /// <summary>A room distance or coordinate multiplied by <paramref name="scale"/>. The sign is kept.</summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="scale"/> is not positive and finite, or the scaled value is not finite.
        /// </exception>
        public static double ToBridgeLength(double roomLength, double scale)
        {
            RequireScale(scale, nameof(scale));
            return RequireFinite(roomLength * scale, roomLength, nameof(roomLength));
        }

        /// <summary>A bridge-world translation in room units at <see cref="Constants.Scale.RoomToBridge"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The scaled translation is not finite.</exception>
        public static Vec3 ToRoomDisplacement(Vec3 bridgeDisplacement)
        {
            return ToRoomDisplacement(bridgeDisplacement, Constants.Scale.RoomToBridge);
        }

        /// <summary>A bridge-world translation divided by <paramref name="scale"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="scale"/> is not positive and finite, or the scaled translation is not finite.
        /// </exception>
        public static Vec3 ToRoomDisplacement(Vec3 bridgeDisplacement, double scale)
        {
            RequireScale(scale, nameof(scale));
            return Divide(bridgeDisplacement, scale, nameof(bridgeDisplacement));
        }

        /// <summary>A bridge-world distance, such as one displacement component, in room units at <see cref="Constants.Scale.RoomToBridge"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The scaled value is not finite.</exception>
        public static double ToRoomLength(double bridgeLength)
        {
            return ToRoomLength(bridgeLength, Constants.Scale.RoomToBridge);
        }

        /// <summary>A bridge-world distance divided by <paramref name="scale"/>. The sign is kept.</summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="scale"/> is not positive and finite, or the scaled value is not finite.
        /// </exception>
        public static double ToRoomLength(double bridgeLength, double scale)
        {
            RequireScale(scale, nameof(scale));
            return RequireFinite(bridgeLength / scale, bridgeLength, nameof(bridgeLength));
        }

        /// <summary>
        /// A bridge-world copy of a room-unit solver input, scaled by the input's own
        /// <see cref="SolverInput.ScaleFactor"/>. The copy has new <see cref="Node"/> objects with
        /// multiplied positions and every other node field copied, in the same order. Members, fills,
        /// and sections are the same array instances, unchanged. The copy's
        /// <see cref="SolverInput.ScaleFactor"/> is 1 because its positions are already in bridge-world
        /// units, so converting it again changes nothing. Convert results back with the room input's
        /// scale factor. The room input is not modified.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="roomInput"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The input's scale factor is not positive and finite, or a scaled node position is not finite.
        /// </exception>
        /// <exception cref="ArgumentException">The node array is null or holds a null entry.</exception>
        public static SolverInput ToBridge(SolverInput roomInput)
        {
            if (roomInput == null)
            {
                throw new ArgumentNullException(nameof(roomInput));
            }

            double scale = roomInput.ScaleFactor;
            RequireScale(scale, nameof(roomInput));

            Node[] roomNodes = roomInput.Nodes ?? throw new ArgumentException("The input has no node array.", nameof(roomInput));
            var bridgeNodes = new Node[roomNodes.Length];
            for (int i = 0; i < roomNodes.Length; i++)
            {
                Node node = roomNodes[i] ?? throw new ArgumentException("The node array holds a null entry.", nameof(roomInput));
                bridgeNodes[i] = new Node
                {
                    Id = node.Id,
                    Position = Multiply(node.Position, scale, nameof(roomInput)),
                    Fixed = node.Fixed,
                    SurfaceId = node.SurfaceId,
                    GroundAnchor = node.GroundAnchor,
                };
            }

            return new SolverInput
            {
                Nodes = bridgeNodes,
                Members = roomInput.Members,
                Fills = roomInput.Fills,
                Sections = roomInput.Sections,
                ScaleFactor = 1.0,
            };
        }

        /// <summary>A bridge-world static result in room units at <see cref="Constants.Scale.RoomToBridge"/>.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="bridgeResult"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A scaled translation is not finite.</exception>
        /// <exception cref="ArgumentException">The displacement array is null.</exception>
        public static StaticSolveResult ToRoom(StaticSolveResult bridgeResult)
        {
            return ToRoom(bridgeResult, Constants.Scale.RoomToBridge);
        }

        /// <summary>
        /// A copy of a bridge-world static result with every translation divided by
        /// <paramref name="scale"/>, in the same node order. Rotations and member forces are the same
        /// array instances, unchanged. Pass the scale factor of the room input that was converted.
        /// The bridge result is not modified.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="bridgeResult"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="scale"/> is not positive and finite, or a scaled translation is not finite.
        /// </exception>
        /// <exception cref="ArgumentException">The displacement array is null.</exception>
        public static StaticSolveResult ToRoom(StaticSolveResult bridgeResult, double scale)
        {
            if (bridgeResult == null)
            {
                throw new ArgumentNullException(nameof(bridgeResult));
            }

            RequireScale(scale, nameof(scale));
            return new StaticSolveResult
            {
                Displacements = DivideAll(bridgeResult.Displacements, scale, nameof(bridgeResult)),
                Rotations = bridgeResult.Rotations,
                MemberForces = bridgeResult.MemberForces,
            };
        }

        /// <summary>A bridge-world dynamic step in room units at <see cref="Constants.Scale.RoomToBridge"/>.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="bridgeStep"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A scaled translation is not finite.</exception>
        /// <exception cref="ArgumentException">The displacement array is null.</exception>
        public static StepOutput ToRoom(StepOutput bridgeStep)
        {
            return ToRoom(bridgeStep, Constants.Scale.RoomToBridge);
        }

        /// <summary>
        /// A copy of a dynamic step whose translations are in bridge-world units, with every
        /// translation divided by <paramref name="scale"/>, in the same node order. Rotations, member
        /// forces, and failed member ids are the same array instances, unchanged. The car state is the
        /// same object and stays in bridge-world units, as <see cref="CarState"/> defines. Pass the scale
        /// factor of the room input that was converted. The bridge step is not modified.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="bridgeStep"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="scale"/> is not positive and finite, or a scaled translation is not finite.
        /// </exception>
        /// <exception cref="ArgumentException">The displacement array is null.</exception>
        public static StepOutput ToRoom(StepOutput bridgeStep, double scale)
        {
            if (bridgeStep == null)
            {
                throw new ArgumentNullException(nameof(bridgeStep));
            }

            RequireScale(scale, nameof(scale));
            return new StepOutput
            {
                Displacements = DivideAll(bridgeStep.Displacements, scale, nameof(bridgeStep)),
                Rotations = bridgeStep.Rotations,
                MemberForces = bridgeStep.MemberForces,
                FailedMemberIds = bridgeStep.FailedMemberIds,
                CarState = bridgeStep.CarState,
            };
        }

        private static void RequireScale(double scale, string paramName)
        {
            if (!(scale > 0.0) || double.IsPositiveInfinity(scale))
            {
                throw new ArgumentOutOfRangeException(paramName, scale, "The room-to-bridge scale factor must be positive and finite.");
            }
        }

        private static Vec3[] DivideAll(Vec3[] bridgeDisplacements, double scale, string paramName)
        {
            if (bridgeDisplacements == null)
            {
                throw new ArgumentException("The result has no displacement array.", paramName);
            }

            var roomDisplacements = new Vec3[bridgeDisplacements.Length];
            for (int i = 0; i < bridgeDisplacements.Length; i++)
            {
                roomDisplacements[i] = Divide(bridgeDisplacements[i], scale, paramName);
            }

            return roomDisplacements;
        }

        // The scale is positive and finite here, so neither product nor quotient can be NaN.
        private static Vec3 Multiply(Vec3 value, double scale, string paramName)
        {
            return RequireFinite(value * scale, value, paramName);
        }

        private static Vec3 Divide(Vec3 value, double scale, string paramName)
        {
            return RequireFinite(value / scale, value, paramName);
        }

        private static Vec3 RequireFinite(Vec3 scaled, Vec3 original, string paramName)
        {
            if (!double.IsFinite(scaled.X) || !double.IsFinite(scaled.Y) || !double.IsFinite(scaled.Z))
            {
                throw new ArgumentOutOfRangeException(paramName, original, "The value does not scale to a finite result.");
            }

            return scaled;
        }

        private static double RequireFinite(double scaled, double original, string paramName)
        {
            if (!double.IsFinite(scaled))
            {
                throw new ArgumentOutOfRangeException(paramName, original, "The value does not scale to a finite result.");
            }

            return scaled;
        }
    }
}
