using System;
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
    /// High-performance MJPEG Camera Stream Receiver for V-ResQ 360°/180° Surround Display.
    /// Reads /video_feed from the active robot node and updates a Texture2D at 20-30 FPS.
    /// Automatically switches stream when VResqClient switches robots.
    /// </summary>
    public class VResqCameraReceiver : MonoBehaviour
    {
        [Header("Stream Target")]
        [Tooltip("Direct MJPEG Stream URL. If blank, auto-derived from VResqClient settings.")]
        public string streamUrl = "";

        [Header("Optional Target Displays")]
        public RawImage targetRawImage;
        public Renderer targetRenderer;

        [Header("Resolution")]
        public int streamWidth = 640;
        public int streamHeight = 480;

        [Header("Diagnostics")]
        [SerializeField] private bool isStreaming = false;
        public bool IsStreaming => isStreaming;

        [SerializeField] private bool hasReceivedLiveFrame = false;
        public bool HasReceivedLiveFrame => hasReceivedLiveFrame;

        [SerializeField] private float currentFps = 0f;
        public float FPS => currentFps;
        public float StreamFps => currentFps;

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
            _videoTexture.wrapMode = TextureWrapMode.Repeat;
            GeneratePanoramicStandbyPattern(_videoTexture);
            ApplyTextureToTargets(_videoTexture);

            if (VResqClient.Instance != null)
            {
                VResqClient.Instance.OnRobotSwitched.AddListener(OnRobotSwitched);
            }

            UpdateStreamUrl();
            StartStream();
        }

        /// <summary>
        /// Generates a sleek 360° panoramic tactical grid & horizon standby texture before live camera frames arrive.
        /// </summary>
        private void GeneratePanoramicStandbyPattern(Texture2D tex)
        {
            int w = tex.width;
            int h = tex.height;
            Color32[] pixels = new Color32[w * h];
            int horizonY = h / 2;

            for (int y = 0; y < h; y++)
            {
                bool isGridY = (y % 40 == 0) || (Mathf.Abs(y - horizonY) <= 1);
                for (int x = 0; x < w; x++)
                {
                    bool isGridX = (x % 40 == 0);
                    if (Mathf.Abs(y - horizonY) <= 1)
                    {
                        pixels[y * w + x] = new Color32(0, 220, 255, 255);
                    }
                    else if (isGridX || isGridY)
                    {
                        pixels[y * w + x] = new Color32(0, 95, 135, 255);
                    }
                    else if (y > horizonY)
                    {
                        byte grad = (byte)Mathf.Clamp(16 + ((y - horizonY) * 24 / (h / 2)), 12, 48);
                        pixels[y * w + x] = new Color32(6, (byte)(grad + 8), (byte)(grad + 24), 255);
                    }
                    else
                    {
                        byte grad = (byte)Mathf.Clamp(12 + ((horizonY - y) * 18 / (h / 2)), 10, 36);
                        pixels[y * w + x] = new Color32(8, grad, (byte)(grad + 14), 255);
                    }
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
        }

        private void OnRobotSwitched(string newIp)
        {
            Debug.Log($"<color=cyan>[CameraReceiver]</color> Switching surround camera stream to {newIp}...");
            hasReceivedLiveFrame = false;
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
            if (targetRawImage != null && targetRawImage.texture != _videoTexture)
            {
                targetRawImage.texture = _videoTexture;
            }

            _fpsTimer += Time.deltaTime;
            if (_fpsTimer >= 1.0f)
            {
                currentFps = _fpsCounter / _fpsTimer;
                _fpsCounter = 0;
                _fpsTimer = 0f;
            }

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
                hasReceivedLiveFrame = true;
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
                    UpdateStreamUrl();
                    request = (HttpWebRequest)WebRequest.Create(streamUrl);
                    request.Timeout = 3500;
                    request.ReadWriteTimeout = 4500;

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
                                ms.SetLength(0);
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
                    await Task.Delay(1800, token);
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
