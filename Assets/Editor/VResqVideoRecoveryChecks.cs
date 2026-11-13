using System;
using System.Reflection;
using System.Threading;
using UnityEngine;
using UnityEditor.SceneManagement;
using VResQ.Video;
using VResQ.Core;

public static class VResqVideoRecoveryChecks
{
    public static void VerifyLiveCamera()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var camera = UnityEngine.Object.FindFirstObjectByType<VResqCameraReceiver>();
        // Start from the currently failing source to exercise actual camera failover.
        typeof(VResqCameraReceiver).GetField("_preferredIp", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(camera, "192.168.1.149");
        camera.StartStream();
        try
        {
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (deadline.Elapsed.TotalSeconds < 25 && !camera.HasReceivedLiveFrame)
            {
                camera.PumpFrameUpdate();
                Thread.Sleep(40);
            }
            if (!camera.HasReceivedLiveFrame || camera.DisplayedMetadata == null)
                throw new InvalidOperationException("Live camera verification failed: " + camera.LastDataError);
            if (camera.targetRawImage == null || camera.targetRawImage.texture != camera.VideoTexture)
                throw new InvalidOperationException("Center monitor is not bound to the decoded frame");
            var surround = camera.GetComponent<VResqSurroundDisplay>();
            surround.SetViewMode(SurroundViewMode.Surround360);
            typeof(VResqSurroundDisplay).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(surround, null);
            var renderer = surround.transform.Find("Curved_Surround_Screen").GetComponent<MeshRenderer>();
            if (!renderer.gameObject.activeInHierarchy || renderer.sharedMaterial.mainTexture != camera.VideoTexture)
                throw new InvalidOperationException("VR camera background is not bound to the live texture");
            if (renderer.bounds.size.x < 1f || renderer.bounds.size.y < 1f)
                throw new InvalidOperationException("VR panorama was scaled down by the HUD canvas");
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllBytes("Logs/codex-live-camera.jpg", camera.VideoTexture.EncodeToJPG());
            var view = Camera.main;
            var target = new RenderTexture(960, 640, 24);
            var capture = new Texture2D(960, 640, TextureFormat.RGB24, false);
            var oldTarget = view.targetTexture;
            var oldActive = RenderTexture.active;
            try
            {
                view.targetTexture = target;
                view.Render();
                RenderTexture.active = target;
                capture.ReadPixels(new Rect(0, 0, 960, 640), 0, 0);
                capture.Apply();
                System.IO.File.WriteAllBytes("Logs/codex-live-vr.jpg", capture.EncodeToJPG());
            }
            finally
            {
                view.targetTexture = oldTarget;
                RenderTexture.active = oldActive;
                UnityEngine.Object.DestroyImmediate(capture);
                UnityEngine.Object.DestroyImmediate(target);
            }
            Debug.Log($"[VRESQ_LIVE_VIDEO] Panorama world bounds={renderer.bounds.size}");
            Debug.Log($"[VRESQ_LIVE_VIDEO] PASS Source={camera.DisplayedSourceIp} Frame={camera.DisplayedMetadata.frame_id} Size={camera.VideoTexture.width}x{camera.VideoTexture.height} Rejected={camera.RejectedFrames} VRTexture=True");
        }
        finally { camera.StopStream(); }
    }
}
