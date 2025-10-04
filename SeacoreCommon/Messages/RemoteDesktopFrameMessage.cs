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

        [Key(5)]
        public int SequenceId { get; set; }

        [Key(6)]
        public int ChunkIndex { get; set; }

        [Key(7)]
        public int TotalChunks { get; set; } = 1;

        [Key(8)]
        public int OriginalWidth { get; set; }

        [Key(9)]
        public int OriginalHeight { get; set; }

        [Key(10)]
        public bool IsDeltaFrame { get; set; }

        [Key(11)]
        public int OffsetX { get; set; }

        [Key(12)]
        public int OffsetY { get; set; }

        [Key(13)]
        public int RegionWidth { get; set; }

        [Key(14)]
        public int RegionHeight { get; set; }

        [Key(15)]
        public bool IsKeyFrame { get; set; }
    }
}
