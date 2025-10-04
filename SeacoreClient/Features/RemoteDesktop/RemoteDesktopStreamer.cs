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
        private const int BytesPerPixel = 4;
        private const int PixelDifferenceThreshold = 45;
        private const int KeyFrameIntervalMilliseconds = 4000;
        private const double MaxDeltaCoverageBeforeKeyFrame = 0.45;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private static readonly object syncRoot = new();
        private static CancellationTokenSource? captureCts;
        private static int frameSequence;
        private static byte[]? previousFrameBuffer;
        private static int previousWidth;
        private static int previousHeight;
        private static int previousStride;
        private static long lastKeyFrameTimestamp;

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
                ResetFrameState();

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

            ResetFrameState();
        }

        private static async Task CaptureLoopAsync(TcpClientManager clientManager, CaptureSettings settings, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var frame = CaptureFrame(settings);
                        if (frame != null)
                        {
                            frame.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            await SendFrameAsync(clientManager, frame, token).ConfigureAwait(false);
                        }
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
                    ImageData = chunkBuffer,
                    IsDeltaFrame = frame.IsDeltaFrame,
                    IsKeyFrame = frame.IsKeyFrame,
                    OffsetX = frame.OffsetX,
                    OffsetY = frame.OffsetY,
                    RegionWidth = frame.RegionWidth,
                    RegionHeight = frame.RegionHeight
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
        private static RemoteDesktopFrameMessage? CaptureFrame(CaptureSettings settings)
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
                if (settings.MaxWidth > 0 || settings.MaxHeight > 0)
                {
                    double widthScale = settings.MaxWidth > 0 ? (double)settings.MaxWidth / screenWidth : double.PositiveInfinity;
                    double heightScale = settings.MaxHeight > 0 ? (double)settings.MaxHeight / screenHeight : double.PositiveInfinity;
                    double scale = Math.Min(Math.Min(widthScale, heightScale), 1.0);

                    if (scale < 0.995)
                    {
                        int targetWidth = Math.Max(1, (int)Math.Round(screenWidth * scale));
                        int targetHeight = Math.Max(1, (int)Math.Round(screenHeight * scale));
                        scaledBitmap = CreateScaledBitmap(bitmap, targetWidth, targetHeight);
                        sourceBitmap = scaledBitmap;
                    }
                }

                int stride;
                var currentPixels = ExtractPixels(sourceBitmap, out stride);
                bool hasPrevious = previousFrameBuffer != null
                    && previousWidth == sourceBitmap.Width
                    && previousHeight == sourceBitmap.Height
                    && previousStride == stride;

                bool forceKeyFrame = !hasPrevious
                    || Environment.TickCount64 - lastKeyFrameTimestamp >= KeyFrameIntervalMilliseconds;

                Rectangle deltaRegion = Rectangle.Empty;
                double coverage = 0;

                RemoteDesktopFrameMessage? message = null;

                if (forceKeyFrame)
                {
                    message = CreateKeyFrameMessage(sourceBitmap, screenWidth, screenHeight, settings.Quality);
                    lastKeyFrameTimestamp = Environment.TickCount64;
                }
                else if (hasPrevious && TryDetectDelta(previousFrameBuffer!, currentPixels, sourceBitmap.Width, sourceBitmap.Height, stride, out deltaRegion, out coverage))
                {
                    if (coverage >= MaxDeltaCoverageBeforeKeyFrame)
                    {
                        message = CreateKeyFrameMessage(sourceBitmap, screenWidth, screenHeight, settings.Quality);
                        lastKeyFrameTimestamp = Environment.TickCount64;
                    }
                    else
                    {
                        message = CreateDeltaMessage(sourceBitmap, screenWidth, screenHeight, settings.Quality, deltaRegion, coverage);
                    }
                }
                else if (!hasPrevious)
                {
                    message = CreateKeyFrameMessage(sourceBitmap, screenWidth, screenHeight, settings.Quality);
                    lastKeyFrameTimestamp = Environment.TickCount64;
                }

                previousFrameBuffer = currentPixels;
                previousWidth = sourceBitmap.Width;
                previousHeight = sourceBitmap.Height;
                previousStride = stride;

                return message;
            }
            finally
            {
                scaledBitmap?.Dispose();
            }
        }

        private static void ResetFrameState()
        {
            previousFrameBuffer = null;
            previousWidth = 0;
            previousHeight = 0;
            previousStride = 0;
            lastKeyFrameTimestamp = 0;
            frameSequence = 0;
        }

        private static byte[] ExtractPixels(Bitmap bitmap, out int stride)
        {
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var bitmapData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                stride = bitmapData.Stride;
                int byteCount = stride * bitmap.Height;
                var buffer = new byte[byteCount];
                Marshal.Copy(bitmapData.Scan0, buffer, 0, byteCount);
                return buffer;
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
        }

        private static RemoteDesktopFrameMessage CreateKeyFrameMessage(Bitmap bitmap, int screenWidth, int screenHeight, int quality)
        {
            var imageData = EncodeBitmapToJpeg(bitmap, quality);
            return new RemoteDesktopFrameMessage
            {
                ImageData = imageData,
                Width = bitmap.Width,
                Height = bitmap.Height,
                OriginalWidth = screenWidth,
                OriginalHeight = screenHeight,
                IsDeltaFrame = false,
                IsKeyFrame = true,
                OffsetX = 0,
                OffsetY = 0,
                RegionWidth = bitmap.Width,
                RegionHeight = bitmap.Height
            };
        }

        private static RemoteDesktopFrameMessage CreateDeltaMessage(Bitmap sourceBitmap, int screenWidth, int screenHeight, int quality, Rectangle region, double coverage)
        {
            using var deltaBitmap = sourceBitmap.Clone(region, PixelFormat.Format32bppArgb);
            int deltaQuality = coverage <= 0.1
                ? Math.Min(100, quality + 12)
                : Math.Min(100, quality + 5);
            var imageData = EncodeBitmapToJpeg(deltaBitmap, deltaQuality);

            return new RemoteDesktopFrameMessage
            {
                ImageData = imageData,
                Width = sourceBitmap.Width,
                Height = sourceBitmap.Height,
                OriginalWidth = screenWidth,
                OriginalHeight = screenHeight,
                IsDeltaFrame = true,
                IsKeyFrame = false,
                OffsetX = region.X,
                OffsetY = region.Y,
                RegionWidth = region.Width,
                RegionHeight = region.Height
            };
        }

        private static bool TryDetectDelta(byte[] previous, byte[] current, int width, int height, int stride, out Rectangle region, out double coverage)
        {
            int left = width;
            int right = -1;
            int top = height;
            int bottom = -1;
            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int index = rowOffset + x * BytesPerPixel;
                    int diffB = Math.Abs(current[index] - previous[index]);
                    int diffG = Math.Abs(current[index + 1] - previous[index + 1]);
                    int diffR = Math.Abs(current[index + 2] - previous[index + 2]);
                    int diffA = Math.Abs(current[index + 3] - previous[index + 3]);
                    int totalDiff = diffR + diffG + diffB + diffA;

                    if (totalDiff > PixelDifferenceThreshold)
                    {
                        if (x < left)
                        {
                            left = x;
                        }

                        if (x > right)
                        {
                            right = x;
                        }

                        if (y < top)
                        {
                            top = y;
                        }

                        if (y > bottom)
                        {
                            bottom = y;
                        }
                    }
                }
            }

            if (right < left || bottom < top)
            {
                region = Rectangle.Empty;
                coverage = 0;
                return false;
            }

            const int padding = 4;
            left = Math.Max(0, left - padding);
            top = Math.Max(0, top - padding);
            right = Math.Min(width - 1, right + padding);
            bottom = Math.Min(height - 1, bottom + padding);

            left = (left / 8) * 8;
            top = (top / 8) * 8;
            right = Math.Min(width - 1, ((right + 7) / 8) * 8);
            bottom = Math.Min(height - 1, ((bottom + 7) / 8) * 8);

            int regionWidth = Math.Max(1, right - left + 1);
            int regionHeight = Math.Max(1, bottom - top + 1);

            region = Rectangle.FromLTRB(left, top, left + regionWidth, top + regionHeight);
            coverage = (regionWidth * regionHeight) / (double)Math.Max(1, width * height);
            return true;
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
                Interval = Math.Max(80, request?.IntervalMilliseconds ?? 500);
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
