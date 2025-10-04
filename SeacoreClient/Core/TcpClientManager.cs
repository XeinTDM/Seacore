using System.Buffers;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MessagePack;
using SeacoreClient.Handlers;
using SeacoreCommon.Messages;
using SeacoreCommon.Utilities;

namespace SeacoreClient.Core
{
    public class TcpClientManager : IDisposable
    {
        private static readonly MessagePackSerializerOptions SerializerOptions = SerializerOptionsProvider.Options;
        public HeartbeatConfig HeartbeatConfig { get; set; } = new HeartbeatConfig();
        private static readonly HttpClient httpClient = new();
        private static readonly Random jitterer = new();
        private readonly SemaphoreSlim sendLock = new(1, 1);
        private readonly object connectionLock = new();

        private HeartbeatSender? heartbeatSender;
        private bool allowReconnect = true;
        private readonly string serverIp;
        private readonly int serverPort;
        private NetworkStream? stream;
        private int retryCount;
        private TcpClient? client;
        private CancellationTokenSource? runLoopCts;
        private bool disposed;

        public HeartbeatSender? HeartbeatSender => heartbeatSender;

        public TcpClientManager(string ip, int port)
        {
            serverIp = ip;
            serverPort = port;
        }

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (runLoopCts != null)
            {
                throw new InvalidOperationException("RunAsync has already been called. Invoke Stop before starting again.");
            }

            allowReconnect = true;
            runLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var runToken = runLoopCts.Token;

            try
            {
                while (allowReconnect && !runToken.IsCancellationRequested)
                {
                    runToken.ThrowIfCancellationRequested();

                    try
                    {
                        client = new TcpClient();
                        await client.ConnectAsync(serverIp, serverPort);
                        stream = client.GetStream();

                        var userName = Environment.UserName;
                        var osString = DetectClientOS();
                        var publicIP = await GetPublicIPAsync(runToken);

                        var identificationMessage = new ClientIdentificationMessage
                        {
                            Username = userName,
                            OS = osString,
                            PublicIP = publicIP
                        };

                        await SendMessageAsync(identificationMessage, runToken);

                        heartbeatSender = new HeartbeatSender(this, HeartbeatConfig);
                        heartbeatSender.OnHeartbeatFailure += HandleHeartbeatFailure;
                        heartbeatSender.Start();

                        Console.WriteLine("Connected to server.");
                        retryCount = 0;

                        await ListenForMessagesAsync(runToken);
                    }
                    catch (OperationCanceledException) when (runToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (!allowReconnect || runToken.IsCancellationRequested)
                        {
                            break;
                        }

                        int delay = CalculateDelay(retryCount);
                        Console.WriteLine($"Connection failed: {ex.Message}. Retrying in {delay / 1000} seconds...");
                        retryCount++;

                        try
                        {
                            await Task.Delay(delay, runToken);
                        }
                        catch (OperationCanceledException) when (runToken.IsCancellationRequested)
                        {
                            break;
                        }
                    }
                    finally
                    {
                        CloseConnection();
                    }
                }
            }
            finally
            {
                runLoopCts.Dispose();
                runLoopCts = null;
            }
        }

        private void HandleHeartbeatFailure()
        {
            if (!allowReconnect)
            {
                return;
            }

            Console.WriteLine("Heartbeat failure threshold reached. Initiating reconnection...");
            RequestReconnect();
        }

        public void RequestReconnect()
        {
            ThrowIfDisposed();

            if (!allowReconnect)
            {
                return;
            }

            CloseConnection();
        }

        public void Stop()
        {
            allowReconnect = false;
            CancelRunLoop();
            CloseConnection();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            Stop();
            disposed = true;
            GC.SuppressFinalize(this);
        }

        private void CancelRunLoop()
        {
            var loopCts = runLoopCts;
            if (loopCts != null && !loopCts.IsCancellationRequested)
            {
                loopCts.Cancel();
            }
        }

        private void CloseConnection()
        {
            lock (connectionLock)
            {
                if (heartbeatSender != null)
                {
                    heartbeatSender.OnHeartbeatFailure -= HandleHeartbeatFailure;
                    heartbeatSender.Dispose();
                    heartbeatSender = null;
                }

                stream?.Dispose();
                stream = null;

                if (client != null)
                {
                    client.Dispose();
                    client = null;
                }
            }
        }

        private async Task<string> GetPublicIPAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var response = await httpClient.GetAsync("https://api.ipify.org?format=json", cancellationToken);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var ipResponse = JsonSerializer.Deserialize<IpifyResponse>(json);
                return ipResponse?.Ip ?? "Unknown";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching public IP: {ex.Message}");
                return "Unknown";
            }
        }

        private class IpifyResponse
        {
            [JsonPropertyName("ip")]
            public string? Ip { get; set; }
        }

        private string DetectClientOS()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Version ver = Environment.OSVersion.Version;
                return (ver.Major == 10 && ver.Build >= 22000) ? "Win 11" : "Win 10";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return "Linux";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "Mac";
            }

            return "Unknown";
        }

        private static int CalculateDelay(int retryCount)
        {
            int baseDelay = retryCount < 5 ? 500 : (int)Math.Pow(2, retryCount - 4) * 500;

            baseDelay = Math.Min(baseDelay, 10000);

            double jitter = baseDelay * 0.1;
            return baseDelay + (int)(jitterer.NextDouble() * 2 * jitter - jitter);
        }

        public async Task SendMessageAsync(MessageBase message, CancellationToken cancellationToken = default)
        {
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            ThrowIfDisposed();

            await sendLock.WaitAsync(cancellationToken);
            try
            {
                if (stream == null || client?.Connected != true)
                {
                    throw new InvalidOperationException("Cannot send message because the client is not connected.");
                }

                var bytes = MessagePackSerializer.Serialize(message, SerializerOptions);
                var lengthBytes = BitConverter.GetBytes(bytes.Length);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(lengthBytes);
                }

                await stream.WriteAsync(lengthBytes.AsMemory(0, lengthBytes.Length), cancellationToken);
                await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in SendMessageAsync: {ex.Message}");
                throw;
            }
            finally
            {
                sendLock.Release();
            }
        }

        private async Task ListenForMessagesAsync(CancellationToken cancellationToken)
        {
            if (stream == null)
            {
                return;
            }

            try
            {
                while (!cancellationToken.IsCancellationRequested && client?.Connected == true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    byte[] lengthBuffer = ArrayPool<byte>.Shared.Rent(4);
                    try
                    {
                        int bytesRead = await stream.ReadAsync(lengthBuffer.AsMemory(0, 4), cancellationToken);
                        if (bytesRead < 4)
                        {
                            Console.WriteLine("Disconnected: Incomplete length prefix received.");
                            break;
                        }

                        if (BitConverter.IsLittleEndian)
                        {
                            Array.Reverse(lengthBuffer, 0, 4);
                        }

                        int messageLength = BitConverter.ToInt32(lengthBuffer, 0);
                        if (messageLength <= 0 || messageLength > Utility.MAX_MESSAGE_SIZE)
                        {
                            Console.WriteLine($"Invalid or oversized message length received: {messageLength}");
                            break;
                        }

                        byte[] messageBuffer = ArrayPool<byte>.Shared.Rent(messageLength);
                        try
                        {
                            int totalBytesRead = 0;
                            while (totalBytesRead < messageLength)
                            {
                                int read = await stream.ReadAsync(messageBuffer.AsMemory(totalBytesRead, messageLength - totalBytesRead), cancellationToken);
                                if (read == 0)
                                {
                                    Console.WriteLine("Disconnected: Incomplete message received.");
                                    break;
                                }

                                totalBytesRead += read;
                            }

                            if (totalBytesRead < messageLength)
                            {
                                Console.WriteLine("Disconnected: Incomplete message received after reading.");
                                break;
                            }

                            try
                            {
                                var message = MessagePackSerializer.Deserialize<MessageBase>(messageBuffer.AsMemory(0, messageLength), SerializerOptions);
                                Console.WriteLine($"Received message of type: {message.GetType().Name}");

                                try
                                {
                                    MessageHandler.ProcessMessage(message, this);
                                }
                                catch (Exception handlerEx)
                                {
                                    Console.WriteLine($"Error processing message: {handlerEx.Message}");
                                }
                            }
                            catch (MessagePackSerializationException ex)
                            {
                                Console.WriteLine($"Deserialization error: {ex.Message}");
                            }
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(messageBuffer);
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(lengthBuffer);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // graceful shutdown
            }
            catch (IOException ioEx) when (ioEx.InnerException is SocketException socketEx && socketEx.SocketErrorCode == SocketError.ConnectionReset)
            {
                Console.WriteLine("Connection was forcibly closed by the server. Attempting to reconnect...");
            }
            catch (SocketException sockEx)
            {
                Console.WriteLine($"Socket error occurred: {sockEx.Message}. Attempting to reconnect...");
            }
            catch (ObjectDisposedException)
            {
                if (!cancellationToken.IsCancellationRequested && allowReconnect)
                {
                    Console.WriteLine("Connection closed. Attempting to reconnect...");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An unexpected error occurred: {ex.Message}. Attempting to reconnect...");
            }
        }

        public void HandleServerInitiatedDisconnect()
        {
            Console.WriteLine("Server initiated disconnect.");
            Stop();
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(TcpClientManager));
            }
        }
    }
}
