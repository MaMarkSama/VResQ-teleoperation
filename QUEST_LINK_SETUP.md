# Quest 3 / 3S over Meta Horizon Link

The operator application uses the headset passthrough compositor, XR Hands and
the existing Unity XR Interaction Toolkit hands rig.

## Controls

- Headset view toggles passthrough from the headset cameras.
- Robot monitor toggles the center robot image.
- Next camera selects the next robot camera explicitly. The selected IP and
  MANUAL mode appear in the status strip. An unavailable selected camera reports
  its error until another source is selected.
- Auto camera resumes camera discovery and failover.
- The existing view buttons cycle robot panorama views and headset view.
- Both hands have near poke and far interaction from the supplied XRI hands rig.
  Point and pinch for distant UI, or touch a nearby UI surface. The rig also
  includes controller fallback. Mouse input is enabled on the PC.

The status strip shows left and right hand tracking availability. Hands appear
when the headset runtime provides tracked hand data.

## Meta Horizon Link Settings

In the PC app, open Settings and Developer. Enable Developer runtime features,
Pass-through over Meta Horizon Link, and Spatial data over Meta Horizon Link.
Enable hand tracking on the headset and connect through Link or Air Link.

The PC already used the Meta OpenXR runtime when inspected. The agent's app UI
inspection was stopped with Escape, so the Developer settings were not verified
or changed through the app interface.

Passthrough is composited in the headset. Unity's Game view does not display the
passthrough pixels. Test the physical surroundings in the headset.

Official setup documentation is available at
[Unity Meta Horizon Link configuration](https://docs.unity3d.com/Packages/com.unity.xr.meta-openxr@2.5/manual/get-started/link.html)
and [Meta passthrough scene setup](https://docs.unity3d.com/Packages/com.unity.xr.meta-openxr@2.5/manual/features/camera.html).

## Project Setup

Auto Setup installs `VResQ XR Hands Rig` from the project's existing hands prefab,
disables the previous XR Origin, and rebinds HUD references to the new headset
camera. It configures ARSession, ARCameraManager, transparent camera clearing,
OpenXR hand and camera features, and XRUIInputModule with mouse and XR input.
The old XR Origin remains in the scene as an inactive object.

The UI raycaster ignores world geometry occlusion. Decorative HUD graphics have
raycast targets disabled. Presentation button listeners survive the HUD's command
button wiring.

Windows builds use Mono through AutoBuild_Unity.bat or the V-ResQ build menu.
Headset passthrough and physical hand gestures require on-device verification.
