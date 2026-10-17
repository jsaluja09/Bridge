using System.Numerics;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Room-space axes for the gap: Origin is the start edge's midpoint, U points horizontally toward the
    /// far edge, Side is horizontal and perpendicular to U, Up is gravity-up (spec 2.2).
    /// </summary>
    public readonly struct BridgeFrame
    {
        public readonly Vector3 Origin;
        public readonly Vector3 U;
        public readonly Vector3 Side;
        public readonly Vector3 Up;

        public BridgeFrame(Vector3 origin, Vector3 towardFar, Vector3 up)
        {
            Up = Vector3.Normalize(up);
            Vector3 u = towardFar - Vector3.Dot(towardFar, Up) * Up;
            U = u.LengthSquared() > 1e-10f ? Vector3.Normalize(u) : Geometry.AnyPerpendicular(Up);
            Side = Vector3.Cross(Up, U);
            Origin = origin;
        }

        /// <summary>Coordinates of p along (U, Side, Up) relative to Origin.</summary>
        public Vector3 Local(Vector3 p)
        {
            Vector3 d = p - Origin;
            return new Vector3(Vector3.Dot(d, U), Vector3.Dot(d, Side), Vector3.Dot(d, Up));
        }

        public Vector3 World(float u, float side, float up) => Origin + U * u + Side * side + Up * up;

        /// <summary>Reflects p across the vertical plane through the gap's centerline (the Symmetry tool).</summary>
        public Vector3 Mirror(Vector3 p) => p - 2f * Vector3.Dot(p - Origin, Side) * Side;
    }
}
