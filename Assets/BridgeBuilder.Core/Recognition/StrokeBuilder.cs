using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>What adding one stroke did: how many lines went in, or why it was refused.</summary>
    public readonly struct StrokeResult
    {
        public readonly int Lines;
        public readonly EditReason Reason;

        public StrokeResult(int lines, EditReason reason)
        {
            Lines = lines;
            Reason = reason;
        }

        public bool Ok => Lines > 0;
    }

    /// <summary>
    /// Adds a finished stroke to the model (spec 11.1): each resolved piece goes through FreeDraw, then, when a
    /// mirror plane is given, the mirror copy of each piece. The whole stroke is one undo step and goes in whole
    /// or not at all, so a mirror pair never ends up half built.
    /// </summary>
    public static class StrokeBuilder
    {
        public sealed class Context
        {
            public IReadOnlyList<Pad> Pads = Array.Empty<Pad>();
            public IReadOnlyList<Vec3> GroundDots = Array.Empty<Vec3>();
            /// <summary>Copy every piece across this plane; null builds the stroke alone.</summary>
            public MirrorPlane? Mirror;
            /// <summary>The material (section id) of every line the stroke and its mirror copy make.</summary>
            public int SectionId;
        }

        public static StrokeResult Build(BridgeModel model, IReadOnlyList<IReadOnlyList<Vec3>> pieces, Context ctx)
        {
            var all = new List<IReadOnlyList<Vec3>>(pieces);
            if (ctx.Mirror.HasValue)
            {
                foreach (var piece in pieces)
                {
                    var copy = MirrorAssist.Mirror(piece, ctx.Mirror.Value);
                    if (copy != null)
                        all.Add(copy);
                }
            }

            int added = 0;
            model.BeginGroup();
            try
            {
                foreach (var piece in all)
                {
                    var draw = new FreeDraw.Context { Nodes = model.Nodes, Joined = JoinedLookup(model), Pads = ctx.Pads, GroundDots = ctx.GroundDots };
                    foreach (var chain in FreeDraw.Build(piece, draw, out _))
                    {
                        var result = model.TryAddFreeLine(Vertices(model, chain), ctx.SectionId);
                        if (!result.Ok)
                        {
                            if (added > 0)
                                model.Undo();          // the group saved one snapshot before its first line
                            return new StrokeResult(0, result.Reason);
                        }
                        added++;
                    }
                }
            }
            finally
            {
                model.EndGroup();
            }
            return new StrokeResult(added, EditReason.None);
        }

        static List<LineVertex> Vertices(BridgeModel model, List<DrawVertex> chain)
        {
            var vertices = new List<LineVertex>(chain.Count);
            foreach (var v in chain)
            {
                if (v.IsExisting)
                    vertices.Add(LineVertex.Existing(v.ExistingId));
                else if (v.NewSupport)
                    vertices.Add(LineVertex.Existing(model.AddFixedNode(v.Position, v.SurfaceId, v.Ground)));
                else
                    vertices.Add(LineVertex.New(v.Position));
            }
            return vertices;
        }

        /// <summary>"Are these two nodes already joined?" from one pass over the members (FreeDraw asks per segment).</summary>
        static Func<int, int, bool> JoinedLookup(BridgeModel model)
        {
            var joined = new HashSet<(int, int)>();
            foreach (var m in model.Members)
                joined.Add(m.NodeA < m.NodeB ? (m.NodeA, m.NodeB) : (m.NodeB, m.NodeA));
            return (a, b) => joined.Contains(a < b ? (a, b) : (b, a));
        }
    }
}
