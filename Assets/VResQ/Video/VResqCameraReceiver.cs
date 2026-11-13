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
    /// with zero HttpClient buffering. Automatically fails over to another fleet camera if a selected
    /// node has no physical camera stream attached.
    /// </summary>
    public class VResqCameraReceiver : MonoBehaviour
    {
        [Header("Stream Target")]
        public string streamUrl = "http://192.168.1.138:8000/video_feed";

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
        public bool HasReceivedLiveFrame => hasReceivedLiveFrame && isStreaming && FrameAgeSeconds <= 2f;
        public float FrameAgeSeconds => _lastFrameRealtime < 0f ? float.PositiveInfinity : Time.realtimeSinceStartup - _lastFrameRealtime;

        [SerializeField] private float currentFps = 0f;
        public float FPS => currentFps;
        public float StreamFps => currentFps;

        [SerializeField] private int framesReceived = 0;
        public int FramesReceived => framesReceived;

        [SerializeField] private string activeCameraSourceIp = "192.168.1.138";
        public string ActiveCameraSourceIp => activeCameraSourceIp;
        private float _lastFrameRealtime = -1f;
        public VResqFrameMetadata DisplayedMetadata { get; private set; }
        public string RawFrameMetadata { get; private set; }
        public string LastDataError { get; private set; } = "";
        public int RejectedFrames { get; private set; }
        [SerializeField, TextArea(2, 6)] private string lastRejectedMetadata;
        public void ResetDiagnostics() { RejectedFrames = 0; LastDataError = ""; }
        public string FrameSyncStatus => DisplayedMetadata == null ? "UNSYNCED" : "FRAME SNAPSHOT";

        private Texture2D _videoTexture;
        public Texture2D VideoTexture => _videoTexture;

        private CancellationTokenSource _cts;
        private byte[] _latestFrameBytes;
        private string _latestMetadataJson;
        private string _latestSourceIp;
        public string DisplayedSourceIp { get; private set; }
        private readonly object _frameLock = new object();
        private bool _hasNewFrame = false;
        private volatile string _rejectedSourceIp;

        private string _preferredIp = "192.168.1.138";
        private int _preferredPort = 8000;
        private bool _automaticCameraSelection = true;
        public bool AutomaticCameraSelection => _automaticCameraSelection;

        public void SelectCameraSource(string ip, int port = 8000)
        {
            if (string.IsNullOrWhiteSpace(ip) || port < 1 || port > 65535) return;
            _automaticCameraSelection = false;
            _preferredIp = ip;
            _preferredPort = port;
            StartStream();
        }

        public void CycleCameraSource()
        {
            var config = VResqFrameworkConfig.Instance;
            var nodes = config != null ? config.Profile.fleetNodes : null;
            if (nodes == null || nodes.Length == 0) return;
            string selected = _automaticCameraSelection ? DisplayedSourceIp : _preferredIp;
            int index = Array.FindIndex(nodes, node => node != null && node.ipAddress == selected);
            for (int step = 1; step <= nodes.Length; step++)
            {
                var next = nodes[(index + step + nodes.Length) % nodes.Length];
                if (next == null || string.IsNullOrWhiteSpace(next.ipAddress)) continue;
                SelectCameraSource(next.ipAddress, next.port);
                return;
            }
        }

        public void EnableAutomaticCameraSelection()
        {
            _automaticCameraSelection = true;
            if (VResqClient.Instance != null)
            {
                _preferredIp = VResqClient.Instance.robotIp;
                _preferredPort = VResqClient.Instance.port;
            }
            StartStream();
        }
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
            if (!_automaticCameraSelection) return;
            int newPort = VResqClient.Instance != null ? VResqClient.Instance.port : 8000;
            if (!string.IsNullOrEmpty(newIp) && (newIp != _preferredIp || newPort != _preferredPort))
            {
                _preferredIp = newIp;
                _preferredPort = newPort;
                StartStream();
            }
        }

        private void Update()
        {
            EnsureTextureCreated();

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
            string metadataJson = null;
            string sourceIp = null;
            lock (_frameLock)
            {
                if (_hasNewFrame && _latestFrameBytes != null)
                {
                    frameToLoad = _latestFrameBytes;
                    metadataJson = _latestMetadataJson;
                    sourceIp = _latestSourceIp;
                    _hasNewFrame = false;
                }
            }

            if (frameToLoad != null)
            {
                VResqFrameMetadata metadata;
                try
                {
                    metadata = VResqFrameMetadata.Parse(metadataJson);
                    if (metadata != null && DisplayedMetadata != null &&
                        metadata.source_session_id == DisplayedMetadata.source_session_id &&
                        metadata.sequence <= DisplayedMetadata.sequence)
                        throw new FormatException("Duplicate or out-of-order camera frame");
                }
                catch (Exception error)
                {
                    RejectedFrames++;
                    lastRejectedMetadata = metadataJson;
                    LastDataError = error.Message;
                    _rejectedSourceIp = sourceIp;
                    hasReceivedLiveFrame = false;
                    return false;
                }
                if (_videoTexture.LoadImage(frameToLoad))
                {
                    ApplyTextureToTargets(_videoTexture);
                    DisplayedMetadata = metadata;
                    DisplayedSourceIp = sourceIp;
                    RawFrameMetadata = metadataJson;
                    LastDataError = metadata == null ? "Frame metadata unavailable" :
                        VResqTelemetryQuality.Validate(metadata.telemetry != null ? metadata.telemetry.telemetry : null);
                    hasReceivedLiveFrame = true;
                    _lastFrameRealtime = Time.realtimeSinceStartup;
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
            // Snapshot Unity configuration on the main thread before starting network work.
            string[] candidates = _automaticCameraSelection ? BuildCandidateIpOrder(prefIp) : new[] { prefIp };
            Task.Run(() => StreamMasterLoopAsync(candidates, prefPort, token));
        }

        public void StopStream()
        {
            isStreaming = false;
            _cts?.Cancel();
            _cts = null;
            hasReceivedLiveFrame = false;
            DisplayedMetadata = null;
            DisplayedSourceIp = null;
            RawFrameMetadata = null;
            _lastFrameRealtime = -1f;
            currentFps = 0f;
            _fpsCounter = 0;
            lock (_frameLock)
            {
                _latestFrameBytes = null;
                _latestMetadataJson = null;
                _hasNewFrame = false;
            }
        }

        /// <summary>
        /// Connects to the preferred robot IP first; if that robot has no camera stream (0 JPEG frames within 2.0s),
        /// tries the remaining configured fleet endpoints and known LAN/hotspot addresses.
        /// </summary>
        private async Task StreamMasterLoopAsync(string[] candidateIps, int preferredPort, CancellationToken token)
        {
            int candidateIdx = 0;

            while (!token.IsCancellationRequested)
            {
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
                    try { await Task.Delay(150, token); }
                    catch (OperationCanceledException) { break; }
                }
                else
                {
                    // Stream was working and disconnected; retry same node first
                    try { await Task.Delay(300, token); }
                    catch (OperationCanceledException) { break; }
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
            string[] defaults = { "192.168.1.138", "192.168.1.122", "192.168.1.149", "172.20.10.4", "172.20.10.6", "172.20.10.3", "172.20.10.2" };
            foreach (var d in defaults)
            {
                if (!candidates.Contains(d)) candidates.Add(d);
            }

            return candidates.ToArray();
        }

        private async Task<int> StreamSingleNodeTcpAsync(string ip, int port, CancellationToken token)
        {
            int decodedFrames = 0;
            if (_rejectedSourceIp == ip) _rejectedSourceIp = null;
            using (var tcp = new TcpClient())
            using (token.Register(() => tcp.Close()))
            {
                try
                {
                    tcp.NoDelay = true;
                    var connect = tcp.ConnectAsync(ip, port);
                    if (await Task.WhenAny(connect, Task.Delay(1500, token)) != connect)
                        throw new TimeoutException("Camera connection timed out");
                    await connect;
                    using (var stream = tcp.GetStream())
                    {
                        string request = $"GET /video_feed HTTP/1.0\r\nHost: {ip}:{port}\r\nConnection: close\r\n\r\n";
                        byte[] bytes = Encoding.ASCII.GetBytes(request);
                        await stream.WriteAsync(bytes, 0, bytes.Length, token);
                        var reader = new VResqMjpegReader(stream);
                        while (!token.IsCancellationRequested)
                        {
                            if (_rejectedSourceIp == ip) return 0;
                            var read = reader.ReadAsync(token);
                            if (await Task.WhenAny(read, Task.Delay(3500, token)) != read)
                            {
                                tcp.Close();
                                try { await read; } catch { }
                                throw new TimeoutException("Camera frame timed out");
                            }
                            var frame = await read;
                            lock (_frameLock)
                            {
                                if (token.IsCancellationRequested) return decodedFrames;
                                _latestFrameBytes = frame.jpeg;
                                _latestMetadataJson = frame.metadata;
                                _latestSourceIp = ip;
                                _hasNewFrame = true;
                            }
                            decodedFrames++;
                        }
                    }
                }
                catch (Exception error)
                {
                    if (!token.IsCancellationRequested) LastDataError = error.Message;
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
                targetRawImage.color = Color.white;
            }
            if (targetRenderer != null && targetRenderer.sharedMaterial != null)
            {
                targetRenderer.sharedMaterial.mainTexture = tex;
            }
        }
    }
}
