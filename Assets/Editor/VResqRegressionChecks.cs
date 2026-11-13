using System;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEngine;
using VResQ.Core;
using VResQ.Video;

public static class VResqRegressionChecks
{
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
