namespace BridgeBuilder.Core
{
    /// <summary>
    /// Latest-value mailbox between the simulation and the render thread (spec 2.12): the reader always sees
    /// one whole snapshot, never a mix of two generations. The Unity host publishes from its worker thread.
    /// </summary>
    public sealed class Mailbox<T> where T : class
    {
        readonly object _lock = new object();
        T _latest;
        bool _fresh;

        public void Publish(T value)
        {
            lock (_lock)
            {
                _latest = value;
                _fresh = true;
            }
        }

        /// <summary>The latest value; fresh is true if it arrived since the last read.</summary>
        public bool TryRead(out T value, out bool fresh)
        {
            lock (_lock)
            {
                value = _latest;
                fresh = _fresh;
                _fresh = false;
                return value != null;
            }
        }
    }

    public sealed class SnapshotMailbox : ISnapshotStore
    {
        readonly Mailbox<SimulationSnapshot> _box = new Mailbox<SimulationSnapshot>();

        public void Publish(SimulationSnapshot snapshot) => _box.Publish(snapshot);

        public bool TryRead(out SimulationSnapshot snapshot) => _box.TryRead(out snapshot, out _);
    }
}
