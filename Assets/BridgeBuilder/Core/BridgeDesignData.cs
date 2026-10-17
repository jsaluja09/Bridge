using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// A design as plain data with public fields, for JSON test bridges and saved designs (spec section 3).
    /// Positions are room units as [x, y, z]. Taps are the four surface-edge taps, 12 floats.
    /// </summary>
    [Serializable]
    public sealed class BridgeDesignData
    {
        public int FormatVersion = 1;
        public float[] Taps;
        public float GroundY;
        public int StartSurface;
        public int NextId;
        public List<NodeData> Nodes = new List<NodeData>();
        public List<LineData> Lines = new List<LineData>();
        public List<FillData> Fills = new List<FillData>();

        [Serializable]
        public sealed class NodeData
        {
            public int Id;
            public float[] Position;
            public NodeKind Kind;
            public int Surface = -1;
            public int OwnerLine = -1;
        }

        [Serializable]
        public sealed class LineData
        {
            public int Id;
            public StrokeShape Shape;
            public List<int> Nodes = new List<int>();
            public float Section = 1f;
        }

        [Serializable]
        public sealed class FillData
        {
            public int Id;
            public List<int> Corners = new List<int>();
            public List<int> Boundary = new List<int>();
            public List<int> Triangles = new List<int>();
            public List<int> Bracing = new List<int>();     // pairs, flattened
            public float[] Normal;
            public float Area;
            public bool IsRoad;
        }
    }
}
