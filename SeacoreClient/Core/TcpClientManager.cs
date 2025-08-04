using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using SeacoreClient.Handlers;
using SeacoreCommon.Messages;
using System.Net.Sockets;
using System.Text.Json;
using MessagePack;
using SeacoreCommon.Utilities;

namespace SeacoreClient.Core
{
    public class TcpClientManager
    {
        private static readonly MessagePackSerializerOptions SerializerOptions = SerializerOptionsProvider.Options;
        public HeartbeatConfig HeartbeatConfig { get; set; } = new HeartbeatConfig();
        private static readonly HttpClient httpClient = new HttpClient();
        private static readonly Random jitterer = new Random();
        private HeartbeatSender? heartbeatSender;
        private bool allowReconnect = true;
        private readonly string serverIp;
        private readonly int serverPort;
        private NetworkStream? stream;
        private int retryCount = 0;
        private TcpClient? client;

        public HeartbeatSender? HeartbeatSender => heartbeatSender;

        public TcpClientManager(string ip, int port)
        {
            serverIp = ip;
            serverPort = port;
        }

        public async Task RunAsync()
        {
            while (allowReconnect)
            {
                try
                {
                    client = new TcpClient();
                    await client.ConnectAsync(serverIp, serverPort);
                    stream = client.GetStream();

                    var userName = Environment.UserName;
                    var osString = DetectClientOS();
                    var publicIP = await GetPublicIPAsync();

                    var identificationMessage = new ClientIdentificationMessage
                    {
                        Username = userName,
                        OS = osString,
                        PublicIP = publicIP
                    };
                    SendMessage(identificationMessage);

                    heartbeatSender = new HeartbeatSender(this, this.HeartbeatConfig);
                    heartbeatSender.OnHeartbeatFailure += HandleHeartbeatFailure;
                    heartbeatSender.Start();
                    Console.WriteLine("Connected to server.");
                    retryCount = 0;
                    await ListenForMessagesAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Connection failed: {ex.Message}. Retrying in {CalculateDelay(retryCount) / 1000} seconds...");
                    int delay = CalculateDelay(retryCount);
                    await Task.Delay(delay);
                    retryCount++;
                }
            }
        }

        private void HandleHeartbeatFailure()
        {
            Console.WriteLine("Heartbeat failure threshold reached. Initiating reconnection...");
            InitiateReconnection();
        }

        private void InitiateReconnection()
        {
            allowReconnect = true;
            heartbeatSender?.Stop();
            stream?.Close();
            client?.Close();
        }

        private async Task<string> GetPublicIPAsync()
        {
            try
            {
                var response = await httpClient.GetAsync("https://api.ipify.org?format=json");
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                var ipResponse = JsonSerializer.Deserialize<IpifyResponse>(json);
                return ipResponse?.Ip ?? "Unknown";
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
            public string Ip { get; set; }
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

        public void SendMessage(MessageBase message)
        {
            try
            {
                if (stream != null && client?.Connected == true)
                {
                    var bytes = MessagePackSerializer.Serialize(message, SerializerOptions);
                    var lengthBytes = BitConverter.GetBytes(bytes.Length);
                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(lengthBytes);
                    stream.Write(lengthBytes, 0, lengthBytes.Length);
                    stream.Write(bytes, 0, bytes.Length);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in SendMessage: {ex.Message}");
            }
        }

        private async Task ListenForMessagesAsync()
        {
            try
            {
                while (client?.Connected == true)
                {
                    byte[] lengthBytes = new byte[4];
                    int bytesRead = await stream.ReadAsync(lengthBytes, 0, 4);
                    if (bytesRead < 4)
                    {
                        Console.WriteLine("Disconnected: Incomplete length prefix received.");
                        break;
                    }

                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(lengthBytes);
                    int messageLength = BitConverter.ToInt32(lengthBytes, 0);

                    if (messageLength <= 0 || messageLength > Utility.MAX_MESSAGE_SIZE)
                    {
                        Console.WriteLine($"Invalid or oversized message length received: {messageLength}");
                        break;
                    }

                    byte[] messageBytes = new byte[messageLength];
                    int totalBytesRead = 0;
                    while (totalBytesRead < messageLength)
                    {
                        int read = await stream.ReadAsync(messageBytes, totalBytesRead, messageLength - totalBytesRead);
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

                    Console.WriteLine($"Received message bytes: {BitConverter.ToString(messageBytes)}");

                    try
                    {
                        var message = MessagePackSerializer.Deserialize<MessageBase>(messageBytes, SerializerOptions);
                        Console.WriteLine($"Received message of type: {message.GetType().Name}");
                        MessageHandler.ProcessMessage(message, this);
                    }
                    catch (MessagePackSerializationException ex)
                    {
                        Console.WriteLine($"Deserialization error: {ex.Message}");
                    }
                }
            }
            catch (IOException ioEx) when (ioEx.InnerException is SocketException socketEx && socketEx.SocketErrorCode == SocketError.ConnectionReset)
            {
                Console.WriteLine("Connection was forcibly closed by the server. Attempting to reconnect...");
            }
            catch (SocketException sockEx)
            {
                Console.WriteLine($"Socket error occurred: {sockEx.Message}. Attempting to reconnect...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An unexpected error occurred: {ex.Message}. Attempting to reconnect...");
            }
            finally
            {
                heartbeatSender?.Stop();
                stream?.Close();
                client?.Close();
            }
        }

        public void HandleServerInitiatedDisconnect()
        {
            Console.WriteLine("Server initiated disconnect.");
            allowReconnect = false;
            heartbeatSender?.Stop();
            stream?.Close();
            client?.Close();
        }
    }
}
