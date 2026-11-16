using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VResQ.Core;
using VResQ.Video;

public static class VResqOperationsChecks
{
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Debug.Log("[VRESQ_OPERATIONS_CHECK] PASS " + name);
    }

    public static void Run(RescueRobotVRHUD hud)
    {
        var view = hud.GetComponent<VResqOperationsPanel>();
        Check(view != null && view.tabs.Length == 5, "Five operations pages are wired");
        typeof(VResqOperationsPanel).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
        var hidden = view.liveSections.FirstOrDefault(section => !section.activeSelf);
        foreach (string page in new[] { "Live", "Telemetry", "Protocol", "Swarm", "System" })
        {
            view.Show(page);
            if (page != "Live")
            {
                view.ApplyPayload("{\"fixture_only\":true,\"unknown_nested_field\":{\"retained\":123},\"protocol\":{\"name\":\"TEST FIXTURE\"},\"traffic\":{\"tx_frames\":42,\"rx_frames\":40},\"integrity\":{\"crc_checked\":40,\"crc_errors\":0},\"latency\":{\"telemetry_age_ms\":12}}");
                Check(view.LastPayload.Contains("unknown_nested_field") && view.body.text.Contains("123"), page + " preserves all received fields");
            }
            CaptureAndCheck(hud, page);
        }
        view.Show("Live");
        Check(hidden == null || !hidden.activeSelf, "Page navigation preserves hidden Live sections");
        view.Show("Telemetry");
        view.ApplyPayload("{\"retained_on_error\":true}");
        string retained = view.LastPayload;
        bool rejected = false;
        try { view.ApplyPayload("{broken"); } catch (Newtonsoft.Json.JsonException) { rejected = true; }
        Check(rejected && view.LastPayload == retained, "Malformed payload does not replace valid data");
        var longPayload = new JObject();
        for (int i = 0; i < 120; i++) longPayload["field_" + i] = i;
        view.ApplyPayload(longPayload.ToString());
        Canvas.ForceUpdateCanvases();
        view.scroll.verticalNormalizedPosition = 1;
        view.ScrollPage(-1);
        Check(view.scroll.verticalNormalizedPosition < 1 && view.body.text.Contains("field_119"), "Long payload stays complete and page navigation scrolls");
        var instrument = hud.GetComponentInChildren<VResqAttitudeDisplay>(true);
        Check(instrument != null, "Attitude display is wired");
        instrument.Render(new TelemetryData { pitch_deg = 0, roll_deg = 0, yaw_deg = 0 }, true, 1);
        Check(instrument.pitchLabel.text.Contains("0"), "Zero-degree IMU reading is retained");
        instrument.Render(null, false, 1);
        Check(instrument.stateLabel.text.Contains("UNAVAILABLE"), "Missing IMU data is explicit");
        instrument.Render(new TelemetryData { pitch_deg = float.NaN }, true, 1);
        Check(instrument.stateLabel.text.Contains("UNAVAILABLE"), "Non-finite attitude is rejected");
        view.Show("System");
        var payload = JObject.Parse(view.LastPayload);
        Check(payload.SelectToken("operator.xr_diagnostics.passthrough_extension") != null, "System export contains runtime passthrough diagnostics");
        view.Show("Live");
        hud.ToggleSettingsModal();
        CaptureAndCheck(hud, "Settings");
        hud.ToggleSettingsModal();
    }

    private static void CaptureAndCheck(RescueRobotVRHUD hud, string page)
    {
        Canvas.ForceUpdateCanvases();
        var camera = Camera.main;
        var target = new RenderTexture(1440, 1100, 24);
        var previous = camera.targetTexture;
        var active = RenderTexture.active;
        var capture = new Texture2D(1440, 1100, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            capture.ReadPixels(new Rect(0, 0, 1440, 1100), 0, 0);
            capture.Apply();
            System.IO.File.WriteAllBytes("Logs/codex-operations-" + page + ".png", capture.EncodeToPNG());
            var events = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            int checkedButtons = 0;
            foreach (var button in hud.GetComponentsInChildren<Button>())
            {
                if (!button.IsInteractable()) continue;
                bool modal = hud.settingsModalRoot != null && hud.settingsModalRoot.activeSelf;
                if (modal && !button.transform.IsChildOf(hud.settingsModalRoot.transform)) continue;
                var pointer = new PointerEventData(events) { position = RectTransformUtility.WorldToScreenPoint(camera, button.transform.position) };
                var hits = new List<RaycastResult>();
                hud.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
                Check(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button,
                    page + " pointer reaches " + button.name + " (top=" + (hits.Count > 0 ? hits[0].gameObject.name : "none") + ")");
                checkedButtons++;
            }
            Check(checkedButtons > 0, page + " has reachable controls");
        }
        finally
        {
            camera.targetTexture = previous;
            RenderTexture.active = active;
            UnityEngine.Object.DestroyImmediate(capture);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
