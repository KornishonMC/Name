using Cinemachine.Utility;
using UnityEngine;

namespace Cinemachine.Examples
{
    /// <summary>
    /// Implements continuous motion by wrapping the position around a range.
    /// </summary>
    public class WrapAround : MonoBehaviour
    {
        public enum AxisSelection { XAxis = 0, YAxis = 1, ZAxis = 2 };
        public AxisSelection Axis = AxisSelection.ZAxis;
        public float MinRange;
        public float MaxRange;

        private void OnValidate()
        {
            MaxRange = Mathf.Max(MinRange, MaxRange);
        }

        void LateUpdate()
        {
            // Wrap the axis around the range
            var pos = transform.position;
            var newPos = pos;
            if (newPos[(int)Axis] < MinRange)
                newPos[(int)Axis] = MaxRange;
            if (newPos[(int)Axis] > MaxRange)
                newPos[(int)Axis] = MinRange;

            var delta = newPos - pos;
            if (!delta.AlmostZero())
            {
                transform.position = newPos;
                if (TryGetComponent<Rigidbody>(out var rb))
                    rb.position = newPos;

                // Notify any CmCameras that are targeting this object
                CinemachineCore.Instance.OnTargetObjectWarped(transform, delta);
            }
        }
    }
}
