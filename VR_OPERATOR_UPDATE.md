# V-ResQ Quest Link Operator 2026.11.16.1

## Install on the headset PC

Extract the complete ZIP. Keep VResQ-Operator.exe, its Data folder, UnityPlayer.dll
and the other included files together. Enter Quest Link or Air Link, then launch
VResQ-Operator.exe. The robot and the headset PC must be reachable over the LAN.
This package is a Windows Link player, not an Android standalone APK.

## Operator pages

- Live retains robot video, fleet selection and controls.
- Telemetry reads all fields from the selected robot's /api/state response.
- Protocol reads /api/protocol with TX/RX, CRC counters and telemetry age.
- Swarm reads /api/swarm from the selected robot. Monitoring is read-only.
- System combines /api/system with local operator XR diagnostics.

Payload pages support scrolling, Page up, Page down, Refresh and Save payload.
Unknown nested JSON fields are retained. Missing summary fields display --.
Unavailable endpoints display the HTTP/network error and retry. Old data remains
marked stale. Source changes clear the previous robot's payload.
Robot firmware must expose the corresponding APIs for those pages to populate.

## Headset camera diagnosis

Developer runtime features and Pass-through over Meta Horizon Link were reported
enabled on the headset PC. Their state alone does not confirm successful runtime
passthrough composition. Physical passthrough still needs verification there.

On that PC, open System and choose Save payload. The JSON includes the actual
XR loader, runtime version, XR_FB_passthrough extension, AR session state,
camera subsystem state, OpenXR feature flags, camera alpha and Player.log path.
Local XR diagnostics remain available when the robot API is unreachable.
Send that JSON and Player.log when the headset camera remains black.
Restart XR is an explicit recovery action that restarts the local XR session.
It temporarily interrupts headset tracking. Use while stationary.

The Headset view toggle controls headset passthrough. Robot monitor controls
robot video independently. A running camera subsystem is diagnostic evidence,
not proof that physical passthrough pixels are visible in the headset.

## Validation

AutoBuild_Unity.bat repairs component wiring, runs regression and UI raycast
checks, then builds the Windows player. Test captures under Logs use clearly
marked fixtures. They do not represent connected robot telemetry.
The tests do not invoke drive commands. They cover mouse-pointer UI hit targets
and XR rig wiring. Physical controller/hand presses and passthrough require
testing on the headset PC.

The new attitude instrument smooths valid pitch/roll and displays unavailable
when telemetry is missing, stale or invalid. It does not fabricate IMU readings.
Serial turnaround diagnostics are not presented as measured end-to-end RTT.
