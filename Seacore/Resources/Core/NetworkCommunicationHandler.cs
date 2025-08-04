using Seacore.Resources.Usercontrols.Clients;
using SeacoreCommon.Utilities;
using SeacoreCommon.Messages;
using System.Net.Sockets;
using MessagePack;
using Serilog;

namespace Seacore.Resources.Core
{
    public class NetworkCommunicationHandler
    {
        private static readonly MessagePackSerializerOptions SerializerOptions = SerializerOptionsProvider.Options;
        private readonly HeartbeatManager heartbeatManager;
        private readonly NetworkStream stream;
        private readonly ClientInfo clientInfo;
        private readonly TcpClient client;

        public NetworkCommunicationHandler(TcpClient client, HeartbeatManager heartbeatManager, ClientInfo clientInfo)
        {
            this.client = client;
            this.heartbeatManager = heartbeatManager;
            this.clientInfo = clientInfo;
            this.stream = client.GetStream();
        }

        public async Task HandleCommunicationAsync(CancellationToken token)
        {
            Log.Information("Handling communication for client: {ClientEndPoint}", client.Client.RemoteEndPoint);

            try
            {
                while (!token.IsCancellationRequested && client.Connected)
                {
                    byte[] lengthBytes = new byte[4];
                    int bytesRead = await stream.ReadAsync(lengthBytes, 0, 4, token);
                    if (bytesRead == 0)
                    {
                        Log.Information("Client {ClientEndPoint} disconnected.", client.Client.RemoteEndPoint);
                        break;
                    }
                    if (bytesRead < 4)
                    {
                        Log.Error("Incomplete length prefix received from client {ClientEndPoint}. Closing connection.", client.Client.RemoteEndPoint);
                        break;
                    }

                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(lengthBytes);
                    int messageLength = BitConverter.ToInt32(lengthBytes, 0);

                    if (messageLength <= 0 || messageLength > Utility.MAX_MESSAGE_SIZE)
                    {
                        Log.Error("Invalid or oversized message length ({MessageLength}) received from client {ClientEndPoint}. Closing connection.", messageLength, client.Client.RemoteEndPoint);
                        break;
                    }

                    byte[] messageBytes = new byte[messageLength];
                    int totalBytesRead = 0;
                    while (totalBytesRead < messageLength)
                    {
                        int read = await stream.ReadAsync(messageBytes, totalBytesRead, messageLength - totalBytesRead, token);
                        if (read == 0)
                        {
                            Log.Error("Incomplete message received from client {ClientEndPoint}. Closing connection.", client.Client.RemoteEndPoint);
                            break;
                        }
                        totalBytesRead += read;
                    }

                    if (totalBytesRead < messageLength)
                    {
                        Log.Error("Incomplete message received after reading from client {ClientEndPoint}. Closing connection.", client.Client.RemoteEndPoint);
                        break;
                    }

                    Log.Debug("Received message bytes from client {ClientEndPoint}: {Bytes}", client.Client.RemoteEndPoint, BitConverter.ToString(messageBytes));

                    try
                    {
                        var message = MessagePackSerializer.Deserialize<MessageBase>(messageBytes, SerializerOptions);
                        Log.Information("Received message of type: {MessageType} from client {ClientEndPoint}", message.GetType().Name, client.Client.RemoteEndPoint);

                        bool success = MessageProcessor.ProcessMessage(message, client, stream, heartbeatManager, clientInfo);

                        if (!success)
                        {
                            Log.Warning("Message processing failed for client {ClientEndPoint}. Closing connection.", client.Client.RemoteEndPoint);
                            break;
                        }
                    }
                    catch (MessagePackSerializationException ex)
                    {
                        Log.Error(ex, "Deserialization error for client {ClientEndPoint}. Closing connection.", client.Client.RemoteEndPoint);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error handling communication for client {ClientEndPoint}", client.Client.RemoteEndPoint);
            }
            finally
            {
                TcpConnectionManager.Instance.RemoveConnectedClient(client);
                Log.Information("Client disconnected: {ClientEndPoint}", client.Client.RemoteEndPoint);
                client.Close();
            }
        }
    }
}
