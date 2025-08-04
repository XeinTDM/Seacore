using MessagePack;

namespace SeacoreCommon.Messages
{
    [MessagePackObject]
    public class ClientIdentificationMessage : MessageBase
    {
        [Key(0)]
        public string Username { get; set; }
        [Key(1)]
        public string OS { get; set; }
        [Key(2)]
        public string PublicIP { get; set; }
    }
}
