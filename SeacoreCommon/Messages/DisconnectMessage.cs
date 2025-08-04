using MessagePack;

namespace SeacoreCommon.Messages
{
    [MessagePackObject]
    public class DisconnectMessage : MessageBase
    {
        [Key(0)]
        public string Reason { get; set; } = "Server requested disconnect";
    }
}