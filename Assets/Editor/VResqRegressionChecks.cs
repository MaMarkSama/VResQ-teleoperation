using System;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEngine;
using VResQ.Core;
using VResQ.Video;

public static class VResqRegressionChecks
{
    private sealed class FragmentedStream : System.IO.MemoryStream
    {
        public FragmentedStream(byte[] bytes) : base(bytes) { }
        public override System.Threading.Tasks.Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            return base.ReadAsync(buffer, offset, Math.Min(count, 3), token);
        }
    }
    public static void VerifyScene()
    {
        Run();
        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        int missing = 0;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject);
        Check(missing == 0, "SampleScene has no missing scripts");
        Check(UnityEngine.Object.FindObjectsByType<VResqClient>(FindObjectsSortMode.None).Length == 1,
            "SampleScene has one active VResqClient");
        var camera = Camera.main;
        Check(camera != null, "SampleScene has a main camera");
        var hud = UnityEngine.Object.FindFirstObjectByType<RescueRobotVRHUD>();
        Check(hud != null, "SampleScene has an operator HUD");
        Invoke(hud, "Start");
        var presentation = hud.GetComponent<VResqPresentationControls>();
        Check(presentation != null && presentation.backgroundToggle != null && presentation.monitorToggle != null &&
            presentation.brightnessSlider != null && presentation.cameraShader != null, "Presentation controls and shader are wired");
        Invoke(presentation, "OnEnable");
        presentation.SetBackground(false);
        Check(!hud.surroundDisplay.externalWorldRoot.activeSelf, "Background toggle hides external world");
        presentation.SetBackground(true);
        presentation.SetMonitor(false);
        Check(!hud.centerVideoScreen.enabled, "Monitor toggle hides only robot image");
        presentation.SetMonitor(true);
        presentation.SetBrightness(1.5f);
        Check(Mathf.Approximately(hud.centerVideoScreen.material.GetFloat("_Brightness"), 1.5f), "Brightness reaches camera shader");
        presentation.SetBrightness(1.2f);
        Invoke(hud, "OnRobotStateReceived", new RobotStateSnapshot
        {
            left_pwm = 120, right_pwm = 120, servo_deg = 0, speed_scale = 0,
            telemetry = new TelemetryData { motor_driver = "l298n", motor_left_cmd = 0, motor_right_cmd = 0 }
        });
        Check(hud.motorLeftText.text.Contains("0") && !hud.motorLeftText.text.Contains("120"), "Reported zero motor command stays zero");
        Check(hud.servoPanText.text == "SERVO: 0 deg", "Zero servo angle is preserved");
        Check(hud.speedPercentText.text.Contains("0%"), "Zero speed scale is preserved");
        Check(hud.smartAssistText.text.Contains("0/4"), "Missing sensor coverage is reported as zero");
        Invoke(hud, "Start");
        Canvas.ForceUpdateCanvases();
        var target = new RenderTexture(1440, 900, 24);
        var capture = new Texture2D(1440, 900, TextureFormat.RGB24, false);
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            capture.ReadPixels(new Rect(0, 0, 1440, 900), 0, 0);
            capture.Apply();
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllBytes("Logs/codex-scene.png", capture.EncodeToPNG());
            Debug.Log("[VRESQ_REGRESSION] Scene capture saved to Logs/codex-scene.png");
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            UnityEngine.Object.DestroyImmediate(capture);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private static void Set(object target, string name, object value)
    {
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }

    private static object Invoke(object target, string name, params object[] args)
    {
        return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Debug.Log("[VRESQ_REGRESSION] PASS " + message);
    }

    [MenuItem("VResQ/Verify Connection and Camera Regressions")]
    public static void Run()
    {
        var root = new GameObject("RegressionFixture");
        try
        {
            var client = root.AddComponent<VResqClient>();
            client.connectOnStart = false;
            client.autoReconnect = false;
            var camera = root.AddComponent<VResqCameraReceiver>();
            Set(camera, "isStreaming", true);
            Set(camera, "hasReceivedLiveFrame", true);
            Set(camera, "_lastFrameRealtime", Time.realtimeSinceStartup);
            Check(camera.HasReceivedLiveFrame, "Fresh decoded camera frame is live");
            Set(camera, "_lastFrameRealtime", Time.realtimeSinceStartup - 3f);
            Check(!camera.HasReceivedLiveFrame, "Old camera frame expires");
            Set(camera, "_latestFrameBytes", new byte[] { 1, 2, 3 });
            Set(camera, "_hasNewFrame", true);
            camera.StopStream();
            Invoke(camera, "Update");
            Check(!camera.IsStreaming, "Explicit StopStream remains stopped after Update");
            Check(!camera.HasReceivedLiveFrame && camera.StreamFps == 0f, "Stop clears live status and FPS");
            Check(!camera.PumpFrameUpdate(), "Stop discards a pending frame");
            var testTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            byte[] jpeg = testTexture.EncodeToJPG();
            UnityEngine.Object.DestroyImmediate(testTexture);
            string metadata = "{\"schema_version\":1,\"robot_id\":\"fixture\",\"camera_id\":\"front\",\"source_session_id\":\"session\",\"frame_id\":\"session-1\",\"sequence\":1,\"capture_timestamp_ns\":1234,\"valid\":true,\"telemetry\":{\"telemetry\":{\"temp_c\":25}}}";
            string header = "--frame\r\nContent-Length: " + jpeg.Length + "\r\nX-VResQ-Meta: " +
                Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(metadata)) + "\r\n\r\n";
            var multipart = new System.IO.MemoryStream();
            byte[] headerBytes = System.Text.Encoding.ASCII.GetBytes(header);
            multipart.Write(headerBytes, 0, headerBytes.Length);
            multipart.Write(jpeg, 0, jpeg.Length);
            multipart.Position = 0;
            var parsed = new VResqMjpegReader(multipart).ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Check(parsed.jpeg.Length == jpeg.Length && parsed.metadata == metadata, "MJPEG preserves matching JPEG and metadata");
            var fragmented = new VResqMjpegReader(new FragmentedStream(multipart.ToArray()))
                .ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Check(fragmented.metadata == metadata && fragmented.jpeg.Length == jpeg.Length, "TCP fragmentation preserves frame alignment");
            bool rejectedOversize = false;
            try
            {
                new VResqMjpegReader(new System.IO.MemoryStream(System.Text.Encoding.ASCII.GetBytes(
                    "--frame\r\nContent-Length: 999999999\r\n\r\n")))
                    .ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (System.IO.InvalidDataException) { rejectedOversize = true; }
            Check(rejectedOversize, "Oversized frame length is rejected before allocation");
            bool cameraFaultVisible = false;
            try { VResqFrameMetadata.Parse("{\"valid\":false,\"error\":\"OpenCV not ready\"}"); }
            catch (FormatException error) { cameraFaultVisible = error.Message == "OpenCV not ready"; }
            Check(cameraFaultVisible, "Camera fault reason is preserved");
            Set(camera, "_latestFrameBytes", parsed.jpeg);
            Set(camera, "_latestMetadataJson", parsed.metadata);
            Set(camera, "_hasNewFrame", true);
            Check(camera.PumpFrameUpdate() && camera.DisplayedMetadata.frame_id == "session-1" &&
                camera.DisplayedMetadata.telemetry.telemetry.temp_c == 25f, "Displayed frame uses its own telemetry snapshot");
            Set(camera, "_hasNewFrame", true);
            Check(!camera.PumpFrameUpdate() && camera.RejectedFrames == 1, "Duplicate camera sequence is rejected");
            Check(VResqTelemetryQuality.Validate(new TelemetryData { temp_c = float.NaN }) != "", "Non-finite telemetry is flagged");
            Check(VResqTelemetryQuality.Validate(new TelemetryData { humidity = 500 }) != "", "Out-of-range telemetry is flagged");

            Set(client, "latencyMs", 123f);
            Set(client, "_lastStateReceivedRealtime", Time.realtimeSinceStartup - .05f);
            Invoke(client, "ProcessIncomingJsonOnMainThread", "{\"telemetry\":{},\"identity\":{\"node_name\":\"fixture\"}}", true);
            Check(Mathf.Approximately(client.LatencyMs, 123f), "State interval cannot overwrite measured RTT");
            client.Disconnect();
            client.PumpMainThreadQueue();
            Check(!client.IsConnected, "Disconnect clears connection state");
            var semaphore = (SemaphoreSlim)typeof(VResqClient).GetField("_wsSendLock", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(client);
            Check(semaphore.CurrentCount == 1, "Send semaphore has a single permit");
            Debug.Log("[VRESQ_REGRESSION] All checks passed");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
