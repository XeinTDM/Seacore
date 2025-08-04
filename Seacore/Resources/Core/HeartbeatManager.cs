using System.Net.Sockets;
using Serilog;

namespace Seacore.Resources.Core
{
    public class HeartbeatManager : IDisposable
    {
        private class ClientHeartbeatInfo
        {
            public TcpClient Client { get; }
            public DateTime ExpiryTime { get; set; }

            public ClientHeartbeatInfo(TcpClient client, DateTime expiryTime)
            {
                Client = client;
                ExpiryTime = expiryTime;
            }
        }

        private readonly Dictionary<TcpClient, ClientHeartbeatInfo> clientMapping = new();
        public bool IsMonitoring => monitoringTask != null && !monitoringTask.IsCompleted;
        private CancellationTokenSource? cancellationTokenSource;
        private readonly TimeSpan heartbeatTimeout;
        private readonly object lockObject = new();
        private Task? monitoringTask;

        public HeartbeatManager(TimeSpan timeout)
        {
            heartbeatTimeout = timeout;
        }

        public void RefreshLastHeartbeat(TcpClient client)
        {
            lock (lockObject)
            {
                if (clientMapping.TryGetValue(client, out var info))
                {
                    Log.Debug("Heartbeat refreshed for client {ClientEndPoint}. New expiry: {ExpiryTime}", client.Client.RemoteEndPoint, info.ExpiryTime);
                    info.ExpiryTime = DateTime.UtcNow.Add(heartbeatTimeout);
                }
                else
                {
                    clientMapping[client] = new ClientHeartbeatInfo(client, DateTime.UtcNow.Add(heartbeatTimeout));
                }
            }
        }

        public void StartMonitoring()
        {
            if (cancellationTokenSource != null)
                throw new InvalidOperationException("Monitoring already started.");

            cancellationTokenSource = new CancellationTokenSource();
            monitoringTask = Task.Run(() => MonitorHeartbeats(cancellationTokenSource.Token));
        }

        private async Task MonitorHeartbeats(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                List<TcpClient> expiredClients = new();
                lock (lockObject)
                {
                    var now = DateTime.UtcNow;
                    foreach (var kvp in clientMapping.ToList())
                    {
                        if (kvp.Value.ExpiryTime <= now)
                        {
                            expiredClients.Add(kvp.Key);
                        }
                    }
                    foreach (var client in expiredClients)
                    {
                        clientMapping.Remove(client);
                    }
                }

                foreach (var client in expiredClients)
                {
                    var clientInfo = TcpConnectionManager.Instance.ConnectedClients
                        .FirstOrDefault(c => c.TcpClient == client);

                    if (clientInfo != null)
                    {
                        TcpConnectionManager.Instance.DisconnectClient(clientInfo);
                    }
                    else
                    {
                        Log.Warning("ClientInfo not found for TcpClient {ClientEndPoint}", client.Client.RemoteEndPoint);
                    }
                }

                await Task.Delay(1000, token).ConfigureAwait(false);
            }
        }

        public void StopMonitoring()
        {
            cancellationTokenSource?.Cancel();
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;
            monitoringTask = null;
        }

        public void RemoveClient(TcpClient client)
        {
            lock (lockObject)
            {
                clientMapping.Remove(client);
            }
        }

        public void Dispose()
        {
            StopMonitoring();
        }
    }
}
