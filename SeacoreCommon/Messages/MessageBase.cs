using MessagePack;

namespace SeacoreCommon.Messages
{
    [MessagePackObject]
    [Union(0, typeof(HeartbeatMessage))]
    [Union(1, typeof(HeartbeatAckMessage))]
    [Union(2, typeof(DisconnectMessage))]
    [Union(3, typeof(ReconnectMessage))]
    [Union(4, typeof(ChromiumRecoveryMessage))]
    [Union(5, typeof(UnknownMessage))]
    [Union(6, typeof(ClientIdentificationMessage))]
    public abstract class MessageBase
    {
    }
}
