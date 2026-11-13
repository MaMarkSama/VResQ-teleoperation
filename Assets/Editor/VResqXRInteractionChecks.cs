using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Interaction.Toolkit.UI;
using VResQ.Video;

public static class VResqXRInteractionChecks
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        Debug.Log("[VRESQ_XR_CHECK] PASS " + message);
    }

    public static void Run(RescueRobotVRHUD hud)
    {
        var rig = GameObject.Find("VResQ XR Hands Rig");
        Check(rig != null, "XR hands rig is active");
        var components = rig.GetComponentsInChildren<MonoBehaviour>(true);
        Check(components.Count(c => c != null && c.GetType().Name == "XRHandTrackingEvents") >= 2, "Left and right tracked hands are configured");
        Check(components.Count(c => c != null && c.GetType().Name == "XRPokeInteractor") >= 2, "Both hands have poke interactors");
        Check(components.Count(c => c != null && (c.GetType().Name == "NearFarInteractor" || c.GetType().Name == "XRRayInteractor")) >= 2, "Both hands have far interaction");
        var events = UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).First(e => e.enabled);
        Check(UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Count(e => e.enabled) == 1, "Only one EventSystem is enabled");
        var module = events.GetComponent<XRUIInputModule>();
        Check(module != null && module.enabled && module.enableXRInput && module.enableMouseInput, "XR and mouse input are enabled");
        Check(Camera.main.GetComponent<ARCameraManager>() != null && Camera.main.backgroundColor.a == 0,
            "Headset camera has a transparent passthrough background");
        var controls = hud.GetComponent<VResqPresentationControls>();
        hud.WireAllInteractiveButtons();
        controls.nextCameraButton.onClick.Invoke();
        var receiver = hud.GetComponent<VResqCameraReceiver>();
        Check(!receiver.AutomaticCameraSelection, "Next camera preserves explicit source selection");
        receiver.StopStream();
        controls.autoCameraButton.onClick.Invoke();
        Check(receiver.AutomaticCameraSelection, "Auto camera restores source discovery");
        receiver.StopStream();
        Canvas.ForceUpdateCanvases();
        var preview = RenderTexture.GetTemporary(640, 480, 24);
        var previousTarget = Camera.main.targetTexture;
        try
        {
            Camera.main.targetTexture = preview;
            Camera.main.Render();
        }
        finally
        {
            Camera.main.targetTexture = previousTarget;
            RenderTexture.ReleaseTemporary(preview);
        }
        var button = hud.GetComponentsInChildren<Button>(true).First(b => b.name.Contains("BTN_CONFIG"));
        var pointer = new PointerEventData(events)
        {
            position = RectTransformUtility.WorldToScreenPoint(Camera.main, button.transform.position)
        };
        var hits = new List<RaycastResult>();
        hud.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
        Debug.Log($"[VRESQ_XR_CHECK] Pointer={pointer.position} Camera={Camera.main.transform.position} HUD={hud.transform.position} PixelRect={Camera.main.pixelRect} Button={button.transform.position} Depth={button.targetGraphic.depth} CanvasCamera={hud.GetComponent<Canvas>().worldCamera} Hits={string.Join(",", hits.Select(hit => hit.gameObject.name))}");
        Check(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button, "UI settings button receives pointer raycast");
        Debug.Log("[VRESQ_XR_CHECK] Completed");
    }
}
