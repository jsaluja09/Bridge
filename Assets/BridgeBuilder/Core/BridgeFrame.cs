using System.Numerics;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// The vertical drawing plane through the two surface taps.
    /// U runs horizontally from the start surface toward the far one, V is up,
    /// W points across the road (from the drawn side to its mirrored twin).
    /// </summary>
    public readonly struct BridgeFrame
    {
        public readonly Vector3 Origin;
        public readonly Vector3 U;
        public readonly Vector3 V;
        public readonly Vector3 W;
        public readonly float Width;

        public BridgeFrame(Vector3 origin, Vector3 u, Vector3 v, Vector3 w, float width)
        {
            Origin = origin;
            U = u;
            V = v;
            W = w;
            Width = width;
        }

        /// <summary>Builds a frame from two taps. viewer picks which way W points (away from the player).</summary>
        public static BridgeFrame FromTaps(Vector3 start, Vector3 far, Vector3 up, Vector3 viewer, float width)
        {
            up = Vector3.Normalize(up);
            Vector3 gap = far - start;
            Vector3 u = Vector3.Normalize(gap - Vector3.Dot(gap, up) * up);
            Vector3 w = Vector3.Cross(u, up);
            if (Vector3.Dot(w, start - viewer) < 0f)
                w = -w;
            return new BridgeFrame(start, u, up, w, width);
        }

        public Vector3 ToWorld(Vector2 p, int side = 0) => Origin + U * p.X + V * p.Y + W * (side * Width);

        public Vector2 ToPlane(Vector3 world)
        {
            Vector3 d = world - Origin;
            return new Vector2(Vector3.Dot(d, U), Vector3.Dot(d, V));
        }
    }
}
