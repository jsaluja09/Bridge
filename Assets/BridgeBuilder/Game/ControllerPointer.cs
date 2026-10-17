using UnityEngine;
using UnityEngine.InputSystem;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// Right controller tip position and the game's buttons, read straight from the Input System.
    /// Trigger = mark / draw, A = test / back to building, B = undo, left X = clear / re-mark surfaces.
    /// Keyboard fallbacks (Space, Backspace, R) help when testing over Link.
    /// </summary>
    public sealed class ControllerPointer : MonoBehaviour
    {
        [Tooltip("Optional. If set, the tip follows this transform instead of the controller pose.")]
        [SerializeField] Transform tipOverride;

        [Tooltip("Tip offset in the controller's grip space. Adjust until the cursor sits at the controller's front.")]
        [SerializeField] Vector3 tipOffset = new Vector3(0f, -0.01f, 0.05f);

        [Tooltip("The XR Origin's Camera Offset. Defaults to the main camera's parent.")]
        [SerializeField] Transform trackingSpace;

        InputAction _position, _rotation, _trigger, _primary, _secondary, _reset;

        public Vector3 Tip { get; private set; }
        public bool TriggerPressed => _trigger.WasPressedThisFrame();
        public bool TriggerHeld => _trigger.IsPressed();
        public bool TriggerReleased => _trigger.WasReleasedThisFrame();
        public bool PrimaryPressed => _primary.WasPressedThisFrame();
        public bool SecondaryPressed => _secondary.WasPressedThisFrame();
        public bool ResetPressed => _reset.WasPressedThisFrame();

        void Awake()
        {
            _position = new InputAction("Tip Position", InputActionType.Value, "<XRController>{RightHand}/devicePosition", expectedControlType: "Vector3");
            _rotation = new InputAction("Tip Rotation", InputActionType.Value, "<XRController>{RightHand}/deviceRotation", expectedControlType: "Quaternion");

            _trigger = new InputAction("Draw", InputActionType.Button);
            _trigger.AddBinding("<XRController>{RightHand}/{TriggerButton}");
            _trigger.AddBinding("<XRController>{RightHand}/triggerPressed");

            _primary = new InputAction("Test", InputActionType.Button);
            _primary.AddBinding("<XRController>{RightHand}/primaryButton");
            _primary.AddBinding("<Keyboard>/space");

            _secondary = new InputAction("Undo", InputActionType.Button);
            _secondary.AddBinding("<XRController>{RightHand}/secondaryButton");
            _secondary.AddBinding("<Keyboard>/backspace");

            _reset = new InputAction("Reset", InputActionType.Button);
            _reset.AddBinding("<XRController>{LeftHand}/primaryButton");
            _reset.AddBinding("<Keyboard>/r");
        }

        void OnEnable()
        {
            foreach (var a in Actions())
                a.Enable();
        }

        void OnDisable()
        {
            foreach (var a in Actions())
                a.Disable();
        }

        InputAction[] Actions() => new[] { _position, _rotation, _trigger, _primary, _secondary, _reset };

        void Update()
        {
            if (tipOverride != null)
            {
                Tip = tipOverride.TransformPoint(tipOffset);
                return;
            }

            if (trackingSpace == null && Camera.main != null)
                trackingSpace = Camera.main.transform.parent;

            Vector3 local = _position.ReadValue<Vector3>() + _rotation.ReadValue<Quaternion>() * tipOffset;
            Tip = trackingSpace != null ? trackingSpace.TransformPoint(local) : local;
        }
    }
}
