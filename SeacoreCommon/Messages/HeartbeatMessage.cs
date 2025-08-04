using MessagePack;

namespace SeacoreCommon.Messages
{
    [MessagePackObject]
    public class HeartbeatMessage : MessageBase
    {
        [Key(0)]
        public long ClientTimestamp { get; set; }
    }
}
