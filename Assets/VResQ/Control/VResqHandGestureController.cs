using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using VResQ.Core;
using VResQ.HMI;

namespace VResQ.Control
{
    public enum HandGesturePose
    {
        None = 0,
        OpenPalm = 1,
        Fist = 2,
        Pinch = 3,
        Peace = 4,
        ThumbsUp = 5,
        Point = 6
    }

    /// <summary>
    /// OpenXR Hand Tracking Gesture & Opt-In Hand-Drive Controller for V-ResQ Human-Machine Interaction.
    /// Reads UnityEngine.XR.Hands (XRHandSubsystem) with safety dwell-hold confirmation rings:
    ///   - BOTH Open Palms (0.30s)   -> EMERGENCY STOP + DISARM
    ///   - Single Open Palm (0.45s)  -> SAFE STOP (when moving or running macro)
    ///   - Right Thumbs Up (0.65s)   -> ARM / DISARM DRIVE (or RESET E-STOP if latched)
    ///   - Right Peace / V (0.65s)   -> RADAR SWEEP special command
    ///   - Left Peace / V (0.65s)    -> SWITCH NEXT ROBOT NODE (Node 0/1/2)
    ///   - Left Thumbs Up (0.65s)    -> TOGGLE SPECIAL OPS DRAWER
    ///   - Left Point (0.65s)        -> HORN / BEACON PULSE
    ///   - Opt-In Hand-Drive (Pinch) -> Right hand pinch steers linear/angular; Left hand pinch pans camera servo
    /// </summary>
    public class VResqHandGestureController : MonoBehaviour
    {
        public static VResqHandGestureController Instance { get; private set; }

        [Header("Hand Command Modes")]
        public bool handGesturesEnabled = true;
        [Tooltip("Opt-in pinch steering while Drive is ARMED so UI poking never accidentally drives the robot.")]
        public bool handDriveEnabled = false;

        [Header("Dwell Hold Timings (Seconds)")]
        public float estopHoldSeconds = 0.30f;
        public float safeStopHoldSeconds = 0.45f;
        public float commandHoldSeconds = 0.65f;
        public float commandCooldownSeconds = 1.25f;

        [Header("Pinch Hand-Drive Tuning")]
        public float pinchThresholdMeters = 0.032f;
        public float handDriveMaxOffsetMeters = 0.14f;
        public float handDriveDeadzoneMeters = 0.025f;

        // Live Diagnostics for HMI Display
        public bool LeftTracked { get; private set; }
        public bool RightTracked { get; private set; }
        public HandGesturePose LeftPose { get; private set; } = HandGesturePose.None;
        public HandGesturePose RightPose { get; private set; } = HandGesturePose.None;
        public float HoldProgress { get; private set; } = 0f;
        public string PendingCommandLabel { get; private set; } = "";
        public string LastExecutedCommand { get; private set; } = "READY";
        public bool HasActiveHandDrive { get; private set; }
        public Vector2 HandDriveInput { get; private set; } = Vector2.zero; // x = angular, y = linear
        public float HandServoDeltaDegPerSec { get; private set; } = 0f;

        private readonly List<XRHandSubsystem> _subsystems = new List<XRHandSubsystem>();
        private XRHandSubsystem _handSubsystem;
        private float _nextSubsystemScanAt = 0f;

        private string _activeGestureKey = "";
        private float _activeHoldTimer = 0f;
        private float _cooldownUntil = 0f;

        // Hand-Drive Pinch Anchors
        private bool _rightPinchAnchored = false;
        private Vector3 _rightPinchAnchorLocal = Vector3.zero;
        private bool _leftPinchAnchored = false;
        private Vector3 _leftPinchAnchorLocal = Vector3.zero;
        private readonly Transform[] pads = new Transform[2], knobs = new Transform[2];
        private readonly Transform[] pivots = new Transform[2];
        private readonly Vector3[] heldHeads = new Vector3[2];
        private readonly bool[] pinchReady = new bool[2];
        private const float StickHeight = .075f;
        private static Vector3 RestHead(Transform head, int side)
        {
            var yaw = Quaternion.Euler(0, head.eulerAngles.y, 0);
            return head.position + yaw * new Vector3(side == 0 ? -.19f : .19f, -.24f, .38f);
        }

        private bool CanGrab(int side, Transform head, Vector3 trackingPoint)
        {
            if (!pinchReady[side]) return false;
            pinchReady[side] = false;
            var world = head.parent ? head.parent.TransformPoint(trackingPoint) : trackingPoint;
            heldHeads[side] = RestHead(head, side);
            return Vector3.Distance(world, heldHeads[side]) <= .075f;
        }
        private Quaternion rightBasis, leftBasis;
        private string lastHeld = "";

        private void LateUpdate()
        {
            var cam = Camera.main;
            for (int i = 0; i < 2; i++)
            {
                bool show = cam && handGesturesEnabled && handDriveEnabled && (i == 0 ? LeftTracked : RightTracked);
                if (!show) { if (pads[i]) pads[i].gameObject.SetActive(false); continue; }
                var hand = i == 0 ? _handSubsystem.leftHand : _handSubsystem.rightHand;
                if (!hand.GetJoint(i == 0 ? XRHandJointID.Palm : XRHandJointID.IndexTip).TryGetPose(out _))
                { if (pads[i]) pads[i].gameObject.SetActive(false); continue; }
                if (!pads[i])
                {
                    pads[i] = new GameObject().transform;
                    pads[i].name = i == 0 ? "Left Drive Grip" : "Right Camera Stick";
                    var baseDisc = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
                    baseDisc.GetComponent<Collider>().enabled = false;
                    baseDisc.SetParent(pads[i], false);
                    baseDisc.localScale = i == 0 ? new Vector3(.115f,.007f,.115f) : new Vector3(.09f,.005f,.09f);
                    var trim = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
                    trim.GetComponent<Collider>().enabled = false;
                    trim.SetParent(pads[i], false);
                    trim.localPosition = new Vector3(0,.008f,.035f);
                    trim.localScale = new Vector3(.008f,.002f,.018f);
                    pivots[i] = new GameObject("Stick pivot").transform;
                    pivots[i].SetParent(pads[i], false);
                    var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
                    shaft.GetComponent<Collider>().enabled = false;
                    shaft.SetParent(pivots[i], false);
                    shaft.localScale = new Vector3(.014f,StickHeight*.5f,.014f);
                    shaft.localPosition = Vector3.up * StickHeight*.5f;
                    knobs[i] = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
                    knobs[i].GetComponent<Collider>().enabled = false;
                    knobs[i].localScale = new Vector3(.045f,.032f,.045f);
                    var props = new MaterialPropertyBlock();
                    props.SetColor("_BaseColor", i == 0 ? Color.cyan : Color.green);
                    knobs[i].GetComponent<Renderer>().SetPropertyBlock(props);
                    knobs[i].SetParent(pivots[i], false);
                    knobs[i].localPosition = Vector3.up * StickHeight;
                }
                bool anchored = i == 0 ? _leftPinchAnchored : _rightPinchAnchored;
                var origin = cam.transform.parent;
                pads[i].gameObject.SetActive(true);
                pads[i].position = (anchored ? heldHeads[i] : RestHead(cam.transform,i)) - Vector3.up * StickHeight;
                pads[i].rotation = anchored ? (origin ? origin.rotation : Quaternion.identity) * (i == 0 ? leftBasis : rightBasis) : Quaternion.Euler(0,cam.transform.eulerAngles.y,0);
                Vector2 axis = i == 0 ? HandDriveInput : new Vector2(HandServoDeltaDegPerSec / 70f,0);
                axis = Vector2.ClampMagnitude(axis, 1f);
                pivots[i].localRotation = Quaternion.Euler(axis.y*32f,0,-axis.x*32f);
            }
        }
        private void OnDisable()
        {
            ResetLiveState();
            foreach (var pad in pads) if (pad) pad.gameObject.SetActive(false);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (var pad in pads) if (pad) Destroy(pad.gameObject);
        }

        private void Update()
        {
            if (!handGesturesEnabled)
            {
                ResetLiveState();
                return;
            }

            if ((_handSubsystem == null || !_handSubsystem.running) && Time.unscaledTime >= _nextSubsystemScanAt)
            {
                _nextSubsystemScanAt = Time.unscaledTime + 1.5f;
                RefreshHandSubsystem();
            }

            if (_handSubsystem == null || !_handSubsystem.running)
            {
                ResetLiveState();
                LeftTracked = false;
                RightTracked = false;
                LeftPose = HandGesturePose.None;
                RightPose = HandGesturePose.None;
                HasActiveHandDrive = false;
                HandDriveInput = Vector2.zero;
                HandServoDeltaDegPerSec = 0f;
                HoldProgress = 0f;
                PendingCommandLabel = "";
                return;
            }

            XRHand left = _handSubsystem.leftHand;
            XRHand right = _handSubsystem.rightHand;
            LeftTracked = left.isTracked;
            RightTracked = right.isTracked;

            LeftPose = LeftTracked ? ClassifyHand(left, out _) : HandGesturePose.None;
            RightPose = RightTracked ? ClassifyHand(right, out _) : HandGesturePose.None;

            EvaluateHandDrive(left, right);
            EvaluateGestureCommands();
        }

        private void ResetLiveState()
        {
            LeftTracked = false;
            RightTracked = false;
            LeftPose = HandGesturePose.None;
            RightPose = HandGesturePose.None;
            HoldProgress = 0f;
            PendingCommandLabel = "";
            HasActiveHandDrive = false;
            HandDriveInput = Vector2.zero;
            HandServoDeltaDegPerSec = 0f;
            _activeGestureKey = "";
            _activeHoldTimer = 0f;
            _rightPinchAnchored = false;
            _leftPinchAnchored = false;
            pinchReady[0] = pinchReady[1] = false;
        }

        private void RefreshHandSubsystem()
        {
            _subsystems.Clear();
            SubsystemManager.GetSubsystems(_subsystems);
            _handSubsystem = null;
            for (int i = 0; i < _subsystems.Count; i++)
            {
                if (_subsystems[i] != null && _subsystems[i].running)
                {
                    _handSubsystem = _subsystems[i];
                    break;
                }
            }
        }

        private HandGesturePose ClassifyHand(XRHand hand, out Vector3 palmPosition)
        {
            palmPosition = Vector3.zero;
            if (!hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out Pose wrist)) return HandGesturePose.None;
            if (!hand.GetJoint(XRHandJointID.Palm).TryGetPose(out Pose palm)) palm = wrist;
            palmPosition = palm.position;

            bool hasThumbTip = hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out Pose thumbTip);
            bool hasThumbProx = hand.GetJoint(XRHandJointID.ThumbProximal).TryGetPose(out Pose thumbProx);
            bool hasIndexTip = hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose indexTip);
            bool hasIndexProx = hand.GetJoint(XRHandJointID.IndexProximal).TryGetPose(out Pose indexProx);
            bool hasMiddleTip = hand.GetJoint(XRHandJointID.MiddleTip).TryGetPose(out Pose middleTip);
            bool hasMiddleProx = hand.GetJoint(XRHandJointID.MiddleProximal).TryGetPose(out Pose middleProx);
            bool hasRingTip = hand.GetJoint(XRHandJointID.RingTip).TryGetPose(out Pose ringTip);
            bool hasRingProx = hand.GetJoint(XRHandJointID.RingProximal).TryGetPose(out Pose ringProx);
            bool hasLittleTip = hand.GetJoint(XRHandJointID.LittleTip).TryGetPose(out Pose littleTip);
            bool hasLittleProx = hand.GetJoint(XRHandJointID.LittleProximal).TryGetPose(out Pose littleProx);

            // A closed driving hand often puts thumb and index close together too.
            if (hand.handedness == Handedness.Left && handDriveEnabled &&
                hasIndexTip && hasIndexProx && hasMiddleTip && hasMiddleProx && hasRingTip && hasRingProx &&
                hasLittleTip && hasLittleProx &&
                !IsFingerExtended(wrist.position,indexProx.position,indexTip.position,1.24f) &&
                !IsFingerExtended(wrist.position,middleProx.position,middleTip.position,1.24f) &&
                !IsFingerExtended(wrist.position,ringProx.position,ringTip.position,1.24f) &&
                !IsFingerExtended(wrist.position,littleProx.position,littleTip.position,1.24f) &&
                !(hasThumbTip && hasThumbProx && IsFingerExtended(wrist.position,thumbProx.position,thumbTip.position,1.28f)))
                return HandGesturePose.Fist;

            // Pinch (Thumb Tip <-> Index Tip close)
            if (hasThumbTip && hasIndexTip && Vector3.Distance(thumbTip.position, indexTip.position) <= pinchThresholdMeters)
            {
                return HandGesturePose.Pinch;
            }

            if (!hasIndexTip || !hasIndexProx || !hasMiddleTip || !hasMiddleProx || !hasRingTip || !hasRingProx || !hasLittleTip || !hasLittleProx) return HandGesturePose.None;
            bool indexExt = IsFingerExtended(wrist.position, indexProx.position, indexTip.position, 1.42f);
            bool middleExt = IsFingerExtended(wrist.position, middleProx.position, middleTip.position, 1.42f);
            bool ringExt = hasRingTip && hasRingProx && IsFingerExtended(wrist.position, ringProx.position, ringTip.position, 1.38f);
            bool littleExt = hasLittleTip && hasLittleProx && IsFingerExtended(wrist.position, littleProx.position, littleTip.position, 1.35f);
            bool thumbExt = hasThumbTip && hasThumbProx && IsFingerExtended(wrist.position, thumbProx.position, thumbTip.position, 1.28f);

            int fourCount = (indexExt ? 1 : 0) + (middleExt ? 1 : 0) + (ringExt ? 1 : 0) + (littleExt ? 1 : 0);

            // 2. Open Palm (4 fingers extended)
            if (fourCount >= 4) return HandGesturePose.OpenPalm;

            // 3. Peace / V-Sign (Index + Middle extended, Ring + Little curled)
            if (indexExt && middleExt && !ringExt && !littleExt) return HandGesturePose.Peace;

            // 4. Point (Index extended only)
            if (indexExt && !middleExt && !ringExt && !littleExt &&
                hand.GetJoint(XRHandJointID.IndexIntermediate).TryGetPose(out Pose indexMid))
            {
                Vector3 proximal = indexMid.position - indexProx.position;
                Vector3 distal = indexTip.position - indexMid.position;
                if (Vector3.Dot(proximal.normalized, distal.normalized) > .85f)
                    return HandGesturePose.Point;
            }

            // 5. Thumbs Up (Thumb extended pointing upward, other 4 curled)
            if (thumbExt && fourCount == 0 && hasThumbTip && hasThumbProx)
            {
                Vector3 thumbDir = (thumbTip.position - thumbProx.position).normalized;
                if (Vector3.Dot(thumbDir, Vector3.up) > 0.50f) return HandGesturePose.ThumbsUp;
            }

            // 6. Fist (All 4 curled)
            if (fourCount == 0) return HandGesturePose.Fist;

            return HandGesturePose.None;
        }

        private static bool IsFingerExtended(Vector3 wrist, Vector3 prox, Vector3 tip, float ratioThreshold)
        {
            float dProx = Vector3.Distance(wrist, prox);
            float dTip = Vector3.Distance(wrist, tip);
            return dProx > 0.02f && (dTip / dProx) >= ratioThreshold;
        }

        private void EvaluateHandDrive(XRHand left, XRHand right)
        {
            HasActiveHandDrive = false;
            HandDriveInput = Vector2.zero;
            HandServoDeltaDegPerSec = 0f;

            var teleop = VResqTeleopController.Instance != null ? VResqTeleopController.Instance : GetComponent<VResqTeleopController>();
            // The handles can be exercised offline. Teleop gates all hardware output.
            if (!handDriveEnabled || teleop == null)
            {
                pinchReady[0] = pinchReady[1] = false;
                _rightPinchAnchored = false;
                _leftPinchAnchored = false;
                return;
            }

            if (RightTracked && RightPose != HandGesturePose.Pinch) pinchReady[1] = true;
            if (LeftTracked && LeftPose != HandGesturePose.None && LeftPose != HandGesturePose.Fist) pinchReady[0] = true;
            if (!RightTracked) pinchReady[1] = false;
            if (!LeftTracked) pinchReady[0] = false;
            // Open Palm immediately cancels hand-drive for safety
            if (LeftPose == HandGesturePose.OpenPalm || RightPose == HandGesturePose.OpenPalm)
            {
                _rightPinchAnchored = false;
                _leftPinchAnchored = false;
                return;
            }

            Transform head = Camera.main != null ? Camera.main.transform : transform;
            Quaternion basis = Quaternion.Euler(0, head.localEulerAngles.y, 0);

            if (LeftTracked && LeftPose == HandGesturePose.Fist && left.GetJoint(XRHandJointID.Palm).TryGetPose(out Pose palm))
            {
                if (!_leftPinchAnchored && pinchReady[0])
                {
                    pinchReady[0] = false;
                    _leftPinchAnchored = true;
                    _leftPinchAnchorLocal = palm.position;
                    heldHeads[0] = head.parent ? head.parent.TransformPoint(palm.position) : palm.position;
                    leftBasis = basis;
                }
                if (_leftPinchAnchored)
                {
                    var d = Quaternion.Inverse(leftBasis) * (palm.position - _leftPinchAnchorLocal);
                    HandDriveInput = Vector2.ClampMagnitude(new Vector2(
                        ShapeAxis(d.x,handDriveDeadzoneMeters,handDriveMaxOffsetMeters),
                        ShapeAxis(d.z,handDriveDeadzoneMeters,handDriveMaxOffsetMeters)),1f);
                    HasActiveHandDrive = true;
                }
            }
            else _leftPinchAnchored = false;

            if (RightTracked && RightPose == HandGesturePose.Pinch && right.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose tip)
                && (_rightPinchAnchored || CanGrab(1,head,tip.position)))
            {
                if (!_rightPinchAnchored)
                {
                    _rightPinchAnchored = true;
                    _rightPinchAnchorLocal = tip.position;
                    rightBasis = basis;
                }
                float x = (Quaternion.Inverse(rightBasis) * (tip.position - _rightPinchAnchorLocal)).x;
                HandServoDeltaDegPerSec = ShapeAxis(x,handDriveDeadzoneMeters,handDriveMaxOffsetMeters) * 70f;
            }
            else _rightPinchAnchored = false;
        }

        private static float ShapeAxis(float delta, float deadzone, float maxRange)
        {
            // Legacy scenes stored 14 cm travel for free-air pinch; match the physical stick.
            maxRange = Mathf.Clamp(maxRange, .025f, .055f);
            deadzone = Mathf.Clamp(deadzone, .002f, .008f);
            float abs = Mathf.Abs(delta);
            if (abs <= deadzone) return 0f;
            float norm = Mathf.Clamp01((abs - deadzone) / Mathf.Max(0.01f, maxRange - deadzone));
            return Mathf.Sign(delta) * norm;
        }

        private void EvaluateGestureCommands()
        {
            if (Time.unscaledTime < _cooldownUntil)
            {
                HoldProgress = 0f;
                PendingCommandLabel = "COOLDOWN";
                return;
            }

            string candidateKey = "";
            string candidateLabel = "";
            float requiredHold = commandHoldSeconds;

            var client = VResqClient.Instance != null ? VResqClient.Instance : GetComponent<VResqClient>();
            var teleop = VResqTeleopController.Instance != null ? VResqTeleopController.Instance : GetComponent<VResqTeleopController>();
            bool estopActive = client != null && client.LatestState != null && client.LatestState.estop;
            bool motionActive = (teleop != null && teleop.DriveArmed && (teleop.LiveDriveStick.sqrMagnitude > 0.02f || HasActiveHandDrive)) ||
                                (client != null && client.LatestState != null && !string.IsNullOrEmpty(client.LatestState.active_action));

            if (LeftPose == HandGesturePose.OpenPalm && RightPose == HandGesturePose.OpenPalm)
            {
                candidateKey = "BOTH_PALMS_ESTOP";
                candidateLabel = "BOTH PALMS -> EMERGENCY STOP";
                requiredHold = estopHoldSeconds;
            }
            else if ((LeftPose == HandGesturePose.OpenPalm || RightPose == HandGesturePose.OpenPalm) && motionActive)
            {
                candidateKey = "PALM_SAFE_STOP";
                candidateLabel = "OPEN PALM -> SAFE STOP";
                requiredHold = safeStopHoldSeconds;
            }
            else if (HasActiveHandDrive || _rightPinchAnchored)
            {
                // Never issue a menu, node-switch or arm gesture while manipulating a stick.
            }
            else if (LeftPose == HandGesturePose.Peace && RightPose == HandGesturePose.Peace)
            {
                candidateKey = "SHOW_PROTOCOL";
                candidateLabel = "BOTH PEACE -> PROTOCOL";
            }
            else if (LeftPose == HandGesturePose.Point && RightPose == HandGesturePose.Peace)
            {
                candidateKey = "SHOW_LIVE";
                candidateLabel = "LEFT POINT + RIGHT PEACE -> LIVE";
            }
            else if (RightPose == HandGesturePose.ThumbsUp && LeftPose != HandGesturePose.ThumbsUp)
            {
                candidateKey = estopActive ? "R_THUMB_RESET_ESTOP" : "R_THUMB_ARM_TOGGLE";
                candidateLabel = estopActive ? "RIGHT THUMB UP -> RESET E-STOP" : "RIGHT THUMB UP -> ARM / DISARM";
                requiredHold = commandHoldSeconds;
            }
            else if (RightPose == HandGesturePose.Peace)
            {
                candidateKey = "R_PEACE_RADAR";
                candidateLabel = "RIGHT PEACE -> RADAR SWEEP";
                requiredHold = commandHoldSeconds;
            }
            else if (LeftPose == HandGesturePose.Peace && RightPose != HandGesturePose.Point)
            {
                candidateKey = "L_PEACE_NEXT_NODE";
                candidateLabel = "LEFT PEACE -> SWITCH ROBOT NODE";
                requiredHold = commandHoldSeconds;
            }
            else if (LeftPose == HandGesturePose.ThumbsUp)
            {
                candidateKey = "L_THUMB_SPECIAL_OPS";
                candidateLabel = "LEFT THUMB UP -> SPECIAL OPS MENU";
                requiredHold = commandHoldSeconds;
            }
            else if (RightPose == HandGesturePose.Point && LeftPose == HandGesturePose.Peace)
            {
                candidateKey = "PRECISION_DOWN";
                candidateLabel = "LEFT PEACE + RIGHT POINT -> LOWER SPEED";
            }
            else if (RightPose == HandGesturePose.Fist && LeftPose == HandGesturePose.Point)
            {
                candidateKey = "RECENTER_LOOK";
                candidateLabel = "RIGHT FIST + LEFT POINT -> CENTER VIEW";
            }
            else if (LeftPose == HandGesturePose.Point)
            {
                candidateKey = "L_POINT_HORN";
                candidateLabel = "LEFT POINT -> HORN / BEACON PULSE";
                requiredHold = commandHoldSeconds;
            }

            if (string.IsNullOrEmpty(candidateKey))
            {
                lastHeld = "";
                _activeGestureKey = "";
                _activeHoldTimer = 0f;
                HoldProgress = 0f;
                PendingCommandLabel = "";
                return;
            }

            if (candidateKey == lastHeld) return;
            if (candidateKey != _activeGestureKey)
            {
                _activeGestureKey = candidateKey;
                _activeHoldTimer = 0f;
            }

            _activeHoldTimer += Time.unscaledDeltaTime;
            HoldProgress = Mathf.Clamp01(_activeHoldTimer / Mathf.Max(0.1f, requiredHold));
            PendingCommandLabel = candidateLabel;

            if (_activeHoldTimer >= requiredHold)
            {
                _activeGestureKey = "";
                _activeHoldTimer = 0f;
                HoldProgress = 0f;
                PendingCommandLabel = "";
                _cooldownUntil = Time.unscaledTime + commandCooldownSeconds;
                TriggerGestureMacro(candidateKey);
                lastHeld = candidateKey;
            }
        }

        /// <summary>
        /// Executes a gesture command (callable both from XR Hand Tracking dwell lock and clickable HMI quick-buttons).
        /// </summary>
        public void TriggerGestureMacro(string commandId)
        {
            var client = VResqClient.Instance != null ? VResqClient.Instance : GetComponent<VResqClient>();
            var teleop = VResqTeleopController.Instance != null ? VResqTeleopController.Instance : GetComponent<VResqTeleopController>();
            var specialPanel = GetComponent<VResqSpecialCommandsPanel>();
            var hmi = VResqHMIFeedback.Instance;

            switch (commandId)
            {
                case "SHOW_PROTOCOL":
                case "SHOW_LIVE":
                    teleop?.SetDriveArmed(false);
                    var tab = commandId == "SHOW_PROTOCOL" ? "Protocol" : "Live";
                    GetComponent<VResqOperationsPanel>()?.Show(tab);
                    LastExecutedCommand = tab;
                    break;
                case "PRECISION_DOWN":
                    teleop?.AdjustSpeedScale(-0.1f);
                    LastExecutedCommand = "LOWER SPEED";
                    break;
                case "RECENTER_LOOK":
                    teleop?.ResetLookOrientation();
                    LastExecutedCommand = "CENTER VIEW";
                    break;
                case "BOTH_PALMS_ESTOP":
                    LastExecutedCommand = "EMERGENCY STOP (BOTH PALMS)";
                    teleop?.SetDriveArmed(false);
                    teleop?.ActionEmergencyStop();
                    hmi?.PlayWarningPulse();
                    hmi?.ShowToast("HAND GESTURE: E-STOP", "Both palms detected — robot halted & disarmed", new Color(1f, 0.22f, 0.28f), 3.2f);
                    break;

                case "PALM_SAFE_STOP":
                    LastExecutedCommand = "SAFE STOP (OPEN PALM)";
                    client?.StopOperatorMotion();
                    client?.SendActionCommand("safe_stop");
                    hmi?.PlayDisarmChime();
                    hmi?.ShowToast("HAND GESTURE: SAFE STOP", "Motion & active macro cancelled safely", new Color(1f, 0.78f, 0.18f), 2.4f);
                    break;

                case "R_THUMB_RESET_ESTOP":
                    LastExecutedCommand = "RESET E-STOP (THUMB UP)";
                    client?.ResetEstop();
                    hmi?.PlayGestureLock();
                    hmi?.ShowToast("HAND GESTURE: RESET E-STOP", "E-STOP latch reset requested", new Color(0.1f, 1f, 0.55f), 2.6f);
                    break;

                case "R_THUMB_ARM_TOGGLE":
                    if (teleop != null)
                    {
                        bool next = !teleop.DriveArmed;
                        teleop.SetDriveArmed(next);
                        LastExecutedCommand = teleop.DriveArmed ? "DRIVE ARMED (THUMB UP)" : "DRIVE DISARMED (THUMB UP)";
                        hmi?.ShowToast(
                            teleop.DriveArmed ? "HAND GESTURE: DRIVE ARMED" : "HAND GESTURE: DISARMED",
                            teleop.DriveStatus,
                            teleop.DriveArmed ? new Color(0.1f, 1f, 0.55f) : new Color(1f, 0.78f, 0.18f),
                            2.6f);
                    }
                    break;

                case "R_PEACE_RADAR":
                    LastExecutedCommand = "RADAR SWEEP (RIGHT PEACE)";
                    client?.SendActionCommand("radar_sweep");
                    hmi?.PlayGestureLock();
                    hmi?.ShowToast("HAND GESTURE: RADAR SWEEP", "Special sonar radar scan triggered", new Color(0f, 0.92f, 1f), 2.6f);
                    break;

                case "L_PEACE_NEXT_NODE":
                    LastExecutedCommand = "SWITCH ROBOT NODE (LEFT PEACE)";
                    teleop?.SetDriveArmed(false);
                    teleop?.CycleNextFleetNode();
                    hmi?.PlayGestureLock();
                    hmi?.ShowToast("HAND GESTURE: SWITCH NODE", "Switching active swarm robot node", new Color(0f, 0.92f, 1f), 2.4f);
                    break;

                case "L_THUMB_SPECIAL_OPS":
                    LastExecutedCommand = "SPECIAL OPS DRAWER (LEFT THUMB)";
                    specialPanel?.ToggleDrawer();
                    hmi?.PlayGestureLock();
                    hmi?.ShowToast("HAND GESTURE: SPECIAL OPS", "Toggled Special Commands & Hand HMI Panel", new Color(0f, 0.92f, 1f), 2.2f);
                    break;

                case "L_POINT_HORN":
                    LastExecutedCommand = "HORN PULSE (LEFT POINT)";
                    client?.SendBuzzerPulse(0.45f);
                    hmi?.PlayGestureLock();
                    hmi?.ShowToast("HAND GESTURE: HORN PULSE", "Acoustic beacon / buzzer pulsed on robot", new Color(1f, 0.78f, 0.18f), 2.2f);
                    break;
            }
        }

        public string GetCompactStatus()
        {
            if (!handGesturesEnabled) return "HANDS: OFF";
            if (!LeftTracked && !RightTracked) return "HANDS: STANDBY (NO XR HANDS)";
            string l = LeftTracked ? LeftPose.ToString().ToUpperInvariant() : "--";
            string r = RightTracked ? RightPose.ToString().ToUpperInvariant() : "--";
            string driveTag = handDriveEnabled ? " | LEFT GRIP" : "";
            return $"HANDS L:{l} R:{r}{driveTag}";
        }
    }
}
