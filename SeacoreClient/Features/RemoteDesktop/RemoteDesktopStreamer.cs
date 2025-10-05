using SeacoreClient.Core;
using SeacoreCommon.Messages;
using System;
using System.Buffers;
using System.Diagnostics;
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
        private static readonly ArrayPool<byte> frameBufferPool = ArrayPool<byte>.Shared;
        private static readonly object captureContextLock = new();
        private static ScreenCaptureContext? captureContext;
        private static byte[]? previousFrameBuffer;
        private static byte[]? currentFrameBuffer;
        private static int previousWidth;
        private static int previousHeight;
        private static int previousStride;
        private static long lastKeyFrameTimestamp;
        private static ImageCodecInfo? jpegEncoder;

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
                    var frameStopwatch = Stopwatch.StartNew();
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

                    frameStopwatch.Stop();
                    int delay = Math.Max(0, settings.Interval - (int)frameStopwatch.ElapsedMilliseconds);
                    if (delay > 0)
                    {
                        await Task.Delay(delay, token).ConfigureAwait(false);
                    }
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

            RemoteDesktopFrameMessage? message = null;
            Bitmap? scaledBitmap = null;
            Bitmap sourceBitmap;
            Bitmap workingBitmap;
            int stride = 0;
            byte[]? currentPixels = null;
            int workingWidth = screenWidth;
            int workingHeight = screenHeight;

            lock (captureContextLock)
            {
                sourceBitmap = CaptureScreenBitmap(screenWidth, screenHeight);
                workingBitmap = sourceBitmap;

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
                            scaledBitmap = CreateScaledBitmap(sourceBitmap, targetWidth, targetHeight);
                            workingBitmap = scaledBitmap;
                        }
                    }

                    currentPixels = ExtractPixels(workingBitmap, ref currentFrameBuffer, out stride);
                    workingWidth = workingBitmap.Width;
                    workingHeight = workingBitmap.Height;

                    bool hasPrevious = previousFrameBuffer != null
                        && previousWidth == workingBitmap.Width
                        && previousHeight == workingBitmap.Height
                        && previousStride == stride;

                    bool forceKeyFrame = !hasPrevious
                        || Environment.TickCount64 - lastKeyFrameTimestamp >= KeyFrameIntervalMilliseconds;

                    Rectangle deltaRegion = Rectangle.Empty;
                    double coverage = 0;

                    if (forceKeyFrame)
                    {
                        message = CreateKeyFrameMessage(workingBitmap, screenWidth, screenHeight, settings.Quality);
                        lastKeyFrameTimestamp = Environment.TickCount64;
                    }
                    else if (hasPrevious && TryDetectDelta(previousFrameBuffer!, currentPixels, workingBitmap.Width, workingBitmap.Height, stride, out deltaRegion, out coverage))
                    {
                        if (coverage >= MaxDeltaCoverageBeforeKeyFrame)
                        {
                            message = CreateKeyFrameMessage(workingBitmap, screenWidth, screenHeight, settings.Quality);
                            lastKeyFrameTimestamp = Environment.TickCount64;
                        }
                        else
                        {
                            message = CreateDeltaMessage(workingBitmap, screenWidth, screenHeight, settings.Quality, deltaRegion, coverage);
                        }
                    }
                    else if (!hasPrevious)
                    {
                        message = CreateKeyFrameMessage(workingBitmap, screenWidth, screenHeight, settings.Quality);
                        lastKeyFrameTimestamp = Environment.TickCount64;
                    }
                }
                finally
                {
                    scaledBitmap?.Dispose();
                }

                if (currentPixels != null)
                {
                    SwapFrameBuffers(currentPixels);
                    previousWidth = workingWidth;
                    previousHeight = workingHeight;
                    previousStride = stride;
                }
            }

            return message;
        }

        private static void ResetFrameState()
        {
            var previousBuffer = previousFrameBuffer;
            var currentBuffer = currentFrameBuffer;
            previousFrameBuffer = null;
            currentFrameBuffer = null;
            previousWidth = 0;
            previousHeight = 0;
            previousStride = 0;
            lastKeyFrameTimestamp = 0;
            frameSequence = 0;
            if (previousBuffer != null)
            {
                frameBufferPool.Return(previousBuffer);
            }

            if (currentBuffer != null)
            {
                frameBufferPool.Return(currentBuffer);
            }

            lock (captureContextLock)
            {
                if (captureContext != null)
                {
                    captureContext.Dispose();
                    captureContext = null;
                }
            }
        }

        private static byte[] ExtractPixels(Bitmap bitmap, ref byte[]? buffer, out int stride)
        {
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var bitmapData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                stride = bitmapData.Stride;
                int byteCount = stride * bitmap.Height;
                EnsureBuffer(ref buffer, byteCount);
                Marshal.Copy(bitmapData.Scan0, buffer, 0, byteCount);
                return buffer;
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
        }

        private static RemoteDesktopFrameMessage CreateDeltaMessage(Bitmap sourceBitmap, int screenWidth, int screenHeight, int quality, Rectangle region, double coverage)
        {
            int deltaQuality = coverage <= 0.1
                ? Math.Min(100, quality + 12)
                : Math.Min(100, quality + 5);
            var imageData = EncodeRegionToJpeg(sourceBitmap, region, deltaQuality);

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
            unsafe
            {
                fixed (byte* previousPtr = previous)
                fixed (byte* currentPtr = current)
                {
                    byte* prevRow = previousPtr;
                    byte* currRow = currentPtr;
                    for (int y = 0; y < height; y++)
                    {
                        byte* prevPixel = prevRow;
                        byte* currPixel = currRow;

                        for (int x = 0; x < width; x++)
                        {
                            int diffB = currPixel[0] - prevPixel[0];
                            int diffG = currPixel[1] - prevPixel[1];
                            int diffR = currPixel[2] - prevPixel[2];
                            int diffA = currPixel[3] - prevPixel[3];
                            int totalDiff = Math.Abs(diffR) + Math.Abs(diffG) + Math.Abs(diffB) + Math.Abs(diffA);

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

                            prevPixel += BytesPerPixel;
                            currPixel += BytesPerPixel;
                        }

                        prevRow += stride;
                        currRow += stride;
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
                graphics.CompositingQuality = CompositingQuality.HighSpeed;
                graphics.InterpolationMode = InterpolationMode.Low;
                graphics.SmoothingMode = SmoothingMode.HighSpeed;
                graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                graphics.DrawImage(source, new Rectangle(0, 0, width, height), new Rectangle(0, 0, source.Width, source.Height), GraphicsUnit.Pixel);
            }

            return scaled;
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

        private static byte[] EncodeRegionToJpeg(Bitmap bitmap, Rectangle region, int quality)
        {
            using var regionBitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
            CopyRegion(bitmap, regionBitmap, region);
            return EncodeBitmapToJpeg(regionBitmap, quality);
        }

        private static unsafe void CopyRegion(Bitmap source, Bitmap destination, Rectangle region)
        {
            var srcData = source.LockBits(new Rectangle(0, 0, source.Width, source.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var dstData = destination.LockBits(new Rectangle(0, 0, destination.Width, destination.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            try
            {
                int bytesPerRow = region.Width * BytesPerPixel;
                byte* srcStart = (byte*)srcData.Scan0 + region.Y * srcData.Stride + region.X * BytesPerPixel;
                byte* dstStart = (byte*)dstData.Scan0;

                for (int y = 0; y < region.Height; y++)
                {
                    Buffer.MemoryCopy(srcStart, dstStart, bytesPerRow, bytesPerRow);
                    srcStart += srcData.Stride;
                    dstStart += dstData.Stride;
                }
            }
            finally
            {
                source.UnlockBits(srcData);
                destination.UnlockBits(dstData);
            }
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
            var encoder = jpegEncoder;
            if (encoder != null)
            {
                return encoder;
            }

            var encoders = ImageCodecInfo.GetImageEncoders();
            foreach (var candidate in encoders)
            {
                if (candidate.FormatID == ImageFormat.Jpeg.Guid)
                {
                    jpegEncoder = candidate;
                    return candidate;
                }
            }

            throw new InvalidOperationException("JPEG encoder not found.");
        }

        private static void EnsureBuffer(ref byte[]? buffer, int length)
        {
            if (buffer == null)
            {
                buffer = frameBufferPool.Rent(length);
                return;
            }

            if (buffer.Length < length)
            {
                frameBufferPool.Return(buffer);
                buffer = frameBufferPool.Rent(length);
            }
        }

        private static void SwapFrameBuffers(byte[] current)
        {
            var temp = previousFrameBuffer;
            previousFrameBuffer = current;
            currentFrameBuffer = temp;
        }

        private static Bitmap CaptureScreenBitmap(int width, int height)
        {
            var context = EnsureCaptureContext(width, height);
            var captureSize = new Size(width, height);
            context.Graphics.CopyFromScreen(Point.Empty, Point.Empty, captureSize, CopyPixelOperation.SourceCopy);
            return context.Bitmap;
        }

        private static ScreenCaptureContext EnsureCaptureContext(int width, int height)
        {
            var context = captureContext;
            if (context == null || context.Width != width || context.Height != height)
            {
                context?.Dispose();
                captureContext = new ScreenCaptureContext(width, height);
            }

            return captureContext!;
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

        private sealed class ScreenCaptureContext : IDisposable
        {
            public ScreenCaptureContext(int width, int height)
            {
                Bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                Graphics = Graphics.FromImage(Bitmap);
                Graphics.CompositingMode = CompositingMode.SourceCopy;
                Graphics.CompositingQuality = CompositingQuality.HighSpeed;
                Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                Graphics.SmoothingMode = SmoothingMode.HighSpeed;
                Graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                Width = width;
                Height = height;
            }

            public Bitmap Bitmap { get; }
            public Graphics Graphics { get; }
            public int Width { get; }
            public int Height { get; }

            public void Dispose()
            {
                Graphics.Dispose();
                Bitmap.Dispose();
            }
        }
    }
}
