using Seacore.Resources.Styles.Behaviours;
using Seacore.Resources.Usercontrols.Clients;
using SeacoreCommon.Messages;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Seacore.Resources.Usercontrols.Clients.Windows.Control.RemoteDesktop
{
    public partial class RemoteDesktopWindow : Window, IRemoteDesktopSubscriber
    {
        private const int MinStreamWidth = 320;
        private const int MinStreamHeight = 240;
        private const int MaxStreamWidth = 3840;
        private const int MaxStreamHeight = 2160;

        private readonly ClientInfo clientInfo;
        private readonly DispatcherTimer resizeThrottleTimer;
        private readonly RemoteDesktopRequestMessage currentRequest = new RemoteDesktopRequestMessage
        {
            IntervalMilliseconds = 250,
            JpegQuality = 70,
            MaxFrameWidth = 1280,
            MaxFrameHeight = 720
        };

        private long lastFrameTimestamp;
        private WriteableBitmap? currentBitmap;
        private bool awaitingKeyFrame = true;

        public RemoteDesktopWindow(ClientInfo clientInfo)
        {
            this.clientInfo = clientInfo ?? throw new ArgumentNullException(nameof(clientInfo));

            resizeThrottleTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(350),
                IsEnabled = false
            };
            resizeThrottleTimer.Tick += ResizeThrottleTimer_Tick;

            InitializeComponent();

            Loaded += RemoteDesktopWindow_Loaded;
            Closed += RemoteDesktopWindow_Closed;
            SizeChanged += RemoteDesktopWindow_SizeChanged;
            imageScrollViewer.SizeChanged += ImageScrollViewer_SizeChanged;

            clientNameTextBlock.Text = string.IsNullOrWhiteSpace(clientInfo.Username)
                ? "Remote Desktop"
                : $"Remote Desktop - {clientInfo.Username}";
        }

        private void RemoteDesktopWindow_Loaded(object sender, RoutedEventArgs e)
        {
            statusTextBlock.Text = "Requesting remote desktop stream...";
            RemoteDesktopSessionManager.Instance.Register(clientInfo, this);
            RemoteDesktopSessionManager.Instance.StartSession(clientInfo, CreateRequestSnapshot());

            Dispatcher.BeginInvoke(new Action(() => RecalculateCaptureRequest(force: true)), DispatcherPriority.Render);
        }

        private void RemoteDesktopWindow_Closed(object? sender, EventArgs e)
        {
            resizeThrottleTimer.Stop();
            RemoteDesktopSessionManager.Instance.StopSession(clientInfo, "Window closed");
            RemoteDesktopSessionManager.Instance.Unregister(clientInfo, this, "Window closed");
        }

        public void OnFrameReceived(ClientInfo client, RemoteDesktopFrameMessage frame)
        {
            if (!ReferenceEquals(client, clientInfo))
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                if (frame.ImageData?.Length > 0)
                {
                    try
                    {
                        if (!TryRenderFrame(frame))
                        {
                            statusTextBlock.Text = "Awaiting key frame...";
                            return;
                        }

                        long frameTimestamp = frame.Timestamp > 0
                            ? frame.Timestamp
                            : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        var timestampLocal = DateTimeOffset.FromUnixTimeMilliseconds(frameTimestamp).LocalDateTime;

                        double? fps = null;
                        if (lastFrameTimestamp > 0 && frameTimestamp > lastFrameTimestamp)
                        {
                            double frameDelta = frameTimestamp - lastFrameTimestamp;
                            if (frameDelta > 0)
                            {
                                fps = 1000.0 / frameDelta;
                            }
                        }

                        lastFrameTimestamp = frameTimestamp;

                        int displayWidth = frame.Width > 0 ? frame.Width : currentBitmap?.PixelWidth ?? frame.Width;
                        int displayHeight = frame.Height > 0 ? frame.Height : currentBitmap?.PixelHeight ?? frame.Height;
                        int sourceWidth = frame.OriginalWidth > 0 ? frame.OriginalWidth : displayWidth;
                        int sourceHeight = frame.OriginalHeight > 0 ? frame.OriginalHeight : displayHeight;

                        string status = $"Stream: {displayWidth}x{displayHeight}";
                        if (sourceWidth != displayWidth || sourceHeight != displayHeight)
                        {
                            status += $" (source {sourceWidth}x{sourceHeight})";
                        }

                        if (fps.HasValue)
                        {
                            status += $" • ~{fps.Value:0.0} fps";
                        }

                        if (frame.IsDeltaFrame && frame.RegionWidth > 0 && frame.RegionHeight > 0)
                        {
                            double coverage = (frame.RegionWidth * frame.RegionHeight) / (double)Math.Max(1, displayWidth * displayHeight);
                            status += $" • Δ {coverage * 100:0.#}%";
                        }

                        status += $" • Updated: {timestampLocal:HH:mm:ss}";

                        if (!string.IsNullOrWhiteSpace(frame.StatusMessage))
                        {
                            status += $" • {frame.StatusMessage}";
                        }

                        statusTextBlock.Text = status;
                    }
                    catch (Exception ex)
                    {
                        statusTextBlock.Text = $"Failed to render frame: {ex.Message}";
                    }
                }
                else if (!string.IsNullOrWhiteSpace(frame.StatusMessage))
                {
                    remoteDesktopImage.Source = null;
                    currentBitmap = null;
                    statusTextBlock.Text = frame.StatusMessage;
                    lastFrameTimestamp = 0;
                    awaitingKeyFrame = true;
                }
            });
        }

        public void OnSessionStarted(ClientInfo client)
        {
            if (!ReferenceEquals(client, clientInfo))
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                statusTextBlock.Text = "Awaiting first frame...";
                lastFrameTimestamp = 0;
                awaitingKeyFrame = true;
                currentBitmap = null;
            });
        }

        public void OnSessionStopped(ClientInfo client, string reason)
        {
            if (!ReferenceEquals(client, clientInfo))
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    statusTextBlock.Text = reason;
                }

                lastFrameTimestamp = 0;
                awaitingKeyFrame = true;
                currentBitmap = null;
            });
        }

        private bool TryRenderFrame(RemoteDesktopFrameMessage frame)
        {
            var bitmapSource = DecodeBitmap(frame.ImageData);
            if (bitmapSource is null)
            {
                return false;
            }

            if (!frame.IsDeltaFrame || frame.IsKeyFrame)
            {
                return RenderKeyFrame(frame, bitmapSource);
            }

            if (awaitingKeyFrame || currentBitmap is null)
            {
                awaitingKeyFrame = true;
                return false;
            }

            if (frame.Width > 0 && frame.Height > 0
                && (frame.Width != currentBitmap.PixelWidth || frame.Height != currentBitmap.PixelHeight))
            {
                return RenderKeyFrame(frame, bitmapSource);
            }

            if (frame.RegionWidth <= 0 || frame.RegionHeight <= 0)
            {
                return true;
            }

            try
            {
                var formatted = new FormatConvertedBitmap(bitmapSource, PixelFormats.Pbgra32, null, 0);
                formatted.Freeze();

                int regionWidth = Math.Min(formatted.PixelWidth, frame.RegionWidth);
                int regionHeight = Math.Min(formatted.PixelHeight, frame.RegionHeight);
                if (regionWidth <= 0 || regionHeight <= 0)
                {
                    return true;
                }

                int stride = (formatted.Format.BitsPerPixel * regionWidth + 7) / 8;
                var pixelData = new byte[stride * regionHeight];
                formatted.CopyPixels(new Int32Rect(0, 0, regionWidth, regionHeight), pixelData, stride, 0);

                var updateRect = new Int32Rect(
                    Math.Clamp(frame.OffsetX, 0, Math.Max(0, currentBitmap.PixelWidth - 1)),
                    Math.Clamp(frame.OffsetY, 0, Math.Max(0, currentBitmap.PixelHeight - 1)),
                    regionWidth,
                    regionHeight);

                updateRect.Width = Math.Min(updateRect.Width, currentBitmap.PixelWidth - updateRect.X);
                updateRect.Height = Math.Min(updateRect.Height, currentBitmap.PixelHeight - updateRect.Y);

                if (updateRect.Width <= 0 || updateRect.Height <= 0)
                {
                    return true;
                }

                currentBitmap.WritePixels(updateRect, pixelData, stride, 0);
                remoteDesktopImage.Source = currentBitmap;
                awaitingKeyFrame = false;
                return true;
            }
            catch
            {
                return RenderKeyFrame(frame, bitmapSource);
            }
        }

        private bool RenderKeyFrame(RemoteDesktopFrameMessage frame, BitmapSource bitmapSource)
        {
            var formatted = new FormatConvertedBitmap(bitmapSource, PixelFormats.Pbgra32, null, 0);
            formatted.Freeze();

            int width = frame.Width > 0 ? frame.Width : formatted.PixelWidth;
            int height = frame.Height > 0 ? frame.Height : formatted.PixelHeight;

            if (width <= 0 || height <= 0)
            {
                awaitingKeyFrame = true;
                return false;
            }

            currentBitmap = new WriteableBitmap(width, height, formatted.DpiX, formatted.DpiY, PixelFormats.Pbgra32, null);
            int stride = (formatted.Format.BitsPerPixel * formatted.PixelWidth + 7) / 8;
            var buffer = new byte[stride * formatted.PixelHeight];
            formatted.CopyPixels(buffer, stride, 0);
            currentBitmap.WritePixels(new Int32Rect(0, 0, formatted.PixelWidth, formatted.PixelHeight), buffer, stride, 0);

            remoteDesktopImage.Source = currentBitmap;
            awaitingKeyFrame = false;
            return true;
        }

        private static BitmapSource? DecodeBitmap(byte[] data)
        {
            try
            {
                using var stream = new MemoryStream(data);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private void RemoteDesktopWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ScheduleRequestRecalculation();
        }

        private void ImageScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ScheduleRequestRecalculation();
        }

        private void ResizeThrottleTimer_Tick(object? sender, EventArgs e)
        {
            resizeThrottleTimer.Stop();
            RecalculateCaptureRequest();
        }

        private void ScheduleRequestRecalculation()
        {
            if (!IsLoaded)
            {
                return;
            }

            resizeThrottleTimer.Stop();
            resizeThrottleTimer.Start();
        }

        private void RecalculateCaptureRequest(bool force = false)
        {
            Size viewport = GetViewportPixelSize();

            int targetWidth = (int)Math.Round(viewport.Width);
            int targetHeight = (int)Math.Round(viewport.Height);

            targetWidth = Math.Clamp(targetWidth, MinStreamWidth, MaxStreamWidth);
            targetHeight = Math.Clamp(targetHeight, MinStreamHeight, MaxStreamHeight);

            int nextQuality = CalculateQuality(targetWidth, targetHeight);
            int nextInterval = CalculateInterval(targetWidth, targetHeight);

            if (!force
                && currentRequest.MaxFrameWidth == targetWidth
                && currentRequest.MaxFrameHeight == targetHeight
                && currentRequest.JpegQuality == nextQuality
                && currentRequest.IntervalMilliseconds == nextInterval)
            {
                return;
            }

            currentRequest.MaxFrameWidth = targetWidth;
            currentRequest.MaxFrameHeight = targetHeight;
            currentRequest.JpegQuality = nextQuality;
            currentRequest.IntervalMilliseconds = nextInterval;

            RemoteDesktopSessionManager.Instance.StartSession(clientInfo, CreateRequestSnapshot());
        }

        private Size GetViewportPixelSize()
        {
            double width = imageScrollViewer.ViewportWidth;
            double height = imageScrollViewer.ViewportHeight;

            if (double.IsNaN(width) || width <= 1)
            {
                width = imageScrollViewer.ActualWidth;
            }

            if (double.IsNaN(height) || height <= 1)
            {
                height = imageScrollViewer.ActualHeight;
            }

            if (double.IsNaN(width) || width <= 1)
            {
                width = ActualWidth - 40;
            }

            if (double.IsNaN(height) || height <= 1)
            {
                height = ActualHeight - 80;
            }

            width = Math.Max(width, MinStreamWidth);
            height = Math.Max(height, MinStreamHeight);

            var source = PresentationSource.FromVisual(this);
            double dpiX = 1.0;
            double dpiY = 1.0;

            if (source?.CompositionTarget != null)
            {
                dpiX = source.CompositionTarget.TransformToDevice.M11;
                dpiY = source.CompositionTarget.TransformToDevice.M22;
            }

            return new Size(width * dpiX, height * dpiY);
        }

        private static int CalculateQuality(int width, int height)
        {
            double megapixels = (width * height) / 1_000_000d;

            if (megapixels >= 3.5)
            {
                return 60;
            }

            if (megapixels >= 2.0)
            {
                return 68;
            }

            if (megapixels >= 1.0)
            {
                return 75;
            }

            return 82;
        }

        private static int CalculateInterval(int width, int height)
        {
            double megapixels = (width * height) / 1_000_000d;
            int interval;

            if (megapixels >= 3.5)
            {
                interval = 320;
            }
            else if (megapixels >= 2.0)
            {
                interval = 220;
            }
            else if (megapixels >= 1.0)
            {
                interval = 150;
            }
            else
            {
                interval = 110;
            }

            return Math.Max(80, interval);
        }

        private RemoteDesktopRequestMessage CreateRequestSnapshot()
        {
            return new RemoteDesktopRequestMessage
            {
                IntervalMilliseconds = currentRequest.IntervalMilliseconds,
                JpegQuality = currentRequest.JpegQuality,
                MaxFrameWidth = currentRequest.MaxFrameWidth,
                MaxFrameHeight = currentRequest.MaxFrameHeight
            };
        }

        #region Behaviours
        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            WindowBehaviours.Border_MouseLeftButtonDown(sender, e, this);
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowBehaviours.MinimizeButton_Click(sender, e, this);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            WindowBehaviours.CloseButton_Click(sender, e, this);
        }
        #endregion
    }
}
