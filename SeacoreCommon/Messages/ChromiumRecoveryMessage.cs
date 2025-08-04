using MessagePack;

namespace SeacoreCommon.Messages
{
    [MessagePackObject]
    public class ChromiumRecoveryMessage : MessageBase
    {
        [Key(0)]
        public string MessageType { get; set; } = "ChromiumRecovery";
    }
}
