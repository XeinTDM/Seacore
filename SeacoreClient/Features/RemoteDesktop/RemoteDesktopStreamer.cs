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
        private const double MinimumDeltaCoverageBeforeSend = 0.0015;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private static readonly object syncRoot = new();
        private static CancellationTokenSource? captureCts;
        private static int frameSequence;
        private static readonly ArrayPool<byte> frameBufferPool = ArrayPool<byte>.Shared;
        private static readonly object captureContextLock = new();
        private static ScreenCaptureContext? captureContext;
        private static readonly object regionContextLock = new();
        private static RegionCaptureContext? regionCaptureContext;
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
            var pacingState = new FramePacingState();

            try
            {
                while (!token.IsCancellationRequested)
                {
                    int intervalBeforeCapture = settings.Interval;
                    int wait = pacingState.GetDelay(intervalBeforeCapture);
                    if (wait > 0)
                    {
                        await Task.Delay(wait, token).ConfigureAwait(false);
                    }

                    RemoteDesktopFrameMessage? frame = null;
                    bool cancelled = false;
                    var frameStopwatch = Stopwatch.StartNew();

                    try
                    {
                        frame = CaptureFrame(settings);
                        if (frame != null)
                        {
                            frame.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            await SendFrameAsync(clientManager, frame, token).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        await SendStatusAsync(clientManager, $"Remote desktop error: {ex.Message}", token).ConfigureAwait(false);

                        try
                        {
                            await Task.Delay(Math.Min(1000, Math.Max(120, settings.Interval)), token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            cancelled = true;
                            break;
                        }
                    }
                    finally
                    {
                        frameStopwatch.Stop();
                        cancelled |= token.IsCancellationRequested;

                        if (!cancelled)
                        {
                            pacingState.Commit(intervalBeforeCapture);
                            settings.AdjustAfterFrame(frameStopwatch.Elapsed, frame);
                        }
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
                            deltaRegion = NormalizeDeltaRegion(deltaRegion, workingBitmap.Width, workingBitmap.Height);

                            if (!deltaRegion.IsEmpty)
                            {
                                double normalizedCoverage = Math.Max(coverage, (double)(deltaRegion.Width * deltaRegion.Height) / Math.Max(1, workingBitmap.Width * workingBitmap.Height));

                                if (normalizedCoverage >= MinimumDeltaCoverageBeforeSend)
                                {
                                    message = CreateDeltaMessage(workingBitmap, screenWidth, screenHeight, settings.Quality, deltaRegion, normalizedCoverage);
                                }
                            }
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

            lock (regionContextLock)
            {
                if (regionCaptureContext != null)
                {
                    regionCaptureContext.Dispose();
                    regionCaptureContext = null;
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
            int changeCount = 0;
            int totalPixels = Math.Max(1, width * height);
            int changeThreshold = (int)Math.Max(0, Math.Round(totalPixels * MaxDeltaCoverageBeforeKeyFrame));
            bool exceeded = false;

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

                                changeCount++;

                                if (changeThreshold > 0 && changeCount >= changeThreshold)
                                {
                                    exceeded = true;
                                    goto EndScan;
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

        EndScan:
            if (changeCount == 0 || right < left || bottom < top)
            {
                region = Rectangle.Empty;
                coverage = 0;
                return false;
            }

            if (exceeded)
            {
                region = new Rectangle(0, 0, width, height);
                coverage = changeCount / (double)totalPixels;
                return true;
            }

            region = Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
            coverage = changeCount / (double)totalPixels;
            return true;
        }

        private static Rectangle NormalizeDeltaRegion(Rectangle region, int width, int height)
        {
            if (region.IsEmpty)
            {
                return Rectangle.Empty;
            }

            region.Inflate(4, 4);
            region = Rectangle.Intersect(region, new Rectangle(0, 0, width, height));

            if (region.IsEmpty)
            {
                return Rectangle.Empty;
            }

            int left = Math.Max(0, (region.Left / 8) * 8);
            int top = Math.Max(0, (region.Top / 8) * 8);
            int right = Math.Min(width, ((region.Right + 7) / 8) * 8);
            int bottom = Math.Min(height, ((region.Bottom + 7) / 8) * 8);

            if (right <= left || bottom <= top)
            {
                return Rectangle.Empty;
            }

            return Rectangle.FromLTRB(left, top, right, bottom);
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
            if (region.Width <= 0 || region.Height <= 0)
            {
                return Array.Empty<byte>();
            }

            Bitmap target;

            lock (regionContextLock)
            {
                var context = EnsureRegionContext(region.Width, region.Height);
                target = context.Prepare(region.Width, region.Height);
                context.Graphics.DrawImage(bitmap, new Rectangle(0, 0, region.Width, region.Height), region, GraphicsUnit.Pixel);
            }

            return EncodeBitmapToJpeg(target, quality);
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

        private static RegionCaptureContext EnsureRegionContext(int width, int height)
        {
            var context = regionCaptureContext;
            if (context == null)
            {
                context = new RegionCaptureContext(width, height);
                regionCaptureContext = context;
                return context;
            }

            context.EnsureSize(width, height);
            return context;
        }

        private sealed class CaptureSettings
        {
            private readonly RollingAverage encodeDurations = new(12);
            private readonly RollingAverage payloadSizes = new(12);
            private int idleFrameCounter;

            public CaptureSettings(RemoteDesktopRequestMessage request)
            {
                int initialInterval = Math.Max(60, request?.IntervalMilliseconds ?? 500);
                BaseInterval = initialInterval;
                MinInterval = Math.Max(50, (int)Math.Round(initialInterval * 0.6));
                MaxInterval = Math.Min(800, (int)Math.Round(initialInterval * 1.8));
                Interval = initialInterval;

                int initialQuality = Math.Clamp(request?.JpegQuality ?? 70, 35, 100);
                BaseQuality = initialQuality;
                MinQuality = Math.Max(35, initialQuality - 25);
                MaxQuality = Math.Min(100, initialQuality + 15);
                Quality = initialQuality;

                MaxWidth = Math.Clamp(request?.MaxFrameWidth ?? 0, 0, 8192);
                MaxHeight = Math.Clamp(request?.MaxFrameHeight ?? 0, 0, 4320);
                EnableMouseControl = request?.EnableMouseControl ?? true;
                EnableKeyboardControl = request?.EnableKeyboardControl ?? true;
            }

            public int BaseInterval { get; }
            public int Interval { get; private set; }
            public int MinInterval { get; }
            public int MaxInterval { get; }
            public int BaseQuality { get; }
            public int Quality { get; private set; }
            public int MinQuality { get; }
            public int MaxQuality { get; }
            public int MaxWidth { get; }
            public int MaxHeight { get; }
            public bool EnableMouseControl { get; }
            public bool EnableKeyboardControl { get; }

            public void UpdateInterval(int interval) => Interval = Math.Clamp(interval, MinInterval, MaxInterval);
            public void UpdateQuality(int quality) => Quality = Math.Clamp(quality, MinQuality, MaxQuality);

            public void AdjustAfterFrame(TimeSpan encodeDuration, RemoteDesktopFrameMessage? frame)
            {
                double encodeMs = encodeDuration.TotalMilliseconds;
                if (encodeMs > 0)
                {
                    encodeDurations.Add(encodeMs);
                }

                if (frame?.ImageData is { Length: > 0 })
                {
                    payloadSizes.Add(frame.ImageData.Length);
                }

                if (frame == null)
                {
                    idleFrameCounter++;
                }
                else
                {
                    idleFrameCounter = 0;
                }

                if (frame != null && encodeDurations.Count >= 3)
                {
                    double avgEncode = encodeDurations.Average;

                    if (avgEncode > Interval * 0.85 && Interval < MaxInterval)
                    {
                        UpdateInterval((int)Math.Min(MaxInterval, Interval + Math.Max(5, (int)Math.Round(Interval * 0.12))));
                        encodeDurations.Clear();
                    }
                    else if (avgEncode < Interval * 0.55 && Interval > MinInterval)
                    {
                        UpdateInterval((int)Math.Max(MinInterval, Interval - Math.Max(4, (int)Math.Round(Interval * 0.1))));
                        encodeDurations.Clear();
                    }
                }

                if (frame != null && (!frame.IsDeltaFrame || frame.IsKeyFrame) && payloadSizes.Count >= 3)
                {
                    double avgPayload = payloadSizes.Average;

                    if (avgPayload > 225_000 && Quality > MinQuality)
                    {
                        UpdateQuality(Quality - 4);
                        payloadSizes.Clear();
                    }
                    else if (avgPayload < 140_000 && Quality < MaxQuality)
                    {
                        UpdateQuality(Quality + 3);
                        payloadSizes.Clear();
                    }
                }

                if (frame == null && idleFrameCounter >= 6 && Interval < MaxInterval)
                {
                    UpdateInterval((int)Math.Min(MaxInterval, Interval + Math.Max(6, (int)Math.Round(BaseInterval * 0.1))));
                    idleFrameCounter = Math.Min(idleFrameCounter, 6);
                }
                else if (frame != null && Interval > BaseInterval && idleFrameCounter == 0)
                {
                    UpdateInterval((int)Math.Max(BaseInterval, (int)Math.Round(Interval * 0.94)));
                }
            }
        }

        private sealed class ScreenCaptureContext : IDisposable
        {
            public ScreenCaptureContext(int width, int height)
            {
                Bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                Graphics = Graphics.FromImage(Bitmap);
                ConfigureGraphics(Graphics);
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

        private sealed class RegionCaptureContext : IDisposable
        {
            public RegionCaptureContext(int width, int height)
            {
                Bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                Graphics = Graphics.FromImage(Bitmap);
                ConfigureGraphics(Graphics);
            }

            public Bitmap Bitmap { get; private set; }
            public Graphics Graphics { get; private set; }
            public int Width => Bitmap.Width;
            public int Height => Bitmap.Height;

            public Bitmap Prepare(int width, int height)
            {
                EnsureSize(width, height);
                return Bitmap;
            }

            public void EnsureSize(int width, int height)
            {
                if (width <= Bitmap.Width && height <= Bitmap.Height)
                {
                    return;
                }

                Graphics.Dispose();
                Bitmap.Dispose();
                Bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                Graphics = Graphics.FromImage(Bitmap);
                ConfigureGraphics(Graphics);
            }

            public void Dispose()
            {
                Graphics.Dispose();
                Bitmap.Dispose();
            }
        }

        private sealed class FramePacingState
        {
            private readonly Stopwatch stopwatch = Stopwatch.StartNew();
            private long nextFrameTimestamp;

            public int GetDelay(int interval)
            {
                long now = stopwatch.ElapsedMilliseconds;
                if (nextFrameTimestamp <= now)
                {
                    return 0;
                }

                long delay = nextFrameTimestamp - now;
                return (int)Math.Max(0, Math.Min(delay, 1000));
            }

            public void Commit(int interval)
            {
                long now = stopwatch.ElapsedMilliseconds;
                long baseTime = Math.Max(now, nextFrameTimestamp);
                nextFrameTimestamp = baseTime + interval;
            }
        }

        private sealed class RollingAverage
        {
            private readonly double[] samples;
            private int index;
            private int count;
            private double total;

            public RollingAverage(int capacity)
            {
                if (capacity <= 0)
                {
                    capacity = 1;
                }

                samples = new double[capacity];
            }

            public int Count => count;
            public double Average => count == 0 ? 0 : total / count;

            public void Add(double value)
            {
                if (count < samples.Length)
                {
                    samples[count] = value;
                    total += value;
                    count++;
                    return;
                }

                total -= samples[index];
                samples[index] = value;
                total += value;
                index++;
                if (index >= samples.Length)
                {
                    index = 0;
                }
            }

            public void Clear()
            {
                Array.Clear(samples, 0, samples.Length);
                index = 0;
                count = 0;
                total = 0;
            }
        }

        private static void ConfigureGraphics(Graphics graphics)
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighSpeed;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.SmoothingMode = SmoothingMode.HighSpeed;
            graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;
        }
    }
}
