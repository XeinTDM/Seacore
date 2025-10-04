using MessagePack;

namespace SeacoreCommon.Messages
{
    [MessagePackObject]
    public class RemoteDesktopRequestMessage : MessageBase
    {
        [Key(0)]
        public bool IsStart { get; set; } = true;

        [Key(1)]
        public int IntervalMilliseconds { get; set; } = 500;

        [Key(2)]
        public int JpegQuality { get; set; } = 70;
    }
}
