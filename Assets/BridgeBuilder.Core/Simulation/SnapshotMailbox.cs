using System;
using System.Globalization;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Single-slot mailbox between the solver worker and the render thread (spec 2.12): the writer
    /// publishes, and the reader only ever sees the latest snapshot. <see cref="Publish"/> stores a
    /// copy, so a publisher may reuse its buffers and a reader never sees fields from two different
    /// publishes. <see cref="TryRead"/> hands every reader the same stored copy until the next publish,
    /// so readers must not modify it. Safe to call from any thread.
    /// </summary>
    public sealed class SnapshotMailbox : ISnapshotStore
    {
        private readonly object gate = new object();
        private SimulationSnapshot latest;

        /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// An array is null, <c>Displacements</c> and <c>Rotations</c> differ in length, <c>MemberIds</c> and
        /// <c>Utilizations</c> differ in length, or a value is not finite.
        /// </exception>
        public void Publish(SimulationSnapshot snapshot)
        {
            SimulationSnapshot copy = Copy(snapshot);
            lock (gate)
            {
                latest = copy;
            }
        }

        public bool TryRead(out SimulationSnapshot snapshot)
        {
            lock (gate)
            {
                snapshot = latest;
            }

            return snapshot != null;
        }

        private static SimulationSnapshot Copy(SimulationSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            Vec3[] displacements = snapshot.Displacements ?? throw new ArgumentException("The snapshot has no displacement array.", nameof(snapshot));
            Vec3[] rotations = snapshot.Rotations ?? throw new ArgumentException("The snapshot has no rotation array.", nameof(snapshot));
            int[] memberIds = snapshot.MemberIds ?? throw new ArgumentException("The snapshot has no member id array.", nameof(snapshot));
            double[] utilizations = snapshot.Utilizations ?? throw new ArgumentException("The snapshot has no utilization array.", nameof(snapshot));
            if (displacements.Length != rotations.Length)
            {
                throw new ArgumentException("The snapshot needs one displacement and one rotation per node.", nameof(snapshot));
            }

            if (memberIds.Length != utilizations.Length)
            {
                throw new ArgumentException("The snapshot needs one utilization per member id.", nameof(snapshot));
            }

            for (int i = 0; i < displacements.Length; i++)
            {
                if (!IsFinite(displacements[i]) || !IsFinite(rotations[i]))
                {
                    throw new ArgumentException("Node entry " + Format(i) + " has a displacement or rotation that is not finite.", nameof(snapshot));
                }
            }

            for (int k = 0; k < utilizations.Length; k++)
            {
                if (!double.IsFinite(utilizations[k]))
                {
                    throw new ArgumentException("Member " + Format(memberIds[k]) + " has a utilization that is not finite.", nameof(snapshot));
                }
            }

            return new SimulationSnapshot
            {
                StepId = snapshot.StepId,
                Displacements = (Vec3[])displacements.Clone(),
                Rotations = (Vec3[])rotations.Clone(),
                MemberIds = (int[])memberIds.Clone(),
                Utilizations = (double[])utilizations.Clone(),
            };
        }

        private static bool IsFinite(Vec3 v)
        {
            return double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
        }

        private static string Format(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
