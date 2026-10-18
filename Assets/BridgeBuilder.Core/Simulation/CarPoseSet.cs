using System;

namespace BridgeBuilder.Core
{
    /// <summary>Where to draw every car slot this frame (room units). Copied between threads; reuses its poses.</summary>
    public sealed class CarPoses
    {
        public CarPose[] Poses { get; private set; } = Array.Empty<CarPose>();
        public bool[] Visible { get; private set; } = Array.Empty<bool>();

        public int Count => Poses.Length;

        public void Resize(int count)
        {
            if (Poses.Length == count)
                return;
            Poses = new CarPose[count];
            for (int i = 0; i < count; i++)
                Poses[i] = new CarPose();
            Visible = new bool[count];
        }

        public void CopyTo(CarPoses other)
        {
            other.Resize(Count);
            for (int i = 0; i < Count; i++)
            {
                Poses[i].CopyTo(other.Poses[i]);
                other.Visible[i] = Visible[i];
            }
        }
    }

    /// <summary>
    /// One CarPoseTracker per car slot of a CoupledStep (W4-08 poses for traffic). A slot that gets a new car
    /// starts a fresh tracker; a car that fell stays visible where it landed until its slot is reused.
    /// </summary>
    public sealed class CarPoseSet
    {
        readonly CoupledStep _step;
        readonly CarParameters _car;
        readonly double _scale, _floor;
        readonly CarPoseTracker[] _trackers;
        readonly int[] _serials;

        public CarPoseSet(CoupledStep step, CarParameters car, double scale, double floorRoomY)
        {
            _step = step;
            _car = car;
            _scale = scale;
            _floor = floorRoomY;
            _trackers = new CarPoseTracker[step.CarCapacity];
            _serials = new int[step.CarCapacity];
        }

        /// <summary>Advances every car's pose by one solver step and writes them into poses.</summary>
        public void Update(RunResult result, double dt, CarPoses poses)
        {
            poses.Resize(_trackers.Length);
            for (int s = 0; s < _trackers.Length; s++)
            {
                var car = _step.CarAt(s);
                if (car != null && (_trackers[s] == null || _serials[s] != _step.CarSerial(s)))
                {
                    _trackers[s] = new CarPoseTracker(_car, _scale, _floor);
                    _serials[s] = _step.CarSerial(s);
                }
                var tracker = _trackers[s];
                if (car != null)
                    tracker.Update(car, result, dt);
                poses.Visible[s] = tracker != null && (car != null || tracker.Pose.Landed);
                if (poses.Visible[s])
                    tracker.Pose.CopyTo(poses.Poses[s]);
            }
        }
    }
}
