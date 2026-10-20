using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using VResQ.Core;

namespace VResQ.Video
{
    /// <summary>
    /// High-performance MJPEG Camera Stream Receiver for Unity VR Cockpit.
    /// Reads /video_feed from Jetson Nano and updates a Texture2D at 20-30 FPS.
    /// Automatically switches stream when VResqClient switches robots.
    /// </summary>
    public class VResqCameraReceiver : MonoBehaviour
    {
        [Header("Stream Target")]
        [Tooltip("Direct MJPEG Stream URL. If blank, auto-derived from VResqClient settings.")]
        public string streamUrl = "";

        [Header("Target Displays")]
        [Tooltip("Canvas RawImage to display the live video feed")]
        public RawImage targetRawImage;

        [Tooltip("Optional 3D Mesh Renderer (Quad or Curved Cockpit Display)")]
        public Renderer targetRenderer;

        [Header("Resolution")]
        public int streamWidth = 640;
        public int streamHeight = 480;

        [Header("Diagnostics")]
        [SerializeField] private bool isStreaming = false;
        public bool IsStreaming => isStreaming;

        [SerializeField] private float currentFps = 0f;
        public float FPS => currentFps;

        [SerializeField] private int framesReceived = 0;

        private Texture2D _videoTexture;
        public Texture2D VideoTexture => _videoTexture;

        private CancellationTokenSource _cts;
        private byte[] _latestFrameBytes;
        private readonly object _frameLock = new object();
        private bool _hasNewFrame = false;

        private int _fpsCounter = 0;
        private float _fpsTimer = 0f;

        private void Start()
        {
            _videoTexture = new Texture2D(streamWidth, streamHeight, TextureFormat.RGB24, false);
            FillPlaceholderTexture(_videoTexture, new Color32(10, 20, 35, 255));
            ApplyTextureToTargets(_videoTexture);

            if (VResqClient.Instance != null)
            {
                VResqClient.Instance.OnRobotSwitched.AddListener(OnRobotSwitched);
            }

            UpdateStreamUrl();
            StartStream();
        }

        private void FillPlaceholderTexture(Texture2D tex, Color32 col)
        {
            Color32[] pixels = new Color32[tex.width * tex.height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = col;
            tex.SetPixels32(pixels);
            tex.Apply();
        }

        private void OnRobotSwitched(string newIp)
        {
            Debug.Log($"<color=cyan>[CameraReceiver]</color> Switching camera stream to {newIp}...");
            StopStream();
            UpdateStreamUrl();
            StartStream();
        }

        private void UpdateStreamUrl()
        {
            string ip = VResqClient.Instance != null ? VResqClient.Instance.robotIp : "10.66.68.52";
            int port = VResqClient.Instance != null ? VResqClient.Instance.port : 8000;
            streamUrl = $"http://{ip}:{port}/video_feed";
        }

        private void Update()
        {
            // Auto bind texture if assigned dynamically
            if (targetRawImage != null && targetRawImage.texture != _videoTexture)
            {
                targetRawImage.texture = _videoTexture;
                targetRawImage.color = Color.white;
            }

            // FPS Calculation
            _fpsTimer += Time.deltaTime;
            if (_fpsTimer >= 1.0f)
            {
                currentFps = _fpsCounter / _fpsTimer;
                _fpsCounter = 0;
                _fpsTimer = 0f;
            }

            // Consume decoded frame onto Texture2D on Unity main thread
            byte[] frameToLoad = null;
            lock (_frameLock)
            {
                if (_hasNewFrame && _latestFrameBytes != null)
                {
                    frameToLoad = _latestFrameBytes;
                    _hasNewFrame = false;
                }
            }

            if (frameToLoad != null)
            {
                _videoTexture.LoadImage(frameToLoad);
                _fpsCounter++;
                framesReceived++;
            }
        }

        private void OnDestroy()
        {
            if (VResqClient.Instance != null)
            {
                VResqClient.Instance.OnRobotSwitched.RemoveListener(OnRobotSwitched);
            }
            StopStream();
            if (_videoTexture != null)
            {
                Destroy(_videoTexture);
            }
        }

        public void StartStream()
        {
            StopStream();
            _cts = new CancellationTokenSource();
            isStreaming = true;
            Task.Run(() => StreamLoopAsync(_cts.Token));
        }

        public void StopStream()
        {
            isStreaming = false;
            _cts?.Cancel();
            _cts = null;
        }

        private async Task StreamLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                HttpWebRequest request = null;
                HttpWebResponse response = null;
                Stream stream = null;

                try
                {
                    request = (HttpWebRequest)WebRequest.Create(streamUrl);
                    request.Timeout = 4000;
                    request.ReadWriteTimeout = 5000;

                    using (token.Register(() => request.Abort()))
                    {
                        response = (HttpWebResponse)await request.GetResponseAsync();
                    }

                    stream = response.GetResponseStream();
                    byte[] buffer = new byte[65536];
                    using (var ms = new MemoryStream())
                    {
                        while (!token.IsCancellationRequested)
                        {
                            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                            if (bytesRead <= 0) break;

                            ms.Write(buffer, 0, bytesRead);
                            byte[] data = ms.ToArray();

                            // Locate JPEG delimiters: SOI (0xFF, 0xD8) and EOI (0xFF, 0xD9)
                            int soi = FindBytes(data, new byte[] { 0xFF, 0xD8 }, 0);
                            if (soi >= 0)
                            {
                                int eoi = FindBytes(data, new byte[] { 0xFF, 0xD9 }, soi + 2);
                                if (eoi >= 0)
                                {
                                    int jpegLength = (eoi + 2) - soi;
                                    byte[] jpegBytes = new byte[jpegLength];
                                    Buffer.BlockCopy(data, soi, jpegBytes, 0, jpegLength);

                                    lock (_frameLock)
                                    {
                                        _latestFrameBytes = jpegBytes;
                                        _hasNewFrame = true;
                                    }

                                    // Retain leftover bytes
                                    int remaining = data.Length - (eoi + 2);
                                    ms.SetLength(0);
                                    if (remaining > 0)
                                    {
                                        ms.Write(data, eoi + 2, remaining);
                                    }
                                }
                            }

                            if (ms.Length > 2000000)
                            {
                                ms.SetLength(0); // Protect against runaway memory
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // If stream fails, wait briefly before reconnecting
                    await Task.Delay(2000, token);
                }
                finally
                {
                    try { stream?.Dispose(); } catch { }
                    try { response?.Dispose(); } catch { }
                }

                if (token.IsCancellationRequested) break;
                await Task.Delay(1000, token);
            }
        }

        private static int FindBytes(byte[] source, byte[] pattern, int startIndex)
        {
            if (source == null || pattern == null || source.Length < pattern.Length) return -1;
            for (int i = startIndex; i <= source.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (source[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        private void ApplyTextureToTargets(Texture2D tex)
        {
            if (targetRawImage != null)
            {
                targetRawImage.texture = tex;
            }
            if (targetRenderer != null && targetRenderer.material != null)
            {
                targetRenderer.material.mainTexture = tex;
            }
        }
    }
}
