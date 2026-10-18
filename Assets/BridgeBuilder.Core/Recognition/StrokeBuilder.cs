using System;
using System.Collections.Generic;
using System.Linq;

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
    /// What a finished stroke becomes, before anything is added: FreeDraw's chains, or, when it was drawn beside a
    /// matching line at road spacing, the exact shifted copy of that line (W4-02). Empty if the stroke was rejected.
    /// </summary>
    public sealed class ShapedStroke
    {
        public List<List<DrawVertex>> Chains = new List<List<DrawVertex>>();
        public ParallelResult Copy;
        public RejectionReason Reason;
        /// <summary>FreeDraw couldn't read the stroke, so it became a straight line between its ends.</summary>
        public bool Straightened;

        public bool IsEmpty => Chains.Count == 0 && Copy == null;

        /// <summary>The copy as pieces: one per side of a polyline so every corner lands exactly, else one.</summary>
        public List<List<Vec3>> CopyPieces()
        {
            var shape = Copy.Shape;
            if (shape.Kind == ResolvedKind.Polyline)
                return Enumerable.Range(0, shape.Points.Count - 1).Select(i => new List<Vec3> { shape.Points[i], shape.Points[i + 1] }).ToList();
            return new List<List<Vec3>> { shape.Path(0.01) };
        }

        /// <summary>The geometry it builds, one path per chain or copy piece (for the ghost and the mirror).</summary>
        public List<List<Vec3>> Paths() =>
            Copy != null ? CopyPieces() : Chains.Select(c => c.Select(v => v.Position).ToList()).ToList();
    }

    /// <summary>
    /// Turns a finished stroke into lines (spec 11.1, 11.5, 11.6). Shape reads it with FreeDraw; a stroke FreeDraw
    /// can't read (a squiggle) becomes a straight line between its ends rather than being thrown away. Build adds
    /// it, then, with a mirror plane, its mirror copy across the road, as one undo step that goes in whole or not
    /// at all.
    /// </summary>
    public static class StrokeBuilder
    {
        public sealed class Context
        {
            public IReadOnlyList<Pad> Pads = Array.Empty<Pad>();
            public IReadOnlyList<Vec3> GroundDots = Array.Empty<Vec3>();
            /// <summary>Copy what is built across this plane; null builds the stroke alone.</summary>
            public MirrorPlane? Mirror;
        }

        public static ShapedStroke Shape(IReadOnlyList<Vec3> stroke, BridgeModel model, Context ctx)
        {
            var shaped = new ShapedStroke();
            var draw = DrawContext(model, ctx);
            shaped.Chains = FreeDraw.Build(stroke, draw, out var kind, out shaped.Reason);
            if (shaped.Chains.Count == 0 && Straightenable(shaped.Reason) && stroke.Count >= 2)
            {
                shaped.Chains = FreeDraw.Build(new[] { stroke[0], stroke[stroke.Count - 1] }, draw, out kind, out var reason);
                shaped.Straightened = shaped.Chains.Count > 0;
                if (!shaped.Straightened)
                    shaped.Reason = reason;
            }

            if (shaped.Chains.Count == 1 && (kind == StrokeKind.Line || kind == StrokeKind.Arc || kind == StrokeKind.Polyline))
            {
                var snapped = ParallelAssist.Snap(ShapeResolver.Resolve(shaped.Chains[0].Select(v => v.Position).ToList()), model);
                if (snapped.Snapped)
                    shaped.Copy = snapped;
            }
            return shaped;
        }

        /// <summary>Rejections that come from a messy hand rather than a stroke too short to be anything.</summary>
        static bool Straightenable(RejectionReason reason) =>
            reason == RejectionReason.Unrecognized || reason == RejectionReason.NonPlanar;

        public static StrokeResult Build(BridgeModel model, ShapedStroke shaped, Context ctx)
        {
            if (shaped.IsEmpty)
                return new StrokeResult(0, EditReason.InvalidLine);

            int added = 0;
            model.BeginGroup();
            try
            {
                EditReason failure;
                if (shaped.Copy == null)
                    failure = Add(model, shaped.Chains, ref added);
                else
                    failure = AddPieces(model, shaped.CopyPieces(), ctx, ref added);

                if (failure == EditReason.None && ctx.Mirror.HasValue)
                {
                    var mirrored = shaped.Paths().Select(p => MirrorAssist.Mirror(p, ctx.Mirror.Value)).Where(p => p != null).ToList();
                    failure = AddPieces(model, mirrored, ctx, ref added);
                }

                if (failure == EditReason.None && added == 0)
                    failure = EditReason.InvalidLine;
                if (failure != EditReason.None)
                {
                    if (added > 0)
                        model.Undo();          // the group saved one snapshot before its first line
                    return new StrokeResult(0, failure);
                }
            }
            finally
            {
                model.EndGroup();
            }
            return new StrokeResult(added, EditReason.None);
        }

        /// <summary>Each piece through FreeDraw against the model as it is now, so later pieces join earlier ones.</summary>
        static EditReason AddPieces(BridgeModel model, IEnumerable<List<Vec3>> pieces, Context ctx, ref int added)
        {
            foreach (var piece in pieces)
            {
                var failure = Add(model, FreeDraw.Build(piece, DrawContext(model, ctx), out _), ref added);
                if (failure != EditReason.None)
                    return failure;
            }
            return EditReason.None;
        }

        static EditReason Add(BridgeModel model, List<List<DrawVertex>> chains, ref int added)
        {
            foreach (var chain in chains)
            {
                var result = model.TryAddFreeLine(Vertices(model, chain));
                if (!result.Ok)
                    return result.Reason;
                added++;
            }
            return EditReason.None;
        }

        static List<LineVertex> Vertices(BridgeModel model, List<DrawVertex> chain)
        {
            var vertices = new List<LineVertex>(chain.Count);
            foreach (var v in chain)
            {
                if (v.IsExisting)
                    vertices.Add(LineVertex.Existing(v.ExistingId));
                else if (v.IsOnMember)
                    vertices.Add(LineVertex.OnMember(v.MemberId, v.Position));
                else if (v.NewSupport)
                    vertices.Add(LineVertex.Existing(model.AddFixedNode(v.Position, v.SurfaceId, v.Ground)));
                else
                    vertices.Add(LineVertex.New(v.Position));
            }
            return vertices;
        }

        /// <summary>FreeDraw's view of the model now, with a one-pass "already joined?" lookup (FreeDraw asks per segment).</summary>
        public static FreeDraw.Context DrawContext(BridgeModel model, Context ctx)
        {
            var joined = new HashSet<(int, int)>();
            foreach (var m in model.Members)
                joined.Add(m.NodeA < m.NodeB ? (m.NodeA, m.NodeB) : (m.NodeB, m.NodeA));
            return new FreeDraw.Context
            {
                Nodes = model.Nodes,
                Members = model.Members,
                Joined = (a, b) => joined.Contains(a < b ? (a, b) : (b, a)),
                Pads = ctx.Pads,
                GroundDots = ctx.GroundDots,
            };
        }
    }
}
