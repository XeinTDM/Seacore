using SeacoreClient.Core;
using SeacoreCommon.Messages;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SeacoreClient.Features.RemoteDesktop
{
    public static class RemoteDesktopStreamer
    {
        private static readonly object syncRoot = new();
        private static CancellationTokenSource? captureCts;

        public static void Start(TcpClientManager clientManager, RemoteDesktopRequestMessage request)
        {
            if (clientManager is null)
            {
                throw new ArgumentNullException(nameof(clientManager));
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _ = SendStatusAsync(clientManager, "Remote desktop is only supported on Windows clients.", CancellationToken.None);
                return;
            }

            lock (syncRoot)
            {
                StopInternal();

                captureCts = new CancellationTokenSource();
                var token = captureCts.Token;

                Task.Run(async () => await CaptureLoopAsync(clientManager, request, token), token);
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

        private static async Task CaptureLoopAsync(TcpClientManager clientManager, RemoteDesktopRequestMessage request, CancellationToken token)
        {
            int interval = Math.Max(100, request.IntervalMilliseconds);
            int quality = Math.Clamp(request.JpegQuality, 30, 100);

            try
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var frame = CaptureFrame(quality);
                        frame.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        await clientManager.SendMessageAsync(frame, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        await SendStatusAsync(clientManager, $"Remote desktop error: {ex.Message}", token);
                        await Task.Delay(interval, token);
                        continue;
                    }

                    await Task.Delay(interval, token);
                }
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
            finally
            {
                await SendStatusAsync(clientManager, "Remote desktop stream ended.", CancellationToken.None);
            }
        }

        private static RemoteDesktopFrameMessage CaptureFrame(int quality)
        {
            var screen = Screen.PrimaryScreen ?? throw new InvalidOperationException("No primary screen available for capture.");
            var bounds = screen.Bounds;

            using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            }

            using var memoryStream = new MemoryStream();
            var encoder = GetJpegEncoder();
            using var encoderParameters = new EncoderParameters(1);
            encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);

            bitmap.Save(memoryStream, encoder, encoderParameters);

            return new RemoteDesktopFrameMessage
            {
                ImageData = memoryStream.ToArray(),
                Width = bounds.Width,
                Height = bounds.Height
            };
        }

        private static async Task SendStatusAsync(TcpClientManager clientManager, string message, CancellationToken token)
        {
            try
            {
                var statusFrame = new RemoteDesktopFrameMessage
                {
                    StatusMessage = message,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                await clientManager.SendMessageAsync(statusFrame, token);
            }
            catch
            {
                // ignore send failures for status updates
            }
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
    }
}
