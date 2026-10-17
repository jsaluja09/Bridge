namespace BridgeBuilder.Game
{
    /// <summary>Core uses System.Numerics so it stays free of Unity; these convert at the boundary.</summary>
    static class VectorConversions
    {
        public static UnityEngine.Vector3 ToUnity(this System.Numerics.Vector3 v) => new UnityEngine.Vector3(v.X, v.Y, v.Z);
        public static System.Numerics.Vector3 ToNumerics(this UnityEngine.Vector3 v) => new System.Numerics.Vector3(v.x, v.y, v.z);
    }
}
