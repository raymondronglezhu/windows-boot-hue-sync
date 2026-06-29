using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;

namespace SmartHomeAutomation
{
    internal sealed class ServiceSettings
    {
        public string BridgeIp;
        public string BridgeId;
        public string Username;
        public string RoomId;
        public string RoomName;
        public string SceneId;
        public string SceneName;
        public Dictionary<string, bool> Triggers;
    }

    internal sealed class HueController
    {
        private const string DiscoveryUrl = "https://discovery.meethue.com/";
        private const string SettingsFileName = "service.json";
        private const string LogFileName = "service.log";
        private static readonly string InstallDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        private static readonly string SettingsPath = Path.Combine(InstallDir, SettingsFileName);
        private static readonly string LogPath = Path.Combine(InstallDir, LogFileName);
        private static readonly object LogLock = new object();

        // JavaScriptSerializer is deprecated but ships with .NET Framework 4.x and
        // keeps the build dependency-free (compiles with just csc.exe, no NuGet/SDK).
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public void Log(string scope, string message)
        {
            try
            {
                lock (LogLock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                    using (var stream = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var writer = new StreamWriter(stream))
                    {
                        writer.WriteLine(
                            string.Format("[{0}] [{1}] {2}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), scope, message)
                        );
                    }
                }
            }
            catch
            {
            }
        }

        public bool IsTriggerEnabled(string trigger)
        {
            try
            {
                var settings = LoadSettings();
                if (settings.Triggers == null)
                {
                    return true;
                }
                bool enabled;
                return !settings.Triggers.TryGetValue(trigger, out enabled) || enabled;
            }
            catch
            {
                return true;
            }
        }

        public bool TryApplyScene(string scope, int maxAttempts, int retryDelaySeconds)
        {
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    Log(scope, string.Format("Attempt {0}/{1}", attempt, maxAttempts));
                    var settings = LoadSettings();
                    EnsureRoomTarget(settings);

                    Dictionary<string, object> body;
                    string description;
                    if (string.IsNullOrWhiteSpace(settings.SceneId))
                    {
                        body = new Dictionary<string, object> { { "on", true } };
                        description = "last used scene";
                    }
                    else
                    {
                        body = new Dictionary<string, object> { { "scene", settings.SceneId } };
                        description = string.Format("scene '{0}'", settings.SceneName ?? settings.SceneId);
                    }

                    InvokeApiJson(
                        settings,
                        "PUT",
                        string.Format("/groups/{0}/action", settings.RoomId),
                        _json.Serialize(body)
                    );
                    Log(scope, string.Format("Applied {0} to '{1}'", description, settings.RoomName ?? settings.RoomId));
                    return true;
                }
                catch (Exception ex)
                {
                    Log(scope, "Attempt failed: " + ex.Message);
                    if (attempt < maxAttempts)
                    {
                        Thread.Sleep(TimeSpan.FromSeconds(retryDelaySeconds));
                    }
                }
            }

            Log(scope, "Failed after all attempts");
            return false;
        }

        public bool TryTurnRoomOff(string scope, int maxAttempts, int retryDelaySeconds)
        {
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    Log(scope, string.Format("Attempt {0}/{1}", attempt, maxAttempts));
                    var settings = LoadSettings();
                    EnsureRoomTarget(settings);
                    InvokeApiJson(
                        settings,
                        "PUT",
                        string.Format("/groups/{0}/action", settings.RoomId),
                        _json.Serialize(new Dictionary<string, object> { { "on", false } })
                    );
                    Log(scope, string.Format("Turned off '{0}'", settings.RoomName ?? settings.RoomId));
                    return true;
                }
                catch (Exception ex)
                {
                    Log(scope, "Attempt failed: " + ex.Message);
                    if (attempt < maxAttempts)
                    {
                        Thread.Sleep(TimeSpan.FromSeconds(retryDelaySeconds));
                    }
                }
            }

            Log(scope, "Failed after all attempts");
            return false;
        }

        private ServiceSettings LoadSettings()
        {
            if (!File.Exists(SettingsPath))
            {
                throw new InvalidOperationException("Service settings not found at " + SettingsPath);
            }

            var raw = _json.Deserialize<Dictionary<string, object>>(File.ReadAllText(SettingsPath));
            return new ServiceSettings
            {
                BridgeIp = GetString(raw, "bridge_ip"),
                BridgeId = GetString(raw, "bridge_id"),
                Username = GetString(raw, "username"),
                RoomId = GetString(raw, "room_id"),
                RoomName = GetString(raw, "room_name"),
                SceneId = GetString(raw, "scene_id"),
                SceneName = GetString(raw, "scene_name"),
                Triggers = ParseTriggers(raw),
            };
        }

        private static Dictionary<string, bool> ParseTriggers(Dictionary<string, object> raw)
        {
            var result = new Dictionary<string, bool>();
            object value;
            if (!raw.TryGetValue("triggers", out value))
            {
                return result;
            }

            var dict = value as Dictionary<string, object>;
            if (dict == null)
            {
                return result;
            }

            foreach (var kvp in dict)
            {
                if (kvp.Value is bool)
                {
                    result[kvp.Key] = (bool)kvp.Value;
                }
            }
            return result;
        }

        private static void EnsureRoomTarget(ServiceSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.RoomId))
            {
                throw new InvalidOperationException("room_id missing from service settings");
            }
            if (string.IsNullOrWhiteSpace(settings.BridgeIp) || string.IsNullOrWhiteSpace(settings.Username))
            {
                throw new InvalidOperationException("bridge_ip or username missing from service settings");
            }
        }

        private string BuildBaseApi(ServiceSettings settings)
        {
            return string.Format("http://{0}/api/{1}", settings.BridgeIp, settings.Username);
        }

        private object InvokeApiJson(ServiceSettings settings, string method, string apiSuffix, string body)
        {
            var url = BuildBaseApi(settings) + apiSuffix;

            try
            {
                return InvokeJson(method, url, body);
            }
            catch (WebException ex)
            {
                if (!ShouldTryBridgeRefresh(ex))
                {
                    throw;
                }

                if (!TryRefreshBridgeIp(settings))
                {
                    throw;
                }

                return InvokeJson(method, BuildBaseApi(settings) + apiSuffix, body);
            }
        }

        private object InvokeJson(string method, string url, string body)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.ContentType = "application/json";
            request.Timeout = 5000;
            request.ReadWriteTimeout = 5000;

            if (!string.IsNullOrEmpty(body))
            {
                using (var writer = new StreamWriter(request.GetRequestStream()))
                {
                    writer.Write(body);
                }
            }

            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream()))
            {
                return _json.DeserializeObject(reader.ReadToEnd());
            }
        }

        private bool TryRefreshBridgeIp(ServiceSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.BridgeId))
            {
                return false;
            }

            var discoveredIp = DiscoverBridgeIpById(settings.BridgeId);
            if (string.IsNullOrWhiteSpace(discoveredIp))
            {
                return false;
            }

            if (string.Equals(settings.BridgeIp, discoveredIp, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var previousIp = settings.BridgeIp;
            settings.BridgeIp = discoveredIp;
            SaveSettings(settings);
            Log("service", string.Format("Updated bridge IP from '{0}' to '{1}' via discovery", previousIp, discoveredIp));
            return true;
        }

        private string DiscoverBridgeIpById(string bridgeId)
        {
            // JavaScriptSerializer.DeserializeObject returns object[] for JSON arrays.
            var payload = InvokeJson("GET", DiscoveryUrl, null) as object[];
            if (payload == null)
            {
                return null;
            }

            foreach (var item in payload)
            {
                var match = MatchBridgeIp(item, bridgeId);
                if (match != null)
                {
                    return match;
                }
            }
            return null;
        }

        private string MatchBridgeIp(object item, string bridgeId)
        {
            var bridge = item as Dictionary<string, object>;
            if (bridge == null)
            {
                return null;
            }

            var discoveredId = GetString(bridge, "id");
            if (string.Equals(discoveredId, bridgeId, StringComparison.OrdinalIgnoreCase))
            {
                return GetString(bridge, "internalipaddress");
            }
            return null;
        }

        private void SaveSettings(ServiceSettings settings)
        {
            var raw = new Dictionary<string, object>
            {
                { "bridge_ip", settings.BridgeIp },
                { "bridge_id", settings.BridgeId },
                { "username", settings.Username },
                { "room_id", settings.RoomId },
                { "room_name", settings.RoomName },
                { "scene_id", settings.SceneId },
                { "scene_name", settings.SceneName },
                { "triggers", settings.Triggers ?? new Dictionary<string, bool>() },
            };

            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, _json.Serialize(raw));
        }

        private bool ShouldTryBridgeRefresh(WebException ex)
        {
            switch (ex.Status)
            {
                case WebExceptionStatus.ConnectFailure:
                case WebExceptionStatus.ConnectionClosed:
                case WebExceptionStatus.NameResolutionFailure:
                case WebExceptionStatus.ProxyNameResolutionFailure:
                case WebExceptionStatus.ReceiveFailure:
                case WebExceptionStatus.SendFailure:
                case WebExceptionStatus.Timeout:
                    return true;
                default:
                    return false;
            }
        }

        private static string GetString(Dictionary<string, object> dictionary, string key)
        {
            object value;
            if (dictionary.TryGetValue(key, out value) && value != null)
            {
                return value.ToString();
            }
            return null;
        }
    }

    internal static class NativeServiceHost
    {
        private const int SERVICE_WIN32_OWN_PROCESS = 0x00000010;
        private const int SERVICE_STOPPED = 0x00000001;
        private const int SERVICE_START_PENDING = 0x00000002;
        private const int SERVICE_STOP_PENDING = 0x00000003;
        private const int SERVICE_RUNNING = 0x00000004;

        private const int SERVICE_ACCEPT_STOP = 0x00000001;
        private const int SERVICE_ACCEPT_SHUTDOWN = 0x00000004;
        private const int SERVICE_ACCEPT_POWEREVENT = 0x00000040;
        private const int SERVICE_ACCEPT_PRESHUTDOWN = 0x00000100;

        private const int SERVICE_CONTROL_STOP = 0x00000001;
        private const int SERVICE_CONTROL_SHUTDOWN = 0x00000005;
        private const int SERVICE_CONTROL_POWEREVENT = 0x0000000D;
        private const int SERVICE_CONTROL_PRESHUTDOWN = 0x0000000F;

        private const int PBT_APMSUSPEND = 0x00000004;
        private const int PBT_APMRESUMESUSPEND = 0x00000007;
        private const int PBT_APMRESUMEAUTOMATIC = 0x00000012;
        private const int PBT_POWERSETTINGCHANGE = 0x00008013;

        private const int DEVICE_NOTIFY_SERVICE_HANDLE = 0x00000001;
        private const int DISPLAY_STATE_OFF = 0;
        private const int DISPLAY_STATE_ON = 1;
        private const int DISPLAY_STATE_DIMMED = 2;

        private const int NO_ERROR = 0;
        private const int SERVICE_CONFIG_PRESHUTDOWN_INFO = 7;
        private const int PRESHUTDOWN_TIMEOUT_MS = 20000;
        private const int WAKE_DEBOUNCE_SECONDS = 15;
        private const int INITIAL_DISPLAY_ON_IGNORE_SECONDS = 20;
        private const string SERVICE_NAME = "HuePowerService";
        private static readonly Guid GuidConsoleDisplayState = new Guid("6FE69556-704A-47A0-8F24-C28D936FDA47");

        private static readonly HueController Controller = new HueController();
        private static readonly ManualResetEvent StopEvent = new ManualResetEvent(false);
        private static readonly ServiceMainFunction MainCallback = ServiceMain;
        private static readonly HandlerEx HandlerCallback = ServiceControlHandler;
        private static readonly object WakeLock = new object();
        private static readonly object DisplayStateLock = new object();

        private static IntPtr _statusHandle = IntPtr.Zero;
        private static IntPtr _displayNotificationHandle = IntPtr.Zero;
        private static ServiceStatus _status;
        private static StopReason _stopReason = StopReason.None;
        private static DateTime _lastWakeHandledUtc = DateTime.MinValue;
        private static DateTime _serviceStartedUtc = DateTime.MinValue;
        private static int _lastDisplayState = -1;

        public static void Run()
        {
            var serviceTable = new ServiceTableEntry[]
            {
                new ServiceTableEntry { lpServiceName = SERVICE_NAME, lpServiceProc = MainCallback },
                new ServiceTableEntry { lpServiceName = null, lpServiceProc = null }
            };

            if (!StartServiceCtrlDispatcher(serviceTable))
            {
                throw new InvalidOperationException("Could not start service dispatcher");
            }
        }

        public static void ConfigurePreshutdownTimeout()
        {
            var scm = OpenSCManager(null, null, 0x0001);
            if (scm == IntPtr.Zero)
            {
                throw new InvalidOperationException("OpenSCManager failed");
            }

            try
            {
                var service = OpenService(scm, SERVICE_NAME, 0x0002);
                if (service == IntPtr.Zero)
                {
                    throw new InvalidOperationException("OpenService failed");
                }

                try
                {
                    var info = new ServicePreshutdownInfo { dwPreshutdownTimeout = PRESHUTDOWN_TIMEOUT_MS };
                    if (!ChangeServiceConfig2(service, SERVICE_CONFIG_PRESHUTDOWN_INFO, ref info))
                    {
                        throw new InvalidOperationException("ChangeServiceConfig2 failed");
                    }
                }
                finally
                {
                    CloseServiceHandle(service);
                }
            }
            finally
            {
                CloseServiceHandle(scm);
            }
        }

        private static void ServiceMain(int argc, IntPtr argv)
        {
            _statusHandle = RegisterServiceCtrlHandlerEx(SERVICE_NAME, HandlerCallback, IntPtr.Zero);
            if (_statusHandle == IntPtr.Zero)
            {
                return;
            }

            _status = new ServiceStatus
            {
                dwServiceType = SERVICE_WIN32_OWN_PROCESS,
                dwCurrentState = SERVICE_START_PENDING,
                dwControlsAccepted = 0,
                dwWin32ExitCode = NO_ERROR,
                dwServiceSpecificExitCode = 0,
                dwCheckPoint = 0,
                dwWaitHint = 10000
            };
            SetServiceStatus(_statusHandle, ref _status);

            Controller.Log("service", "Native service starting");
            _serviceStartedUtc = DateTime.UtcNow;

            if (Controller.IsTriggerEnabled("boot"))
            {
                var startupThread = new Thread(delegate ()
                {
                    Controller.TryApplyScene("startup", 20, 3);
                });
                startupThread.IsBackground = true;
                startupThread.Start();
            }
            else
            {
                Controller.Log("service", "Boot trigger disabled in service.json");
            }

            _status.dwCurrentState = SERVICE_RUNNING;
            _status.dwControlsAccepted = SERVICE_ACCEPT_STOP | SERVICE_ACCEPT_SHUTDOWN | SERVICE_ACCEPT_PRESHUTDOWN | SERVICE_ACCEPT_POWEREVENT;
            _status.dwWaitHint = 0;
            SetServiceStatus(_statusHandle, ref _status);

            RegisterDisplayNotifications();

            StopEvent.WaitOne();

            if ((_stopReason == StopReason.Shutdown || _stopReason == StopReason.Preshutdown)
                && Controller.IsTriggerEnabled("shutdown"))
            {
                Controller.TryTurnRoomOff("shutdown", 6, 2);
            }

            UnregisterDisplayNotifications();

            _status.dwCurrentState = SERVICE_STOPPED;
            _status.dwControlsAccepted = 0;
            _status.dwWaitHint = 0;
            SetServiceStatus(_statusHandle, ref _status);
        }

        private static int ServiceControlHandler(int control, int eventType, IntPtr eventData, IntPtr context)
        {
            switch (control)
            {
                case SERVICE_CONTROL_STOP:
                    Controller.Log("service", "Stop received");
                    _stopReason = StopReason.Stop;
                    BeginStopPending();
                    StopEvent.Set();
                    break;
                case SERVICE_CONTROL_SHUTDOWN:
                    Controller.Log("service", "Shutdown received");
                    _stopReason = StopReason.Shutdown;
                    BeginStopPending();
                    StopEvent.Set();
                    break;
                case SERVICE_CONTROL_PRESHUTDOWN:
                    Controller.Log("service", "Preshutdown received");
                    _stopReason = StopReason.Preshutdown;
                    BeginStopPending();
                    StopEvent.Set();
                    break;
                case SERVICE_CONTROL_POWEREVENT:
                    HandlePowerEvent(eventType, eventData);
                    break;
            }

            return NO_ERROR;
        }

        private static void HandlePowerEvent(int eventType, IntPtr eventData)
        {
            switch (eventType)
            {
                case PBT_APMSUSPEND:
                    Controller.Log("service", "Sleep received");
                    if (!Controller.IsTriggerEnabled("sleep"))
                    {
                        Controller.Log("service", "Sleep trigger disabled in service.json");
                        return;
                    }
                    Controller.TryTurnRoomOff("sleep", 3, 1);
                    break;
                case PBT_APMRESUMEAUTOMATIC:
                case PBT_APMRESUMESUSPEND:
                    if (!ShouldHandleWakeEvent())
                    {
                        Controller.Log("service", "Wake skipped due to debounce");
                        return;
                    }

                    if (!Controller.IsTriggerEnabled("wake"))
                    {
                        Controller.Log("service", "Wake trigger disabled in service.json");
                        return;
                    }

                    Controller.Log("service", "Wake received");
                    var wakeThread = new Thread(delegate ()
                    {
                        Controller.TryApplyScene("wake", 12, 2);
                    });
                    wakeThread.IsBackground = true;
                    wakeThread.Start();
                    break;
                case PBT_POWERSETTINGCHANGE:
                    HandlePowerSettingChange(eventData);
                    break;
            }
        }

        private static void HandlePowerSettingChange(IntPtr eventData)
        {
            if (eventData == IntPtr.Zero)
            {
                return;
            }

            var setting = (PowerBroadcastSetting)Marshal.PtrToStructure(eventData, typeof(PowerBroadcastSetting));
            if (setting.PowerSetting != GuidConsoleDisplayState)
            {
                return;
            }

            var state = setting.Data;
            if (!ShouldHandleDisplayState(state))
            {
                Controller.Log("service", string.Format("Display state {0} skipped as duplicate", state));
                return;
            }

            switch (state)
            {
                case DISPLAY_STATE_OFF:
                    Controller.Log("service", "Display off received");
                    if (!Controller.IsTriggerEnabled("display_off"))
                    {
                        Controller.Log("service", "Display-off trigger disabled in service.json");
                        return;
                    }
                    Controller.TryTurnRoomOff("display-off", 2, 1);
                    break;
                case DISPLAY_STATE_ON:
                    if ((DateTime.UtcNow - _serviceStartedUtc).TotalSeconds < INITIAL_DISPLAY_ON_IGNORE_SECONDS)
                    {
                        Controller.Log("service", "Initial display on ignored during startup window");
                        return;
                    }

                    if (!ShouldHandleWakeEvent())
                    {
                        Controller.Log("service", "Display on skipped due to wake debounce");
                        return;
                    }

                    if (!Controller.IsTriggerEnabled("display_on"))
                    {
                        Controller.Log("service", "Display-on trigger disabled in service.json");
                        return;
                    }

                    Controller.Log("service", "Display on received");
                    var displayOnThread = new Thread(delegate ()
                    {
                        Controller.TryApplyScene("display-on", 12, 2);
                    });
                    displayOnThread.IsBackground = true;
                    displayOnThread.Start();
                    break;
                case DISPLAY_STATE_DIMMED:
                    Controller.Log("service", "Display dimmed received");
                    break;
            }
        }

        private static bool ShouldHandleWakeEvent()
        {
            lock (WakeLock)
            {
                var now = DateTime.UtcNow;
                if ((now - _lastWakeHandledUtc).TotalSeconds < WAKE_DEBOUNCE_SECONDS)
                {
                    return false;
                }

                _lastWakeHandledUtc = now;
                return true;
            }
        }

        private static bool ShouldHandleDisplayState(int state)
        {
            lock (DisplayStateLock)
            {
                if (_lastDisplayState == state)
                {
                    return false;
                }

                _lastDisplayState = state;
                return true;
            }
        }

        private static void RegisterDisplayNotifications()
        {
            if (_statusHandle == IntPtr.Zero || _displayNotificationHandle != IntPtr.Zero)
            {
                return;
            }

            var powerSettingGuid = GuidConsoleDisplayState;
            _displayNotificationHandle = RegisterPowerSettingNotification(_statusHandle, ref powerSettingGuid, DEVICE_NOTIFY_SERVICE_HANDLE);
            if (_displayNotificationHandle == IntPtr.Zero)
            {
                Controller.Log("service", "Failed to register display power notifications");
            }
            else
            {
                Controller.Log("service", "Registered display power notifications");
            }
        }

        private static void UnregisterDisplayNotifications()
        {
            if (_displayNotificationHandle == IntPtr.Zero)
            {
                return;
            }

            UnregisterPowerSettingNotification(_displayNotificationHandle);
            _displayNotificationHandle = IntPtr.Zero;
        }

        private static void BeginStopPending()
        {
            _status.dwCurrentState = SERVICE_STOP_PENDING;
            _status.dwControlsAccepted = 0;
            _status.dwCheckPoint = 1;
            _status.dwWaitHint = PRESHUTDOWN_TIMEOUT_MS;
            SetServiceStatus(_statusHandle, ref _status);
        }

        private enum StopReason
        {
            None,
            Stop,
            Shutdown,
            Preshutdown
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceStatus
        {
            public int dwServiceType;
            public int dwCurrentState;
            public int dwControlsAccepted;
            public int dwWin32ExitCode;
            public int dwServiceSpecificExitCode;
            public int dwCheckPoint;
            public int dwWaitHint;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ServicePreshutdownInfo
        {
            public int dwPreshutdownTimeout;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PowerBroadcastSetting
        {
            public Guid PowerSetting;
            public int DataLength;
            public int Data;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ServiceTableEntry
        {
            public string lpServiceName;
            public ServiceMainFunction lpServiceProc;
        }

        private delegate void ServiceMainFunction(int argc, IntPtr argv);
        private delegate int HandlerEx(int control, int eventType, IntPtr eventData, IntPtr context);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool StartServiceCtrlDispatcher([In] ServiceTableEntry[] serviceTable);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr RegisterServiceCtrlHandlerEx(string serviceName, HandlerEx callback, IntPtr context);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetServiceStatus(IntPtr statusHandle, ref ServiceStatus serviceStatus);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenSCManager(string machineName, string databaseName, int desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenService(IntPtr scmHandle, string serviceName, int desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool ChangeServiceConfig2(IntPtr serviceHandle, int infoLevel, ref ServicePreshutdownInfo info);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CloseServiceHandle(IntPtr handle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid powerSettingGuid, int flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterPowerSettingNotification(IntPtr handle);
    }

    internal static class Program
    {
        private static int Main(string[] args)
        {
            // Enable TLS 1.2 for outbound HTTPS. .NET Framework 4.x defaults to
            // SSL3/TLS1.0, which discovery.meethue.com rejects, breaking bridge IP
            // rediscovery when the router reassigns the bridge a new DHCP lease.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            var controller = new HueController();

            if (args.Length > 0)
            {
                if (args[0] == "--startup-once")
                {
                    return controller.TryApplyScene("startup", 20, 3) ? 0 : 1;
                }

                if (args[0] == "--shutdown-once")
                {
                    return controller.TryTurnRoomOff("shutdown", 6, 2) ? 0 : 1;
                }

                if (args[0] == "--sleep-once")
                {
                    return controller.TryTurnRoomOff("sleep", 3, 1) ? 0 : 1;
                }

                if (args[0] == "--wake-once")
                {
                    return controller.TryApplyScene("wake", 12, 2) ? 0 : 1;
                }

                if (args[0] == "--display-off-once")
                {
                    return controller.TryTurnRoomOff("display-off", 2, 1) ? 0 : 1;
                }

                if (args[0] == "--display-on-once")
                {
                    return controller.TryApplyScene("display-on", 12, 2) ? 0 : 1;
                }

                if (args[0] == "--configure-preshutdown")
                {
                    NativeServiceHost.ConfigurePreshutdownTimeout();
                    return 0;
                }
            }

            NativeServiceHost.Run();
            return 0;
        }
    }
}
