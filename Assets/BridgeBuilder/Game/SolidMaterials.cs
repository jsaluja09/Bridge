using System.Collections.Generic;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// Shared flat-color materials for runtime primitives, one per color. Uses the BridgeBuilder/SolidColor shader
    /// from Resources so the headset build never draws them magenta.
    /// </summary>
    public static class SolidMaterials
    {
        static readonly Dictionary<Color, Material> Cache = new Dictionary<Color, Material>();
        static Shader _shader;

        public static Material Get(Color color)
        {
            if (Cache.TryGetValue(color, out var material) && material != null)
                return material;
            if (_shader == null)
                _shader = Shader.Find("BridgeBuilder/SolidColor") ?? Shader.Find("Universal Render Pipeline/Unlit");
            material = new Material(_shader) { color = color, enableInstancing = true };
            Cache[color] = material;
            return material;
        }
    }
}
