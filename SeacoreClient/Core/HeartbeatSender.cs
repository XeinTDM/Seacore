using SeacoreCommon.Messages;

namespace SeacoreClient.Core
{
    public class HeartbeatSender : IDisposable
    {
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private NetworkCondition networkCondition = NetworkCondition.Good;
        private readonly object timerLock = new object();
        private readonly TcpClientManager clientManager;
        private readonly Random jitterer = new Random();
        private readonly HeartbeatConfig config;
        private int consecutiveFailures = 0;
        private long lastHeartbeatSent = 0;
        private bool isSending = false;
        private Timer? heartbeatTimer;
        private long lastRtt = 0;

        public event Action? OnHeartbeatFailure;

        public HeartbeatSender(TcpClientManager clientManager, HeartbeatConfig? config = null)
        {
            this.clientManager = clientManager ?? throw new ArgumentNullException(nameof(clientManager));
            this.config = config ?? new HeartbeatConfig();
        }

        public void Start()
        {
            ScheduleNextHeartbeat(ComputeInterval(networkCondition));
        }

        public void Stop()
        {
            lock (timerLock)
            {
                cts.Cancel();
                heartbeatTimer?.Dispose();
                heartbeatTimer = null;
            }
        }

        public void HandleHeartbeatAck(long serverTimestamp)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lastRtt = now - lastHeartbeatSent;

            Console.WriteLine($"Heartbeat acknowledged. RTT: {lastRtt} ms");

            consecutiveFailures = 0;

            AdjustNetworkCondition();
            ScheduleNextHeartbeat(ComputeInterval(networkCondition));
        }

        private async Task SendHeartbeatAsync()
        {
            if (isSending)
                return;

            try
            {
                isSending = true;
                lastHeartbeatSent = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                var heartbeatMessage = new HeartbeatMessage
                {
                    ClientTimestamp = lastHeartbeatSent
                };

                clientManager.SendMessage(heartbeatMessage);

                Console.WriteLine($"Heartbeat sent at {lastHeartbeatSent} ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending heartbeat: {ex.Message}");
                consecutiveFailures++;

                if (consecutiveFailures >= config.MaxConsecutiveFailures)
                {
                    Console.WriteLine("Maximum consecutive heartbeat failures reached. Initiating reconnection.");
                    OnHeartbeatFailure?.Invoke();
                }
            }
            finally
            {
                isSending = false;

                if (consecutiveFailures < config.MaxConsecutiveFailures)
                {
                    ScheduleNextHeartbeat(ComputeInterval(networkCondition));
                }
            }
        }

        private void ScheduleNextHeartbeat(int interval)
        {
            lock (timerLock)
            {
                if (cts.IsCancellationRequested)
                    return;

                heartbeatTimer?.Dispose();

                int jitter = (int)(interval * config.JitterPercentage);
                int jitteredInterval = Math.Max(0, interval + jitterer.Next(-jitter, jitter));

                heartbeatTimer = new Timer(async _ => await SendHeartbeatAsync(), null, jitteredInterval, Timeout.Infinite);
            }
        }

        private int ComputeInterval(NetworkCondition condition)
        {
            return condition switch
            {
                NetworkCondition.Excellent => config.ExcellentInterval,
                NetworkCondition.Good => config.GoodInterval,
                NetworkCondition.Moderate => config.ModerateInterval,
                NetworkCondition.Poor => config.PoorInterval,
                _ => config.ModerateInterval
            };
        }

        private void AdjustNetworkCondition()
        {
            if (lastRtt < 50)
            {
                networkCondition = NetworkCondition.Excellent;
                Console.WriteLine("Network condition set to Excellent.");
            }
            else if (lastRtt < 100)
            {
                networkCondition = NetworkCondition.Good;
                Console.WriteLine("Network condition set to Good.");
            }
            else if (lastRtt < 300)
            {
                networkCondition = NetworkCondition.Moderate;
                Console.WriteLine("Network condition set to Moderate.");
            }
            else
            {
                networkCondition = NetworkCondition.Poor;
                Console.WriteLine("Network condition set to Poor.");
            }
        }

        public void Dispose()
        {
            Stop();
            cts.Dispose();
        }
    }

    public enum NetworkCondition
    {
        Excellent,
        Good,
        Moderate,
        Poor
    }
}
