using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using VResQ.Core;

namespace VResQ.Video
{
    /// <summary>
    /// High-performance MJPEG Camera Stream Receiver for V-ResQ Surround Cockpit.
    /// Uses HttpClient ResponseHeadersRead for zero-buffering 25-30 FPS MJPEG streaming from /video_feed.
    /// Automatically tracks VResqClient's active online robot IP (10.66.68.82, 10.66.68.10, 10.66.68.52).
    /// </summary>
    public class VResqCameraReceiver : MonoBehaviour
    {
        [Header("Stream Target")]
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
        public int FramesReceived => framesReceived;

        private Texture2D _videoTexture;
        public Texture2D VideoTexture => _videoTexture;

        private static readonly HttpClient _streamHttpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        private CancellationTokenSource _cts;
        private byte[] _latestFrameBytes;
        private readonly object _frameLock = new object();
        private bool _hasNewFrame = false;

        private string _activeStreamIp = "";
        private int _activeStreamPort = 8000;
        private int _fpsCounter = 0;
        private float _fpsTimer = 0f;

        private void Awake()
        {
            EnsureTextureCreated();
        }

        private void Start()
        {
            EnsureTextureCreated();
            ApplyTextureToTargets(_videoTexture);

            if (VResqClient.Instance != null)
            {
                VResqClient.Instance.OnRobotSwitched.AddListener(OnRobotSwitched);
            }

            SyncTargetIpAndStart();
        }

        private void EnsureTextureCreated()
        {
            if (_videoTexture == null)
            {
                _videoTexture = new Texture2D(streamWidth, streamHeight, TextureFormat.RGB24, false);
                _videoTexture.wrapMode = TextureWrapMode.Clamp;
                _videoTexture.filterMode = FilterMode.Bilinear;
                GeneratePanoramicStandbyPattern(_videoTexture);
            }
        }

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
            SyncTargetIpAndStart();
        }

        private void SyncTargetIpAndStart()
        {
            string ip = VResqClient.Instance != null ? VResqClient.Instance.robotIp : "10.66.68.82";
            int port = VResqClient.Instance != null ? VResqClient.Instance.port : 8000;

            if (_activeStreamIp == ip && _activeStreamPort == port && isStreaming) return;

            _activeStreamIp = ip;
            _activeStreamPort = port;
            streamUrl = $"http://{_activeStreamIp}:{_activeStreamPort}/video_feed";
            Debug.Log($"<color=cyan>[CameraReceiver]</color> Connecting live MJPEG stream: {streamUrl}");

            StartStream();
        }

        private void Update()
        {
            EnsureTextureCreated();

            // Always stay locked onto VResqClient's current robotIp
            if (VResqClient.Instance != null && !string.IsNullOrEmpty(VResqClient.Instance.robotIp))
            {
                if (_activeStreamIp != VResqClient.Instance.robotIp || !isStreaming)
                {
                    SyncTargetIpAndStart();
                }
            }

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
                if (_videoTexture.LoadImage(frameToLoad))
                {
                    hasReceivedLiveFrame = true;
                    _fpsCounter++;
                    framesReceived++;
                }
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
            string url = streamUrl;
            Task.Run(() => StreamLoopAsync(url, _cts.Token));
        }

        public void StopStream()
        {
            isStreaming = false;
            _cts?.Cancel();
            _cts = null;
        }

        private async Task StreamLoopAsync(string url, CancellationToken token)
        {
            byte[] readBuffer = new byte[65536];
            byte[] workBuffer = new byte[524288];
            int workLen = 0;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (string.IsNullOrEmpty(url))
                    {
                        await Task.Delay(500, token);
                        continue;
                    }

                    using (var reqCts = new CancellationTokenSource(TimeSpan.FromSeconds(4)))
                    using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, reqCts.Token))
                    using (var response = await _streamHttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, linked.Token))
                    {
                        response.EnsureSuccessStatusCode();
                        using (var stream = await response.Content.ReadAsStreamAsync())
                        {
                            workLen = 0;
                            while (!token.IsCancellationRequested)
                            {
                                int bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length, token);
                                if (bytesRead <= 0) break;

                                if (workLen + bytesRead > workBuffer.Length)
                                {
                                    workLen = 0; // Reset on overflow
                                }

                                Buffer.BlockCopy(readBuffer, 0, workBuffer, workLen, bytesRead);
                                workLen += bytesRead;

                                // Extract all complete JPEG frames (SOI 0xFF 0xD8 .. EOI 0xFF 0xD9)
                                while (true)
                                {
                                    int soi = FindMarker(workBuffer, workLen, 0xFF, 0xD8, 0);
                                    if (soi < 0)
                                    {
                                        workLen = 0;
                                        break;
                                    }

                                    int eoi = FindMarker(workBuffer, workLen, 0xFF, 0xD9, soi + 2);
                                    if (eoi < 0)
                                    {
                                        // Shift SOI to start of workBuffer if needed
                                        if (soi > 0)
                                        {
                                            int rem = workLen - soi;
                                            Buffer.BlockCopy(workBuffer, soi, workBuffer, 0, rem);
                                            workLen = rem;
                                        }
                                        break;
                                    }

                                    int jpegLen = (eoi + 2) - soi;
                                    byte[] jpegBytes = new byte[jpegLen];
                                    Buffer.BlockCopy(workBuffer, soi, jpegBytes, 0, jpegLen);

                                    lock (_frameLock)
                                    {
                                        _latestFrameBytes = jpegBytes;
                                        _hasNewFrame = true;
                                    }

                                    int leftover = workLen - (eoi + 2);
                                    if (leftover > 0)
                                    {
                                        Buffer.BlockCopy(workBuffer, eoi + 2, workBuffer, 0, leftover);
                                    }
                                    workLen = leftover;
                                }
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested) break;
                }
                catch (Exception)
                {
                    // Retry after brief delay
                }

                if (token.IsCancellationRequested) break;
                await Task.Delay(1200, token);
            }
        }

        private static int FindMarker(byte[] buffer, int length, byte b0, byte b1, int startIndex)
        {
            int limit = length - 1;
            for (int i = startIndex; i < limit; i++)
            {
                if (buffer[i] == b0 && buffer[i + 1] == b1)
                {
                    return i;
                }
            }
            return -1;
        }

        private void ApplyTextureToTargets(Texture2D tex)
        {
            if (targetRawImage != null)
            {
                targetRawImage.texture = tex;
            }
            if (targetRenderer != null && targetRenderer.sharedMaterial != null)
            {
                targetRenderer.sharedMaterial.mainTexture = tex;
            }
        }
    }
}
