using System.Collections.ObjectModel;
using Seacore.Resources.Core;
using System.Text.Json;
using System.IO;
using Serilog;

namespace Seacore.Resources.Usercontrols.Servers
{
    public sealed class ServerStateManager
    {
        private static readonly Lazy<ServerStateManager> instance = new(() => new ServerStateManager());
        public static ServerStateManager Instance => instance.Value;

        private readonly string configFilePath = "servers.json";

        public ObservableCollection<ServerInfo> ServerInfoList { get; } = [];
        public Dictionary<ServerInfo, TimeSpan> BaseUptimeMap { get; } = [];

        private DateTime lastSaveTime = DateTime.Now;

        private ServerStateManager() { }

        public void Initialize()
        {
            LoadServersFromConfig();
            InitializeBaseUptimes();
            StartListeningServersAtStartup();
        }

        private void LoadServersFromConfig()
        {
            if (!File.Exists(configFilePath)) return;

            try
            {
                string json = File.ReadAllText(configFilePath);
                var servers = JsonSerializer.Deserialize<List<ServerInfo>>(json);
                if (servers != null)
                {
                    ServerInfoList.Clear();
                    foreach (var server in servers)
                        ServerInfoList.Add(server);
                }
            }
            catch
            {
                Log.Warning("File is either corrupted or unreadable.");
            }
        }

        public void SaveServersToConfig(bool force = false)
        {
            var now = DateTime.Now;
            if (!force && (now - lastSaveTime) < TimeSpan.FromMinutes(1))
                return;

            try
            {
                var servers = ServerInfoList.ToList();
                string json = JsonSerializer.Serialize(servers, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configFilePath, json);
                lastSaveTime = now;
            }
            catch
            {
                Log.Warning("Unable to save servers to config.");
            }
        }

        private void InitializeBaseUptimes()
        {
            foreach (var server in ServerInfoList)
            {
                TimeSpan parsed = ParseUptime(server.Uptime);
                BaseUptimeMap[server] = parsed;

                if (server.Status == "Listening" && server.StartTime == default)
                {
                    server.StartTime = DateTime.Now - parsed;
                }
            }
        }

        public void UpdateUptime()
        {
            bool uptimeChanged = false;
            var now = DateTime.Now;

            foreach (var serverInfo in ServerInfoList)
            {
                if (serverInfo.Status == "Listening")
                {
                    if (!BaseUptimeMap.TryGetValue(serverInfo, out var baseUptime))
                        baseUptime = TimeSpan.Zero;

                    TimeSpan elapsed = now - serverInfo.StartTime;
                    var totalUptime = baseUptime + elapsed;
                    var newUptime = FormatUptime(totalUptime);

                    if (serverInfo.Uptime != newUptime)
                    {
                        serverInfo.Uptime = newUptime;
                        uptimeChanged = true;
                    }
                }
            }

            if (uptimeChanged)
                SaveServersToConfig();
        }

        private void StartListeningServersAtStartup()
        {
            var manager = TcpConnectionManager.Instance;
            foreach (var server in ServerInfoList)
            {
                if (server.Status == "Listening" && ValidateNetworkPort(server.Port, out int port) && !manager.IsPortActive(port))
                {
                    manager.Start(port);
                }
            }
        }

        public void StartListeningOnPort(int parsedPort)
        {
            var manager = TcpConnectionManager.Instance;
            var existingServer = ServerInfoList.FirstOrDefault(c => c.Port == parsedPort.ToString());

            if (existingServer == null)
            {
                var newServer = new ServerInfo("Server", parsedPort.ToString(), "Listening", "0s")
                {
                    StartTime = DateTime.Now
                };
                ServerInfoList.Add(newServer);
                BaseUptimeMap[newServer] = TimeSpan.Zero;

                if (!manager.IsPortActive(parsedPort))
                    manager.Start(parsedPort);
            }
            else
            {
                if (existingServer.Status != "Listening")
                {
                    existingServer.Status = "Listening";
                    existingServer.StartTime = DateTime.Now;

                    var oldUptime = ParseUptime(existingServer.Uptime);
                    BaseUptimeMap[existingServer] = oldUptime;

                    if (!manager.IsPortActive(parsedPort))
                        manager.Start(parsedPort);
                }
            }

            SaveServersToConfig(force: true);
        }

        public void ToggleServerStatus(ServerInfo serverInfo)
        {
            var manager = TcpConnectionManager.Instance;
            int port = int.Parse(serverInfo.Port);

            if (serverInfo.Status == "Listening")
            {
                serverInfo.Status = "Inactive";
                manager.ReconnectClientsByPort(port);
                manager.Stop(port);
            }
            else
            {
                serverInfo.Status = "Listening";
                var oldUptime = ParseUptime(serverInfo.Uptime);
                BaseUptimeMap[serverInfo] = oldUptime;
                serverInfo.StartTime = DateTime.Now;

                if (!manager.IsPortActive(port))
                    manager.Start(port);
            }

            SaveServersToConfig();
        }

        public void DeleteServer(ServerInfo serverInfo)
        {
            var manager = TcpConnectionManager.Instance;
            if (int.TryParse(serverInfo.Port, out var port))
            {
                ServerInfoList.Remove(serverInfo);
                manager.ReconnectClientsByPort(port);
                manager.Stop(port);
                BaseUptimeMap.Remove(serverInfo);
                SaveServersToConfig(force: true);
            }
        }

        private bool ValidateNetworkPort(string portString, out int parsedPort)
        {
            return int.TryParse(portString, out parsedPort) && parsedPort is >= 1024 and <= 65535;
        }

        private TimeSpan ParseUptime(string uptimeStr)
        {
            if (string.IsNullOrWhiteSpace(uptimeStr))
                return TimeSpan.Zero;

            int hours = 0, minutes = 0, seconds = 0;
            var temp = uptimeStr;

            int hIndex = temp.IndexOf('h');
            if (hIndex > -1)
            {
                hours = int.Parse(temp[..hIndex]);
                temp = temp[(hIndex + 1)..];
            }

            int mIndex = temp.IndexOf('m');
            if (mIndex > -1)
            {
                minutes = int.Parse(temp[..mIndex]);
                temp = temp[(mIndex + 1)..];
            }

            int sIndex = temp.IndexOf('s');
            if (sIndex > -1)
            {
                seconds = int.Parse(temp[..sIndex]);
            }

            return new TimeSpan(hours, minutes, seconds);
        }

        private string FormatUptime(TimeSpan duration)
        {
            int hours = (int)duration.TotalHours;
            int minutes = duration.Minutes;
            int seconds = duration.Seconds;

            if (hours > 0)
            {
                return $"{hours}h{minutes}m{seconds}s";
            }
            else if (minutes > 0)
            {
                return $"{minutes}m{seconds}s";
            }
            else
            {
                return $"{seconds}s";
            }
        }
    }
}
