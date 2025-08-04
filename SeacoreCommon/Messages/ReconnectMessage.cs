using MessagePack;

namespace SeacoreCommon.Messages
{
    [MessagePackObject]
    public class ReconnectMessage : MessageBase
    {
        [Key(0)]
        public string Reason { get; set; } = "Server requested reconnect";
    }
}