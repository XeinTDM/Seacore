using MessagePack;

namespace SeacoreCommon.Messages
{
    [MessagePackObject]
    public class UnknownMessage : MessageBase
    {
        [Key(0)]
        public string RawMessage { get; set; } = string.Empty;
    }
}
