using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;

namespace SmartHomeAutomation
{
    internal sealed class HueController
    {
        private const string Root = @"C:\Users\Raymond\Documents\Smart_Home";
        private const string RoomName = "Living room";
        private const string SceneName = "Bright";
        private static readonly string ConfigPath = Path.Combine(Root, ".hue-agent", "config.json");
        private static readonly string LogPath = Path.Combine(Root, ".hue-agent", "hue-power-service.log");
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public void Log(string scope, string message)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
            File.AppendAllText(
                LogPath,
                string.Format("[{0}] [{1}] {2}{3}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), scope, message, Environment.NewLine)
            );
        }

        public bool TryApplyStartupScene(int maxAttempts, int retryDelaySeconds)
        {
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    Log("startup", string.Format("Attempt {0}/{1}", attempt, maxAttempts));
                    var config = LoadConfig();
                    var state = GetState(config);
                    var roomId = ResolveRoomId(state);
                    var sceneId = ResolveSceneId(state, roomId);
                    InvokeJson(
                        "PUT",
                        string.Format("{0}/groups/{1}/action", BuildBaseApi(config), roomId),
                        _json.Serialize(new Dictionary<string, object> { { "scene", sceneId } })
                    );
                    Log("startup", string.Format("Applied scene '{0}' to '{1}'", SceneName, RoomName));
                    return true;
                }
                catch (Exception ex)
                {
                    Log("startup", "Attempt failed: " + ex.Message);
                    if (attempt < maxAttempts)
                    {
                        Thread.Sleep(TimeSpan.FromSeconds(retryDelaySeconds));
                    }
                }
            }

            Log("startup", "Failed after all attempts");
            return false;
        }

        public bool TryTurnRoomOff(int maxAttempts, int retryDelaySeconds)
        {
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    Log("shutdown", string.Format("Attempt {0}/{1}", attempt, maxAttempts));
                    var config = LoadConfig();
                    var state = GetState(config);
                    var roomId = ResolveRoomId(state);
                    InvokeJson(
                        "PUT",
                        string.Format("{0}/groups/{1}/action", BuildBaseApi(config), roomId),
                        _json.Serialize(new Dictionary<string, object> { { "on", false } })
                    );
                    Log("shutdown", string.Format("Turned off '{0}'", RoomName));
                    return true;
                }
                catch (Exception ex)
                {
                    Log("shutdown", "Attempt failed: " + ex.Message);
                    if (attempt < maxAttempts)
                    {
                        Thread.Sleep(TimeSpan.FromSeconds(retryDelaySeconds));
                    }
                }
            }

            Log("shutdown", "Failed after all attempts");
            return false;
        }

        private Dictionary<string, object> LoadConfig()
        {
            if (!File.Exists(ConfigPath))
            {
                throw new InvalidOperationException("Hue config not found at " + ConfigPath);
            }

            return _json.Deserialize<Dictionary<string, object>>(File.ReadAllText(ConfigPath));
        }

        private Dictionary<string, object> GetState(Dictionary<string, object> config)
        {
            return AsDictionary(InvokeJson("GET", BuildBaseApi(config), null));
        }

        private string BuildBaseApi(Dictionary<string, object> config)
        {
            return string.Format("http://{0}/api/{1}", config["bridge_ip"], config["username"]);
        }

        private string ResolveRoomId(Dictionary<string, object> state)
        {
            var groups = AsDictionary(state["groups"]);
            foreach (var entry in groups)
            {
                var group = AsDictionary(entry.Value);
                var type = GetString(group, "type");
                var name = GetString(group, "name");
                if ((type == "Room" || type == "Zone") && name == RoomName)
                {
                    return entry.Key;
                }
            }

            throw new InvalidOperationException("Room not found: " + RoomName);
        }

        private string ResolveSceneId(Dictionary<string, object> state, string roomId)
        {
            var scenes = AsDictionary(state["scenes"]);
            foreach (var entry in scenes)
            {
                var scene = AsDictionary(entry.Value);
                var name = GetString(scene, "name");
                var group = GetString(scene, "group");
                if (name == SceneName && group == roomId)
                {
                    return entry.Key;
                }
            }

            throw new InvalidOperationException("Scene not found: " + SceneName);
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

        private Dictionary<string, object> AsDictionary(object value)
        {
            var dictionary = value as Dictionary<string, object>;
            if (dictionary == null)
            {
                throw new InvalidOperationException("Unexpected JSON structure");
            }
            return dictionary;
        }

        private string GetString(Dictionary<string, object> dictionary, string key)
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
        private const int SERVICE_ACCEPT_PRESHUTDOWN = 0x00000100;

        private const int SERVICE_CONTROL_STOP = 0x00000001;
        private const int SERVICE_CONTROL_SHUTDOWN = 0x00000005;
        private const int SERVICE_CONTROL_PRESHUTDOWN = 0x0000000F;

        private const int NO_ERROR = 0;
        private const int SERVICE_CONFIG_PRESHUTDOWN_INFO = 7;
        private const int PRESHUTDOWN_TIMEOUT_MS = 20000;
        private const string SERVICE_NAME = "HuePowerService";

        private static readonly HueController Controller = new HueController();
        private static readonly ManualResetEvent StopEvent = new ManualResetEvent(false);
        private static readonly ServiceMainFunction MainCallback = ServiceMain;
        private static readonly HandlerEx HandlerCallback = ServiceControlHandler;

        private static IntPtr _statusHandle = IntPtr.Zero;
        private static ServiceStatus _status;
        private static StopReason _stopReason = StopReason.None;

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

            var startupThread = new Thread(delegate ()
            {
                Controller.TryApplyStartupScene(20, 3);
            });
            startupThread.IsBackground = true;
            startupThread.Start();

            _status.dwCurrentState = SERVICE_RUNNING;
            _status.dwControlsAccepted = SERVICE_ACCEPT_STOP | SERVICE_ACCEPT_SHUTDOWN | SERVICE_ACCEPT_PRESHUTDOWN;
            _status.dwWaitHint = 0;
            SetServiceStatus(_statusHandle, ref _status);

            StopEvent.WaitOne();

            if (_stopReason == StopReason.Shutdown || _stopReason == StopReason.Preshutdown)
            {
                Controller.TryTurnRoomOff(6, 2);
            }

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
            }

            return NO_ERROR;
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
    }

    internal static class Program
    {
        private static int Main(string[] args)
        {
            var controller = new HueController();

            if (args.Length > 0)
            {
                if (args[0] == "--startup-once")
                {
                    return controller.TryApplyStartupScene(20, 3) ? 0 : 1;
                }

                if (args[0] == "--shutdown-once")
                {
                    return controller.TryTurnRoomOff(6, 2) ? 0 : 1;
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
