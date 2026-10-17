using System;
using System.Collections.Generic;
using System.Numerics;

namespace BridgeBuilder.Core
{
    public struct DesignNode
    {
        public Vector2 Position;
        public bool Anchor;
    }

    public struct DesignMember
    {
        public int A;
        public int B;
    }

    public enum AddLineResult
    {
        Added,
        NoStartNode,
        TooShort,
        Duplicate,
        TooMany,
    }

    /// <summary>
    /// The side of the bridge the player draws, in plane coordinates (u along the gap, v up).
    /// The 3D bridge is built from this when a test starts.
    /// </summary>
    public sealed class BridgeDesign
    {
        public readonly List<DesignNode> Nodes = new List<DesignNode>();
        public readonly List<DesignMember> Members = new List<DesignMember>();

        public float FarU { get; private set; }
        public float FarV { get; private set; }

        readonly BridgeSettings _settings;
        readonly Stack<(int member, int createdNode)> _history = new Stack<(int, int)>();

        /// <summary>Anchors along both surfaces. The start tap is (0,0); the far tap is (farU, farV).</summary>
        public BridgeDesign(BridgeSettings settings, float farU, float farV)
        {
            _settings = settings;
            FarU = farU;
            FarV = farV;
            float s = settings.AnchorSpacing;
            AddAnchor(new Vector2(-s, 0f));
            AddAnchor(new Vector2(0f, 0f));
            AddAnchor(new Vector2(farU, farV));
            AddAnchor(new Vector2(farU + s, farV));
        }

        public int LineCount => _history.Count;

        void AddAnchor(Vector2 p) => Nodes.Add(new DesignNode { Position = p, Anchor = true });

        public int FindNode(Vector2 p, float radius)
        {
            int best = -1;
            float bestDist = radius;
            for (int i = 0; i < Nodes.Count; i++)
            {
                float d = Vector2.Distance(Nodes[i].Position, p);
                if (d <= bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>Where a line ending at p would land: an existing node, else the snap grid.</summary>
        public Vector2 SnapEnd(Vector2 p, out int node)
        {
            node = FindNode(p, _settings.NodeSnapRadius);
            if (node >= 0)
                return Nodes[node].Position;
            float g = _settings.SnapGrid;
            return new Vector2(MathF.Round(p.X / g) * g, MathF.Round(p.Y / g) * g);
        }

        public AddLineResult TryAddLine(int start, Vector2 end)
        {
            if (start < 0 || start >= Nodes.Count)
                return AddLineResult.NoStartNode;
            if (Members.Count >= _settings.MaxDrawnMembers)
                return AddLineResult.TooMany;

            Vector2 p = SnapEnd(end, out int endNode);
            if (endNode == start || Vector2.Distance(Nodes[start].Position, p) < _settings.MinLineLength)
                return AddLineResult.TooShort;

            if (endNode >= 0)
            {
                foreach (var m in Members)
                {
                    if ((m.A == start && m.B == endNode) || (m.A == endNode && m.B == start))
                        return AddLineResult.Duplicate;
                }
            }

            int created = -1;
            if (endNode < 0)
            {
                Nodes.Add(new DesignNode { Position = p });
                endNode = created = Nodes.Count - 1;
            }

            Members.Add(new DesignMember { A = start, B = endNode });
            _history.Push((Members.Count - 1, created));
            return AddLineResult.Added;
        }

        public bool Undo()
        {
            if (_history.Count == 0)
                return false;
            var (member, created) = _history.Pop();
            Members.RemoveAt(member);
            if (created >= 0)
                Nodes.RemoveAt(created);
            return true;
        }

        public void Clear()
        {
            while (Undo()) { }
        }

        public bool IsRoad(int member)
        {
            var m = Members[member];
            Vector2 d = Nodes[m.B].Position - Nodes[m.A].Position;
            if (MathF.Abs(d.X) < 1e-4f)
                return false;
            float slope = MathF.Atan2(MathF.Abs(d.Y), MathF.Abs(d.X)) * (180f / MathF.PI);
            return slope <= _settings.MaxRoadSlopeDeg;
        }
    }
}
