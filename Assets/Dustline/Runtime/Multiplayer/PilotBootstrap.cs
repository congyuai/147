using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Dustline
{
    public sealed class PilotBootstrap : MonoBehaviour
    {
        PilotHost host;
        readonly double[] samples = new double[1800];
        readonly Stopwatch clock = new Stopwatch();
        ulong lastStep, lastBytes, lastSkipped;
        int sampleCount, previousVSync, previousFrameRate;
        double lastReport;
        bool started, previousRunInBackground;
#if !UNITY_SERVER
        PilotClient client;
        PilotPanel panel;
        PilotView view;
        PlayerInputReader input;
        PilotInput next, sent;
        double sendAt;
        bool crouched, focused = true, paused;
#endif

        void Start()
        {
            started = true;
            Initialize();
        }

        void OnEnable()
        {
            if (started) Initialize();
        }

        void Initialize()
        {
            if (WantsHost()) { StartHost(); return; }
#if !UNITY_SERVER
            try
            {
                focused = Application.isFocused;
                view = new PilotView(transform);
                input = gameObject.AddComponent<PlayerInputReader>();
                panel = new PilotPanel(transform, input, Join, Ready, Back);
            }
            catch (Exception ex)
            {
                Debug.LogError("Dustline Pilot client initialization failed: " + ex.GetType().Name);
                enabled = false;
            }
#endif
        }

        static bool WantsHost()
        {
#if UNITY_SERVER
            return true;
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR
            return Array.IndexOf(Environment.GetCommandLineArgs(), "--pilot-server") >= 0;
#else
            return false;
#endif
        }

        void StartHost()
        {
            try
            {
                string address = Argument("--pilot-bind") ?? PilotAddress.Local;
                string port = Argument("--pilot-port") ?? PilotAddress.Port.ToString(CultureInfo.InvariantCulture);
                if (!PilotAddress.TryParse(address, port, out var endpoint, out bool loopback))
                    throw new ArgumentException("Use a specific IPv4 loopback or private address and a nonzero UDP port.");
                string code = Environment.GetEnvironmentVariable("DUSTLINE_PILOT_CODE");
                if (!PilotWire.ValidCode(code)) throw new ArgumentException("DUSTLINE_PILOT_CODE must contain 16–64 allowed characters.");
                string certificate = ReadPem("DUSTLINE_PILOT_CERT"), key = ReadPem("DUSTLINE_PILOT_KEY");
                bool secure = certificate != null || key != null;
                if ((!loopback || secure) && (!PilotAddress.IsCertificate(certificate) || !PilotAddress.IsPrivateKey(key)))
                    throw new ArgumentException("Set DUSTLINE_PILOT_CERT and DUSTLINE_PILOT_KEY to the test server PEM files.");
                clock.Restart();
                host = new PilotHost(endpoint, code, certificate, key, clock.Elapsed.TotalSeconds);
                lastStep = lastBytes = lastSkipped = 0;
                lastReport = sampleCount = 0;
                previousRunInBackground = Application.runInBackground;
                previousVSync = QualitySettings.vSyncCount;
                previousFrameRate = Application.targetFrameRate;
                Application.runInBackground = true;
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 60;
                Debug.Log($"Dustline Pilot ready | UDP {address}:{host.Port} | seats=2 | tick={PilotMatch.Hz}Hz | DTLS={secure}");
            }
            catch (ArgumentException ex) { HostFailed(ex.Message); }
            catch (Exception ex) { HostFailed(ex.GetType().Name + ": check local bind and pilot certificate configuration."); }
        }

        static string ReadPem(string variable)
        {
            string path = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrEmpty(path)) return null;
            using var stream = File.OpenRead(path);
            if (stream.Length <= 0 || stream.Length > PilotAddress.MaxPemLength) throw new ArgumentException(variable + " PEM file size is invalid.");
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
            return reader.ReadToEnd();
        }

        static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            if (index < 0) return null;
            if (index + 1 == args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal) || Array.LastIndexOf(args, name) != index)
                throw new ArgumentException(name + " must have one value.");
            return args[index + 1];
        }

        void HostFailed(string message)
        {
            Debug.LogError("Dustline Pilot host initialization failed: " + message);
            enabled = false;
            if (Application.isBatchMode) Application.Quit(1);
        }

        void Update()
        {
            if (host != null) { PumpHost(); return; }
#if !UNITY_SERVER
            PumpClient();
#endif
        }

        void PumpHost()
        {
            double before = clock.Elapsed.TotalSeconds;
            host.Pump(before);
            if (host.Steps == lastStep) return;
            lastStep = host.Steps;
            samples[sampleCount++] = (clock.Elapsed.TotalSeconds - before) * 1000;
            double elapsed = before - lastReport;
            if (elapsed < 30 && sampleCount < samples.Length) return;
            Array.Sort(samples, 0, sampleCount);
            double p95 = samples[(sampleCount * 95 + 99) / 100 - 1];
            double p99 = samples[(sampleCount * 99 + 99) / 100 - 1];
            Debug.Log(FormattableString.Invariant($"Dustline Pilot | {host.Phase} | tick-pump ms p95={p95:F3} p99={p99:F3} max={samples[sampleCount - 1]:F3} | skipped={host.Skipped - lastSkipped} | payload out={(host.SentBytes - lastBytes) / Math.Max(.1, elapsed):F0} B/s | managed={GC.GetTotalMemory(false) / (1024 * 1024)} MiB"));
            lastReport = before;
            lastBytes = host.SentBytes;
            lastSkipped = host.Skipped;
            sampleCount = 0;
        }

#if !UNITY_SERVER
        void Join(string address, string port, string code, string trust, string serverName)
        {
            if (client != null) return;
            if (!PilotAddress.TryParse(address, port, out var endpoint, out bool loopback))
            { panel.Status("请填写回环或私网 IPv4 地址和有效端口。"); return; }
            if (!PilotWire.ValidCode(code)) { panel.Status("房间口令需为 16–64 位字母、数字、连字符或下划线。"); return; }
            trust = trust?.Trim();
            serverName = serverName?.Trim();
            bool useTrust = !loopback || !string.IsNullOrEmpty(trust) || !string.IsNullOrEmpty(serverName);
            if (useTrust && (!PilotAddress.IsCertificate(trust) || !PilotAddress.IsServerName(serverName)))
            { panel.Status("填写可信签发证书及证书内的服务器名称。"); return; }
            try
            {
                client = new PilotClient(endpoint, code, useTrust ? trust : null, useTrust ? serverName : null, Time.realtimeSinceStartupAsDouble);
                next = sent = default;
                sendAt = 0;
                crouched = false;
                input.EndQuietWalk();
                panel.Connecting();
            }
            catch (Exception)
            {
                client?.Dispose();
                client = null;
                panel.Entry("连接未启动，请检查地址和 DTLS 配置。");
            }
        }

        void Ready()
        {
            if (client != null && !client.Ready()) panel.Status("准备消息未发送，请重试。");
        }

        void Back() => Leave("已离开房间。");

        void PumpClient()
        {
            if (panel == null) return;
            panel.Resize();
            if (client == null)
            {
                if (panel.RoomVisible && focused && !paused && input.EscapePressed) Back();
                return;
            }
            double now = Time.realtimeSinceStartupAsDouble;
            client.Pump(now);
            if (!client.Connected)
            {
                if (client.HasFrame && client.Frame.Result != PilotResult.None)
                {
                    var result = client.Frame;
                    result.Phase = PilotPhase.Results;
                    panel.Show(result, refresh: true);
                    SetGameplay(false);
                    client.Dispose();
                    client = null;
                    return;
                }
                string message = client.Rejected switch
                {
                    PilotReject.Code => "房间口令不正确。",
                    PilotReject.Unavailable => "房间已满或正在突袭。",
                    PilotReject.Protocol => "联机测试包与服务器版本不一致。",
                    _ => "连接已断开或超时，请检查私网、证书和端口。"
                };
                Leave(message);
                return;
            }
            if (focused && !paused && input.EscapePressed) { Back(); return; }
            if (!client.HasFrame) return;
            var frame = client.Frame;
            bool fighting = frame.Phase == PilotPhase.Raid && frame.Result == PilotResult.None;
            bool gameplay = fighting && focused && !paused;
            SetGameplay(gameplay);
            if (gameplay)
            {
                input.SprintAllowed = !frame.SprintExhausted;
                if (frame.SprintExhausted && input.TouchSprintLocked) input.CancelTouchSprint();
                input.SetAimAvailable(!frame.Reloading);
                next.Yaw = Mathf.DeltaAngle(0f, next.Yaw + input.Look.x);
                next.Pitch = Mathf.Clamp(next.Pitch - input.Look.y, -85f, 85f);
                if (input.FirePressed) next.Fire++;
                if (input.ReloadPressed) next.Reload++;
                if (input.InteractPressed) next.Search++;
                if (input.CrouchPressed) crouched = !crouched;
            }
            if (fighting && now >= sendAt)
            {
                sendAt = now + .9 / PilotMatch.Hz;
                next.Sequence++;
                next.Right = gameplay ? Mathf.Clamp(input.Move.x, -1f, 1f) : 0;
                next.Forward = gameplay ? Mathf.Clamp(input.Move.y, -1f, 1f) : 0;
                next.Buttons = crouched ? PilotButtons.Crouch : PilotButtons.None;
                if (gameplay)
                {
                    if (input.SprintHeld) next.Buttons |= PilotButtons.Sprint;
                    if (input.Fire) next.Buttons |= PilotButtons.Fire;
                    if (input.Aim && !frame.Reloading) next.Buttons |= PilotButtons.Aim;
                    if (input.QuietWalk) next.Buttons |= PilotButtons.Quiet;
                }
                else next.Buttons |= PilotButtons.Paused;
                if (client.SendInput(next)) sent = next;
            }
            panel.Show(frame);
            if (frame.Phase == PilotPhase.Raid || frame.First.Health != 0 || frame.Second.Health != 0)
                view.Show(frame, next.Yaw, next.Pitch, gameplay && input.Aim && !frame.Reloading, Time.unscaledDeltaTime);
        }

        void SetGameplay(bool value)
        {
            if (input == null || input.Gameplay == value) return;
            input.Gameplay = value;
            input.ReleaseTouch();
            if (!value)
            {
                next.Fire = sent.Fire;
                next.Reload = sent.Reload;
                next.Search = sent.Search;
                crouched = sent.Has(PilotButtons.Crouch);
                sendAt = 0;
            }
            Cursor.lockState = value && !input.TouchMode ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !value || input.TouchMode;
        }

        void OnApplicationFocus(bool value)
        {
            focused = value;
            if (!focused) SetGameplay(false);
        }

        void OnApplicationPause(bool value)
        {
            paused = value;
            if (paused) SetGameplay(false);
        }

        void Leave(string message)
        {
            SetGameplay(false);
            client?.Dispose();
            client = null;
            view?.Clear();
            panel?.Entry(message);
        }
#endif

        void OnDisable()
        {
            if (host != null)
            {
                Application.runInBackground = previousRunInBackground;
                QualitySettings.vSyncCount = previousVSync;
                Application.targetFrameRate = previousFrameRate;
                host.Dispose();
                host = null;
                clock.Stop();
            }
#if !UNITY_SERVER
            SetGameplay(false);
            panel?.Dispose();
            panel = null;
            client?.Dispose();
            client = null;
            view?.Dispose();
            view = null;
            if (input != null)
            {
                input.enabled = false;
                Destroy(input);
                input = null;
            }
#endif
        }
    }
}
