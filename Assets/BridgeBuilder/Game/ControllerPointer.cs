using UnityEngine;
using UnityEngine.InputSystem;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// Right controller tip position and the game's buttons, read straight from the Input System.
    /// Right: trigger = use tool, stick left/right = change tool, stick click = symmetry, A = test, B = undo.
    /// Left: X = clear, Y = swap the car's start side. Keyboard fallbacks help when testing over Link.
    /// </summary>
    public sealed class ControllerPointer : MonoBehaviour
    {
        [Tooltip("Optional. If set, the tip follows this transform instead of the controller pose.")]
        [SerializeField] Transform tipOverride;

        [Tooltip("Tip offset in the controller's grip space. Adjust until the cursor sits at the controller's front.")]
        [SerializeField] Vector3 tipOffset = new Vector3(0f, -0.01f, 0.05f);

        [Tooltip("The XR Origin's Camera Offset. Defaults to the main camera's parent.")]
        [SerializeField] Transform trackingSpace;

        InputAction _position, _rotation, _trigger, _primary, _secondary, _clear, _swap, _stick, _stickClick, _next, _previous;
        bool _stickCentered = true;

        public Vector3 Tip { get; private set; }
        public Vector3 TipUp { get; private set; } = Vector3.up;
        public bool TriggerPressed => _trigger.WasPressedThisFrame();
        public bool TriggerHeld => _trigger.IsPressed();
        public bool TriggerReleased => _trigger.WasReleasedThisFrame();
        public bool PrimaryPressed => _primary.WasPressedThisFrame();
        public bool SecondaryPressed => _secondary.WasPressedThisFrame();
        public bool ClearPressed => _clear.WasPressedThisFrame();
        public bool SwapPressed => _swap.WasPressedThisFrame();
        public bool SymmetryPressed => _stickClick.WasPressedThisFrame();
        public int ToolStep { get; private set; }     // +1 / -1 on a stick flick or arrow key this frame

        /// <summary>The floor's height: the XR Origin sits on the floor in floor tracking mode.</summary>
        public float FloorY
        {
            get
            {
                var space = TrackingSpace;
                return space != null && space.parent != null ? space.parent.position.y : 0f;
            }
        }

        Transform TrackingSpace
        {
            get
            {
                if (trackingSpace == null && Camera.main != null)
                    trackingSpace = Camera.main.transform.parent;
                return trackingSpace;
            }
        }

        void Awake()
        {
            _position = new InputAction("Tip Position", InputActionType.Value, "<XRController>{RightHand}/devicePosition", expectedControlType: "Vector3");
            _rotation = new InputAction("Tip Rotation", InputActionType.Value, "<XRController>{RightHand}/deviceRotation", expectedControlType: "Quaternion");
            _stick = new InputAction("Tool Stick", InputActionType.Value, "<XRController>{RightHand}/{Primary2DAxis}", expectedControlType: "Vector2");

            _trigger = Button("Use Tool", "<XRController>{RightHand}/{TriggerButton}", "<XRController>{RightHand}/triggerPressed");
            _primary = Button("Test", "<XRController>{RightHand}/primaryButton", "<Keyboard>/space");
            _secondary = Button("Undo", "<XRController>{RightHand}/secondaryButton", "<Keyboard>/backspace");
            _clear = Button("Clear", "<XRController>{LeftHand}/primaryButton", "<Keyboard>/r");
            _swap = Button("Swap Start", "<XRController>{LeftHand}/secondaryButton", "<Keyboard>/y");
            _stickClick = Button("Symmetry", "<XRController>{RightHand}/{Primary2DAxisClick}", "<Keyboard>/m");
            _next = Button("Next Tool", "<Keyboard>/rightArrow");
            _previous = Button("Previous Tool", "<Keyboard>/leftArrow");
        }

        static InputAction Button(string name, params string[] bindings)
        {
            var action = new InputAction(name, InputActionType.Button);
            foreach (var b in bindings)
                action.AddBinding(b);
            return action;
        }

        InputAction[] Actions() => new[] { _position, _rotation, _trigger, _primary, _secondary, _clear, _swap, _stick, _stickClick, _next, _previous };

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

        void Update()
        {
            // Stick flicks: one step per push past the threshold, then wait for it to recenter.
            ToolStep = _next.WasPressedThisFrame() ? 1 : _previous.WasPressedThisFrame() ? -1 : 0;
            float x = _stick.ReadValue<Vector2>().x;
            if (_stickCentered && Mathf.Abs(x) > 0.7f)
            {
                ToolStep = x > 0f ? 1 : -1;
                _stickCentered = false;
            }
            else if (Mathf.Abs(x) < 0.3f)
            {
                _stickCentered = true;
            }

            if (tipOverride != null)
            {
                Tip = tipOverride.TransformPoint(tipOffset);
                TipUp = tipOverride.up;
                return;
            }

            Quaternion rotation = _rotation.ReadValue<Quaternion>();
            if (rotation == new Quaternion(0f, 0f, 0f, 0f))
                rotation = Quaternion.identity;
            Vector3 local = _position.ReadValue<Vector3>() + rotation * tipOffset;
            var space = TrackingSpace;
            Tip = space != null ? space.TransformPoint(local) : local;
            TipUp = space != null ? space.rotation * (rotation * Vector3.up) : rotation * Vector3.up;
        }
    }
}
