using BridgeBuilder.Core;

namespace BridgeBuilder.Game
{
    /// <summary>Core uses its own double-precision Vec3 so it stays free of Unity; these convert at the boundary.</summary>
    static class VectorConversions
    {
        public static UnityEngine.Vector3 ToUnity(this Vec3 v) => new UnityEngine.Vector3((float)v.X, (float)v.Y, (float)v.Z);
        public static Vec3 ToCore(this UnityEngine.Vector3 v) => new Vec3(v.x, v.y, v.z);
    }
}
