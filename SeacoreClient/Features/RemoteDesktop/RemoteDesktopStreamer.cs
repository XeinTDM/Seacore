using SeacoreClient.Core;
using SeacoreCommon.Messages;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace SeacoreClient.Features.RemoteDesktop
{
    public static class RemoteDesktopStreamer
    {
        private const int SmCxScreen = 0;
        private const int SmCyScreen = 1;
        private const int MaxChunkSize = 60 * 1024;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private static readonly object syncRoot = new();
        private static CancellationTokenSource? captureCts;
        private static int frameSequence;

        public static void Start(TcpClientManager clientManager, RemoteDesktopRequestMessage request)
        {
            if (clientManager is null)
            {
                throw new ArgumentNullException(nameof(clientManager));
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _ = SendStatusAsync(clientManager, "Remote desktop stream ended: Remote desktop is only supported on Windows clients.", CancellationToken.None);
                return;
            }

            lock (syncRoot)
            {
                StopInternal();

                captureCts = new CancellationTokenSource();
                var token = captureCts.Token;
                var settings = new CaptureSettings(request);

                Task.Run(async () => await CaptureLoopAsync(clientManager, settings, token), token);
            }
        }

        public static void Stop()
        {
            lock (syncRoot)
            {
                StopInternal();
            }
        }

        private static void StopInternal()
        {
            if (captureCts != null)
            {
                captureCts.Cancel();
                captureCts.Dispose();
                captureCts = null;
            }
        }

        private static async Task CaptureLoopAsync(TcpClientManager clientManager, CaptureSettings settings, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var frame = CaptureFrame(settings.Quality, settings.MaxWidth, settings.MaxHeight);
                        frame.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        await SendFrameAsync(clientManager, frame, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        await SendStatusAsync(clientManager, $"Remote desktop error: {ex.Message}", token).ConfigureAwait(false);
                        await Task.Delay(settings.Interval, token).ConfigureAwait(false);
                        continue;
                    }

                    await Task.Delay(settings.Interval, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
            finally
            {
                await SendStatusAsync(clientManager, "Remote desktop stream ended.", CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static async Task SendFrameAsync(TcpClientManager clientManager, RemoteDesktopFrameMessage frame, CancellationToken token)
        {
            var imageData = frame.ImageData ?? Array.Empty<byte>();
            int sequence = Interlocked.Increment(ref frameSequence);

            if (imageData.Length == 0)
            {
                frame.SequenceId = sequence;
                frame.ChunkIndex = 0;
                frame.TotalChunks = 1;
                await clientManager.SendMessageAsync(frame, token).ConfigureAwait(false);
                return;
            }

            int totalChunks = (imageData.Length + MaxChunkSize - 1) / MaxChunkSize;

            if (totalChunks <= 1)
            {
                frame.SequenceId = sequence;
                frame.ChunkIndex = 0;
                frame.TotalChunks = 1;
                await clientManager.SendMessageAsync(frame, token).ConfigureAwait(false);
                return;
            }

            for (int chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
            {
                int offset = chunkIndex * MaxChunkSize;
                int length = Math.Min(MaxChunkSize, imageData.Length - offset);
                var chunkBuffer = new byte[length];
                Buffer.BlockCopy(imageData, offset, chunkBuffer, 0, length);

                var chunkMessage = new RemoteDesktopFrameMessage
                {
                    SequenceId = sequence,
                    ChunkIndex = chunkIndex,
                    TotalChunks = totalChunks,
                    Width = frame.Width,
                    Height = frame.Height,
                    OriginalWidth = frame.OriginalWidth,
                    OriginalHeight = frame.OriginalHeight,
                    Timestamp = frame.Timestamp,
                    StatusMessage = chunkIndex == totalChunks - 1 ? frame.StatusMessage : null,
                    ImageData = chunkBuffer
                };

                await clientManager.SendMessageAsync(chunkMessage, token).ConfigureAwait(false);
            }

            frame.ImageData = Array.Empty<byte>();
        }

        private static async Task SendStatusAsync(TcpClientManager clientManager, string message, CancellationToken token)
        {
            try
            {
                var statusFrame = new RemoteDesktopFrameMessage
                {
                    StatusMessage = message,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    SequenceId = Interlocked.Increment(ref frameSequence),
                    ChunkIndex = 0,
                    TotalChunks = 1
                };

                await clientManager.SendMessageAsync(statusFrame, token).ConfigureAwait(false);
            }
            catch
            {
                // ignore send failures for status updates
            }
        }

        [SupportedOSPlatform("windows")]
        private static RemoteDesktopFrameMessage CaptureFrame(int quality, int maxWidth, int maxHeight)
        {
            int screenWidth = GetSystemMetrics(SmCxScreen);
            int screenHeight = GetSystemMetrics(SmCyScreen);

            if (screenWidth <= 0 || screenHeight <= 0)
            {
                throw new InvalidOperationException("Unable to determine primary screen dimensions for capture.");
            }

            using var bitmap = new Bitmap(screenWidth, screenHeight, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                var captureSize = new Size(screenWidth, screenHeight);
                graphics.CopyFromScreen(Point.Empty, Point.Empty, captureSize, CopyPixelOperation.SourceCopy);
            }

            Bitmap sourceBitmap = bitmap;
            Bitmap? scaledBitmap = null;

            try
            {
                if (maxWidth > 0 || maxHeight > 0)
                {
                    double widthScale = maxWidth > 0 ? (double)maxWidth / screenWidth : double.PositiveInfinity;
                    double heightScale = maxHeight > 0 ? (double)maxHeight / screenHeight : double.PositiveInfinity;
                    double scale = Math.Min(Math.Min(widthScale, heightScale), 1.0);

                    if (scale < 0.995)
                    {
                        int targetWidth = Math.Max(1, (int)Math.Round(screenWidth * scale));
                        int targetHeight = Math.Max(1, (int)Math.Round(screenHeight * scale));
                        scaledBitmap = CreateScaledBitmap(bitmap, targetWidth, targetHeight);
                        sourceBitmap = scaledBitmap;
                    }
                }

                var imageData = EncodeBitmapToJpeg(sourceBitmap, quality);

                return new RemoteDesktopFrameMessage
                {
                    ImageData = imageData,
                    Width = sourceBitmap.Width,
                    Height = sourceBitmap.Height,
                    OriginalWidth = screenWidth,
                    OriginalHeight = screenHeight
                };
            }
            finally
            {
                scaledBitmap?.Dispose();
            }
        }

        private static Bitmap CreateScaledBitmap(Bitmap source, int width, int height)
        {
            var scaled = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(scaled))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, width, height), new Rectangle(0, 0, source.Width, source.Height), GraphicsUnit.Pixel);
            }

            return scaled;
        }

        private static byte[] EncodeBitmapToJpeg(Bitmap bitmap, int quality)
        {
            using var memoryStream = new MemoryStream();
            var encoder = GetJpegEncoder();
            using var encoderParameters = new EncoderParameters(1);
            encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);

            bitmap.Save(memoryStream, encoder, encoderParameters);
            return memoryStream.ToArray();
        }

        private static ImageCodecInfo GetJpegEncoder()
        {
            var encoders = ImageCodecInfo.GetImageEncoders();
            foreach (var encoder in encoders)
            {
                if (encoder.FormatID == ImageFormat.Jpeg.Guid)
                {
                    return encoder;
                }
            }

            throw new InvalidOperationException("JPEG encoder not found.");
        }

        private sealed class CaptureSettings
        {
            public CaptureSettings(RemoteDesktopRequestMessage request)
            {
                Interval = Math.Max(100, request?.IntervalMilliseconds ?? 500);
                Quality = Math.Clamp(request?.JpegQuality ?? 70, 30, 100);
                MaxWidth = Math.Clamp(request?.MaxFrameWidth ?? 0, 0, 8192);
                MaxHeight = Math.Clamp(request?.MaxFrameHeight ?? 0, 0, 4320);
            }

            public int Interval { get; }
            public int Quality { get; }
            public int MaxWidth { get; }
            public int MaxHeight { get; }
        }
    }
}
