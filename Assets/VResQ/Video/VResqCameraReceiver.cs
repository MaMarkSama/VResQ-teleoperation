using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using VResQ.Core;

namespace VResQ.Video
{
    /// <summary>
    /// Ultra-Low-Latency Raw TCP HTTP/1.0 MJPEG Camera Stream Receiver for V-ResQ Surround Cockpit.
    /// Sends HTTP/1.0 so uvicorn streams unchunked raw multipart/x-mixed-replace JPEG frames (SOI 0xFF 0xD8 .. EOI 0xFF 0xD9)
    /// with zero HttpClient buffering. Automatically fails over to the active camera node (10.66.68.82) if a selected
    /// node has no physical camera stream attached.
    /// </summary>
    public class VResqCameraReceiver : MonoBehaviour
    {
        [Header("Stream Target")]
        public string streamUrl = "http://172.20.10.4:8000/video_feed";

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

        [SerializeField] private string activeCameraSourceIp = "172.20.10.4";
        public string ActiveCameraSourceIp => activeCameraSourceIp;

        private Texture2D _videoTexture;
        public Texture2D VideoTexture => _videoTexture;

        private CancellationTokenSource _cts;
        private byte[] _latestFrameBytes;
        private readonly object _frameLock = new object();
        private bool _hasNewFrame = false;

        private string _preferredIp = "172.20.10.4";
        private int _preferredPort = 8000;
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
                if (!string.IsNullOrEmpty(VResqClient.Instance.robotIp))
                {
                    _preferredIp = VResqClient.Instance.robotIp;
                    _preferredPort = VResqClient.Instance.port;
                }
            }

            StartStream();
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
            if (!string.IsNullOrEmpty(newIp) && newIp != _preferredIp)
            {
                _preferredIp = newIp;
                _preferredPort = VResqClient.Instance != null ? VResqClient.Instance.port : 8000;
                StartStream();
            }
        }

        private void Update()
        {
            EnsureTextureCreated();

            if (!isStreaming)
            {
                StartStream();
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

            PumpFrameUpdate();
        }

        public bool PumpFrameUpdate()
        {
            EnsureTextureCreated();

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
                    return true;
                }
            }
            return false;
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
            var token = _cts.Token;
            isStreaming = true;
            string prefIp = _preferredIp;
            int prefPort = _preferredPort;
            Task.Run(() => StreamMasterLoopAsync(prefIp, prefPort, token));
        }

        public void StopStream()
        {
            isStreaming = false;
            _cts?.Cancel();
            _cts = null;
        }

        /// <summary>
        /// Connects to the preferred robot IP first; if that robot has no camera stream (0 JPEG frames within 2.0s),
        /// automatically falls back to 10.66.68.82 -> 10.66.68.10 -> 10.66.68.52 so live video ALWAYS appears!
        /// </summary>
        private async Task StreamMasterLoopAsync(string preferredIp, int preferredPort, CancellationToken token)
        {
            int candidateIdx = 0;

            while (!token.IsCancellationRequested)
            {
                string[] candidateIps = BuildCandidateIpOrder(preferredIp);
                string ip = candidateIps[candidateIdx % candidateIps.Length];
                int port = preferredPort > 0 ? preferredPort : 8000;
                activeCameraSourceIp = ip;
                streamUrl = $"http://{ip}:{port}/video_feed";

                int framesFromThisNode = await StreamSingleNodeTcpAsync(ip, port, token);

                if (token.IsCancellationRequested) break;

                if (framesFromThisNode == 0)
                {
                    // Current node didn't produce any JPEG frames (either offline or no physical camera plugged in),
                    // immediately try the next fleet node!
                    candidateIdx++;
                    await Task.Delay(150, token);
                }
                else
                {
                    // Stream was working and disconnected; retry same node first
                    await Task.Delay(300, token);
                }
            }
        }

        private static string[] BuildCandidateIpOrder(string preferredIp)
        {
            List<string> candidates = new List<string>();
            if (!string.IsNullOrEmpty(preferredIp))
            {
                candidates.Add(preferredIp);
            }

            // Check dynamically discovered nodes from framework config
            var cfg = VResqFrameworkConfig.Instance;
            if (!object.ReferenceEquals(cfg, null) && cfg.Profile != null && cfg.Profile.fleetNodes != null)
            {
                foreach (var fn in cfg.Profile.fleetNodes)
                {
                    if (fn != null && !string.IsNullOrEmpty(fn.ipAddress) && !candidates.Contains(fn.ipAddress))
                    {
                        candidates.Add(fn.ipAddress);
                    }
                }
            }

            // Fallback list of known default nodes (including .6 hotspot DHCP)
            string[] defaults = { "172.20.10.4", "172.20.10.6", "172.20.10.3", "172.20.10.2" };
            foreach (var d in defaults)
            {
                if (!candidates.Contains(d)) candidates.Add(d);
            }

            return candidates.ToArray();
        }

        private async Task<int> StreamSingleNodeTcpAsync(string ip, int port, CancellationToken token)
        {
            int decodedFrames = 0;
            TcpClient tcp = null;

            try
            {
                tcp = new TcpClient();
                tcp.ReceiveBufferSize = 131072;
                tcp.NoDelay = true;

                // 1. Connect with 1.5s timeout
                var connectTask = tcp.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(1500, token);
                var completed = await Task.WhenAny(connectTask, timeoutTask);
                if (completed != connectTask || !tcp.Connected)
                {
                    return 0;
                }

                using (NetworkStream netStream = tcp.GetStream())
                {
                    // 2. Send HTTP/1.0 request so uvicorn DOES NOT apply Transfer-Encoding: chunked!
                    string reqStr = $"GET /video_feed HTTP/1.0\r\nHost: {ip}:{port}\r\nUser-Agent: VResQ-Unity/1.0\r\nConnection: close\r\n\r\n";
                    byte[] reqBytes = Encoding.ASCII.GetBytes(reqStr);
                    await netStream.WriteAsync(reqBytes, 0, reqBytes.Length, token);
                    await netStream.FlushAsync(token);

                    byte[] readBuffer = new byte[65536];
                    byte[] workBuffer = new byte[524288];
                    int workLen = 0;
                    var firstFrameWatch = System.Diagnostics.Stopwatch.StartNew();
                    long lastReadMs = firstFrameWatch.ElapsedMilliseconds;
                    Task sharedWatchdog = null;

                    while (!token.IsCancellationRequested)
                    {
                        int readTimeoutMs = decodedFrames == 0 ? 2200 : 3500;
                        var readTask = netStream.ReadAsync(readBuffer, 0, readBuffer.Length, token);

                        if (!readTask.IsCompleted)
                        {
                            while (!readTask.IsCompleted && !token.IsCancellationRequested)
                            {
                                if (sharedWatchdog == null || sharedWatchdog.IsCompleted)
                                {
                                    sharedWatchdog = Task.Delay(400, token);
                                }
                                await Task.WhenAny(readTask, sharedWatchdog);
                                if (!readTask.IsCompleted && (firstFrameWatch.ElapsedMilliseconds - lastReadMs) > readTimeoutMs)
                                {
                                    break;
                                }
                            }
                            if (!readTask.IsCompleted)
                            {
                                break;
                            }
                        }

                        int bytesRead = await readTask;
                        if (bytesRead <= 0) break;
                        lastReadMs = firstFrameWatch.ElapsedMilliseconds;

                        if (workLen + bytesRead > workBuffer.Length)
                        {
                            workLen = 0;
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
                                if (soi > 0)
                                {
                                    int rem = workLen - soi;
                                    Buffer.BlockCopy(workBuffer, soi, workBuffer, 0, rem);
                                    workLen = rem;
                                }
                                break;
                            }

                            int jpegLen = (eoi + 2) - soi;
                            if (jpegLen > 256)
                            {
                                byte[] jpegBytes = new byte[jpegLen];
                                Buffer.BlockCopy(workBuffer, soi, jpegBytes, 0, jpegLen);

                                lock (_frameLock)
                                {
                                    _latestFrameBytes = jpegBytes;
                                    _hasNewFrame = true;
                                }
                                decodedFrames++;
                            }

                            int leftover = workLen - (eoi + 2);
                            if (leftover > 0)
                            {
                                Buffer.BlockCopy(workBuffer, eoi + 2, workBuffer, 0, leftover);
                            }
                            workLen = leftover;
                        }

                        // If 2.2 seconds elapsed since connecting and 0 JPEG frames arrived (e.g. 10.66.68.10 header-only),
                        // break out so StreamMasterLoopAsync switches to 10.66.68.82!
                        if (decodedFrames == 0 && firstFrameWatch.ElapsedMilliseconds > 2200)
                        {
                            break;
                        }
                    }
                }
            }
            catch
            {
                // Reconnect / failover handled by StreamMasterLoopAsync
            }
            finally
            {
                if (tcp != null)
                {
                    try { tcp.Close(); } catch { }
                }
            }

            return decodedFrames;
        }

        private static int FindMarker(byte[] buffer, int length, byte b1, byte b2, int startIndex)
        {
            int limit = length - 1;
            for (int i = startIndex; i < limit; i++)
            {
                if (buffer[i] == b1 && buffer[i + 1] == b2)
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
