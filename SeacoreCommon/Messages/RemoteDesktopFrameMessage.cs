using System;
using MessagePack;

namespace SeacoreCommon.Messages
{
    [MessagePackObject]
    public class RemoteDesktopFrameMessage : MessageBase
    {
        [Key(0)]
        public byte[] ImageData { get; set; } = Array.Empty<byte>();

        [Key(1)]
        public int Width { get; set; }

        [Key(2)]
        public int Height { get; set; }

        [Key(3)]
        public long Timestamp { get; set; }

        [Key(4)]
        public string? StatusMessage { get; set; }
    }
}
