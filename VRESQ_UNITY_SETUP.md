# Unity Operator Setup

Open this project with Unity 6000.3.22f1.

## Automatic Setup

Choose `V-ResQ > Auto Setup Components and Video Controls` to update the active
scene and save its component references. Existing HUD layouts are retained.
`VResqAutoSetup.BuildConfiguredScenes` applies this to BasicScene and SampleScene
from batchmode. The full HUD rebuild also invokes the component setup.

Choose `V-ResQ > Auto Build Windows Operator` to configure both scenes and build
`Builds/Windows/VResQ-Operator.exe`. The build starts with SampleScene.
The same build is available through `AutoBuild_Unity.bat` while the project is
closed in Unity. Set VRESQ_UNITY_EDITOR if the Editor is installed elsewhere.
The Windows build uses Mono and restores the prior scripting backend afterward.

## Component Placement

| Object | Components and references |
| --- | --- |
| Tactical_VR_HUD_Root | Canvas, CanvasScaler, GraphicRaycaster, TrackedDeviceGraphicRaycaster |
| Tactical_VR_HUD_Root | VResqFrameworkConfig, VResqAutoDiscovery, VResqClient |
| Tactical_VR_HUD_Root | VResqCameraReceiver, VResqSurroundDisplay, VResqTeleopController, TacticalHUDFollow, RescueRobotVRHUD |
| Tactical_VR_HUD_Root | VResqPresentationControls, linked to the two toggles, brightness slider, reset button and data status |
| Main XR Camera | Existing Camera and XR tracking components. Auto Setup assigns this camera to Canvas, surround and HUD follow |
| Center_Robot_Camera_Screen | RawImage assigned to camera receiver and HUD. PresentationControls supplies the camera shader |
| External_3D_World_Environment | Scene environment root assigned to SurroundDisplay |
| EventSystem | Existing input module retained. A missing input module is filled with XRUIInputModule |

## Runtime Controls

The VR background toggle controls the rendered environment and panoramic camera
background. Switching it off selects a black background. The Robot monitor toggle
controls the center camera image. Brightness ranges from 0.5 to 2, starting at 1.2.
Use the existing Save Profile action to persist these choices.

Brightness is applied on the GPU for display. Original JPEG bytes and telemetry
remain available for parsing and recording.

## Data Alignment and Quality

The MJPEG reader accepts bounded Content-Length parts and the X-VResQ-Meta header.
It presents each JPEG together with its frame identity and telemetry snapshot.
For the selected robot, HUD sensor readings use the valid snapshot attached to
the displayed frame. Operational connection and safety state continue to arrive
through the robot state connection. A fallback camera from another robot retains
its own identity in frame metadata.

Metadata-free streams display UNSYNCED. Duplicate sequences and invalid frame
identity or health are rejected. Telemetry with non-finite values or invalid
sensor ranges is flagged. The client keeps LastRawState and LastTelemetryError,
and the receiver retains rejected metadata for inspection. Reset counters clears
the rejection count and current receiver message.

Detection boxes require an explicit frame_id matching the displayed image.
Payloads that omit this field require a producer update before overlay display.
The host_jpeg_receive timestamp describes host JPEG completion. Camera exposure
time and cross-host latency require sensor timestamps and clock synchronization.

## Verification

Run `VResqRegressionChecks.VerifyScene` in Unity batchmode to check component
wiring, toggles, brightness material, frame/telemetry parsing and data rejection.
The check writes an operator scene capture to `Logs/codex-scene.png`.
Physical XR input and live-camera performance need a headset and connected robot.

## Verification Notes

The latest Windows build completed successfully with 28 regression checks.
The Unity build log still includes package warnings about AR Foundation simulation
asset relocation and missing Thai glyphs in the existing LiberationSans font.
Unity also serializes XR and URP settings while building.

The live Node Two stream at 192.168.1.149 supplied frame metadata with valid=false
and its camera API reported OpenCV not ready during verification. The receiver
retains this camera error in its diagnostics. Live image validation requires that
camera service to recover.
