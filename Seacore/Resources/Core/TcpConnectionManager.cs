using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using MessagePack;
using Seacore.Resources.Usercontrols.Clients;
using SeacoreCommon.Messages;
using SeacoreCommon.Utilities;
using Serilog;

namespace Seacore.Resources.Core
{
    public class TcpConnectionManager
    {
        private static readonly MessagePackSerializerOptions SerializerOptions = SerializerOptionsProvider.Options;
        private static readonly Lazy<TcpConnectionManager> instance = new(() => new TcpConnectionManager());
        private readonly Dictionary<int, CancellationTokenSource> cancellationTokens = new();
        public ObservableCollection<ClientInfo> ConnectedClients { get; } = new();
        private readonly Dictionary<int, List<TcpClient>> portClients = new();
        public static TcpConnectionManager Instance => instance.Value;
        private readonly Dictionary<int, TcpListener> listeners = new();

        private readonly HeartbeatManager heartbeatManager = new(TimeSpan.FromMinutes(1));
        public HeartbeatManager HeartbeatManager => heartbeatManager;
        private TcpConnectionManager() { }

        private static readonly HttpClient httpClient = new HttpClient();
        private static readonly ConcurrentDictionary<string, (string Alpha3, string Alpha2)> locationCache = new ConcurrentDictionary<string, (string, string)>();

        public void Start(int port)
        {
            Log.Information("Starting TCP server on port {Port}", port);

            try
            {
                Stop(port);
                var listener = new TcpListener(IPAddress.Any, port);
                Log.Information("Initializing TCP listener on port {Port}", port);
                var cancellationTokenSource = new CancellationTokenSource();
                listeners[port] = listener;
                cancellationTokens[port] = cancellationTokenSource;

                listener.Start();
                Log.Information("Listener started on port {Port}", port);

                Task.Run(() => AcceptClientsAsync(listener, cancellationTokenSource.Token, port));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to start TCP server on port {Port}", port);
            }
        }

        public static void SendMessage(NetworkStream stream, MessageBase message)
        {
            try
            {
                var bytes = MessagePackSerializer.Serialize<MessageBase>(message, SerializerOptions);
                var lengthBytes = BitConverter.GetBytes(bytes.Length);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(lengthBytes); // Ensure big-endian

                Log.Debug("Sending message with length {Length} and bytes {Bytes}", bytes.Length, BitConverter.ToString(bytes));

                stream.Write(lengthBytes, 0, lengthBytes.Length);
                stream.Write(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Exception in SendMessage");
            }
        }

        private async Task AcceptClientsAsync(TcpListener listener, CancellationToken token, int port)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync(token);
                    Log.Information("Client connected: {ClientEndPoint}", client.Client.RemoteEndPoint);

                    var clientInfo = new ClientInfo(client)
                    {
                        PublicIP = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString(),
                        Created = DateTime.Now
                    };

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ConnectedClients.Add(clientInfo);
                        if (!portClients.TryGetValue(port, out List<TcpClient>? value))
                        {
                            value = new List<TcpClient>();
                            portClients[port] = value;
                        }

                        value.Add(client);
                    });

                    StartHeartbeatForClient(clientInfo);
                    var handler = new NetworkCommunicationHandler(client, heartbeatManager, clientInfo);
                    Task.Run(() => handler.HandleCommunicationAsync(token));

                    await FetchClientLocationAsync(clientInfo);
                }
                catch (OperationCanceledException)
                {
                    Log.Information("Accepting clients operation canceled on port {Port}", port);
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error accepting client connection on port {Port}", port);
                }
            }
        }

        public void ReconnectClientsByPort(int port)
        {
            if (portClients.TryGetValue(port, out var clients))
            {
                foreach (var client in clients.ToList())
                {
                    if (client.Connected)
                    {
                        var clientInfo = ConnectedClients.FirstOrDefault(c => c.TcpClient == client);
                        if (clientInfo != null)
                        {
                            ReconnectClient(clientInfo);
                        }
                    }
                }
            }
        }

        public void ReconnectClient(ClientInfo clientInfo)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (ConnectedClients.Contains(clientInfo))
                {
                    Log.Information("Disconnecting client {ClientEndPoint}", clientInfo.TcpClient.Client.RemoteEndPoint);
                    var message = new ReconnectMessage();
                    SendMessage(clientInfo.TcpClient.GetStream(), message);
                    ConnectedClients.Remove(clientInfo);
                    heartbeatManager.RemoveClient(clientInfo.TcpClient);
                }
            });
        }

        public void DisconnectAllClients(int port)
        {
            if (portClients.TryGetValue(port, out var clients))
            {
                foreach (var client in clients.ToList())
                {
                    if (client.Connected)
                    {
                        var clientInfo = ConnectedClients.FirstOrDefault(c => c.TcpClient == client);
                        if (clientInfo != null)
                        {
                            DisconnectClient(clientInfo);
                        }
                    }
                }
            }
        }

        public void DisconnectClient(ClientInfo clientInfo)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (ConnectedClients.Contains(clientInfo))
                {
                    var message = new DisconnectMessage();
                    SendMessage(clientInfo.TcpClient.GetStream(), message);
                    ConnectedClients.Remove(clientInfo);
                    heartbeatManager.RemoveClient(clientInfo.TcpClient);
                }
            });
        }

        private void StartHeartbeatForClient(ClientInfo clientInfo)
        {
            heartbeatManager.RefreshLastHeartbeat(clientInfo.TcpClient);
        }

        public void RemoveConnectedClient(TcpClient client)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var clientInfo = ConnectedClients.FirstOrDefault(c => c.TcpClient == client);
                if (clientInfo != null)
                {
                    ConnectedClients.Remove(clientInfo);
                }
            });
            heartbeatManager.RemoveClient(client);
        }

        public void Stop(int port)
        {
            Log.Information("Stopping server on port {Port}", port);

            if (listeners.TryGetValue(port, out var listener) && cancellationTokens.TryGetValue(port, out var cancellationTokenSource))
            {
                cancellationTokenSource.Cancel();
                listener.Stop();
                DisconnectAllClients(port);
                listeners.Remove(port);
                cancellationTokens.Remove(port);
                portClients.Remove(port);

                if (listeners.Count == 0)
                {
                    heartbeatManager.StopMonitoring();
                }
            }
            else
            {
                Log.Warning("Attempted to stop non-existent server on port {Port}.", port);
            }
        }

        public void GlobalStop()
        {
            foreach (var port in listeners.Keys.ToList())
            {
                Stop(port);
            }
        }

        public bool IsPortActive(int port)
        {
            return listeners.ContainsKey(port);
        }

        public async Task FetchClientLocationAsync(ClientInfo clientInfo)
        {
            string ip = clientInfo.PublicIP;

            if (string.IsNullOrEmpty(ip) || ip.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            {
                clientInfo.Location = "Unknown";
                clientInfo.LocationImg = "";
                return;
            }

            if (locationCache.TryGetValue(ip, out var cachedLocation))
            {
                clientInfo.Location = cachedLocation.Alpha3;
                clientInfo.LocationImg = $"https://flagsapi.com/{cachedLocation.Alpha2}/flat/16.png";
                return;
            }

            try
            {
                // Primary API: ipapi.co
                var response = await httpClient.GetAsync($"https://ipapi.co/{ip}/json/");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var ipapiResponse = JsonSerializer.Deserialize<IpApiCoResponse>(json);

                    if (ipapiResponse != null && !string.IsNullOrEmpty(ipapiResponse.CountryCodeAlpha3) && !string.IsNullOrEmpty(ipapiResponse.CountryCodeAlpha2))
                    {
                        clientInfo.Location = ipapiResponse.CountryCodeAlpha3;
                        clientInfo.LocationImg = $"https://flagsapi.com/{ipapiResponse.CountryCodeAlpha2}/flat/16.png";

                        locationCache.TryAdd(ip, (ipapiResponse.CountryCodeAlpha3, ipapiResponse.CountryCodeAlpha2));
                        return;
                    }
                }

                // Fallback API: ip-api.com
                response = await httpClient.GetAsync($"http://ip-api.com/json/{ip}");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var ipApiResponse = JsonSerializer.Deserialize<IpApiComResponse>(json);

                    if (ipApiResponse != null && ipApiResponse.Status.Equals("success", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrEmpty(ipApiResponse.CountryCode))
                    {
                        string alpha2 = ipApiResponse.CountryCode;
                        string alpha3 = CountryCodeMappings.GetAlpha3(alpha2);

                        clientInfo.Location = alpha3;
                        clientInfo.LocationImg = $"https://flagsapi.com/{alpha2}/flat/16.png";

                        locationCache.TryAdd(ip, (alpha3, alpha2));
                        return;
                    }
                }

                // If both APIs fail
                clientInfo.Location = "Unknown";
                clientInfo.LocationImg = "";
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error fetching location for IP {IP}", ip);
                clientInfo.Location = "Unknown";
                clientInfo.LocationImg = "";
            }
        }

        private class IpApiCoResponse
        {
            [JsonPropertyName("country")]
            public string Country { get; set; }

            [JsonPropertyName("country_code")]
            public string CountryCodeAlpha2 { get; set; }

            [JsonPropertyName("country_code_alpha3")]
            public string CountryCodeAlpha3 { get; set; }
        }

        private class IpApiComResponse
        {
            [JsonPropertyName("status")]
            public string Status { get; set; }

            [JsonPropertyName("country")]
            public string Country { get; set; }

            [JsonPropertyName("countryCode")]
            public string CountryCode { get; set; }
        }
    }
}
