using MessagePack;

namespace SeacoreCommon.Messages
{
    [MessagePackObject]
    public class HeartbeatAckMessage : MessageBase
    {
        [Key(0)]
        public long ClientTimestamp { get; set; }

        [Key(1)]
        public long ServerTimestamp { get; set; }
    }
}
