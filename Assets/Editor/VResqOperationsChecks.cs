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
        Check(view != null && view.tabs.Length == 6, "Six operations pages are wired");
        typeof(VResqOperationsPanel).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
        var controls = view.controlsPanel.GetComponent<VResQ.Control.VResqControlPanel>();
        typeof(VResQ.Control.VResqControlPanel).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controls, null);
        var hidden = view.liveSections.FirstOrDefault(section => !section.activeSelf);
        foreach (string page in new[] { "Live", "Telemetry", "Protocol", "Swarm", "System", "Controls" })
        {
            view.Show(page);
            if (page != "Live" && page != "Controls")
            {
                view.ApplyPayload("{\"fixture_only\":true,\"unknown_nested_field\":{\"retained\":123},\"protocol\":{\"name\":\"TEST FIXTURE\"},\"traffic\":{\"tx_frames\":42,\"rx_frames\":40},\"integrity\":{\"crc_checked\":40,\"crc_errors\":0},\"latency\":{\"telemetry_age_ms\":12}}");
                if (page == "Swarm")
                {
                    var fixture = JObject.Parse(view.LastPayload);
                    fixture["swarm_summary"] = JObject.Parse("{\"alive_nodes\":2,\"total_nodes\":2,\"leader_name\":\"TEST FIXTURE\",\"version_synced\":true,\"formation\":\"relay_chain\"}");
                    fixture["nodes"] = JArray.Parse("[{\"node_name\":\"Fixture scout\",\"node_uid\":\"fixture-scout\",\"ip\":\"192.0.2.1\",\"port\":8000,\"hw_mode\":\"UNITY-SIM\",\"state\":\"ALIVE\"}]");
                    fixture["_map"] = JObject.Parse("{\"arena_bounds\":{\"min_x\":-180,\"max_x\":180,\"min_y\":-180,\"max_y\":180},\"robots\":[{\"pose\":{\"x\":-80,\"y\":-60},\"hw_mode\":\"UNITY-SIM\"},{\"pose\":{\"x\":90,\"y\":70},\"hw_mode\":\"UNITY-SIM\"}],\"arena_walls\":[{\"x1\":-100,\"y1\":-100,\"x2\":-100,\"y2\":100}]}");
                    fixture["_timeline"] = JObject.Parse("{\"events\":[{\"type\":\"fixture_test_only\",\"message\":\"No hardware commands issued\"}]}");
                    view.ApplyPayload(fixture.ToString());
                }
                view.SetRawView(true);
                Check(view.LastPayload.Contains("unknown_nested_field") && view.body.text.Contains("123"), page + " preserves all received fields");
                view.SetRawView(false);
            }
            CaptureAndCheck(hud, page);
            if (page == "Swarm")
            {
                var mesh = view.swarmPlot.canvasRenderer.GetMesh();
                Check(mesh != null && mesh.vertexCount > 0, "Swarm server-pose fixture renders visible geometry");
            }
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
        SafetyChecks(hud, view);
    }

    private static void SafetyChecks(RescueRobotVRHUD hud, VResqOperationsPanel view)
    {
        var controller = hud.GetComponent<VResQ.Control.VResqTeleopController>();
        Check(hud.GetComponent<VResQ.Control.VResqQuestInput>() != null, "Quest button router is wired");
        controller.SetDriveArmed(true);
        Check(!controller.DriveArmed, "Offline operator cannot arm drive");
        Check(VResQ.Control.VResqQuestInput.ShapeDrive(new Vector2(.01f, .01f), .1f, 1, false, false) == Vector2.zero, "Quest deadzone removes neutral noise");
        var axis = VResQ.Control.VResqQuestInput.ShapeDrive(Vector2.up, .1f, 1, true, false);
        Check(axis.y == -1, "Quest forward inversion works");
        Check(VResQ.Control.VResqQuestInput.ShapeDrive(new Vector2(float.NaN, 0), .1f, 1, false, false) == Vector2.zero, "Invalid stick values are rejected");
        var client = hud.GetComponent<VResqClient>();
        string rawState = client.LastRawState;
        Check(client.HandleCommandAck("{\"type\":\"command_ack\",\"scope\":\"jetson\",\"sequence\":42,\"accepted\":true,\"reason\":\"accepted\"}") && client.LastCommandAck.Contains("ACCEPTED"), "Jetson command ACK is parsed separately");
        Check(client.LastRawState == rawState, "Command ACK cannot replace a telemetry frame");
        Check(client.HandleCommandAck("{\"type\":\"command_ack\",\"scope\":\"jetson\",\"accepted\":\"bad\"}") && client.LastCommandAck.Contains("Malformed"), "Malformed ACK is explicit");
        Check(new ControlPayload().ToJson().Contains("\"request_ack\":true"), "Drive commands request optional backend ACKs");
        int sent = client.OperatorCommandsSent;
        client.SendActionCommand("back_out");
        Check(client.OperatorCommandsSent == sent && client.LastCommandStatus.Contains("authentication"), "Offline action reports rejection without sending motion");
        var contract = JObject.Parse("{\"serial\":{\"protocol\":{\"name\":\"WRAPPED FIXTURE\"},\"traffic\":{\"tx_frames\":0},\"integrity\":{\"crc_checked\":false},\"command_path\":{\"esp32_cmd_rx_ok\":0}}}");
        string report = VResqDashboardReport.Protocol(contract);
        Check(report.Contains("WRAPPED FIXTURE") && report.Contains("COMMAND PATH") && report.Contains("False"), "Protocol supports the same wrapped system schema as the web");
        view.Show("Swarm");
        view.ApplyPayload("{\"nodes\":[{\"node_uid\":\"fixture-only\",\"node_name\":\"Fixture node\",\"ip\":\"192.0.2.1\",\"port\":8000,\"hw_mode\":\"UNITY-SIM\"}]}");
        Check(view.connectNodeButton.interactable && view.nodeSelector.options[0].text.Contains("Fixture node"), "Swarm discovered node becomes selectable without connecting");
        view.Show("Telemetry");
        Check(!view.connectNodeButton.interactable, "Page/source reset removes stale node target");
        view.Show("Live");
        var headset = Camera.main.GetComponent<VResqHeadsetPassthrough>();
        headset.ToggleIsolation(hud.GetComponent<Canvas>());
        Check(headset.Isolated && !hud.GetComponent<Canvas>().enabled, "Headset-only test removes HUD coverage");
        headset.RestoreHud();
        Check(hud.GetComponent<Canvas>().enabled, "Operator menu can restore HUD after headset-only test");
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
