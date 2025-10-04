using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using Seacore.Resources.Core;
using Seacore.Resources.Usercontrols.Clients;
using SeacoreCommon.Messages;

namespace Seacore.Resources.Usercontrols.Clients.Windows.Control.RemoteDesktop
{
    public sealed class RemoteDesktopSessionManager
    {
        private sealed class RemoteDesktopSession
        {
            private sealed class FrameAssembly
            {
                private readonly byte[][] chunks;
                private int receivedChunks;
                private int totalBytes;

                public FrameAssembly(int sequenceId, int totalChunks)
                {
                    SequenceId = sequenceId;
                    chunks = new byte[totalChunks][];
                }

                public int SequenceId { get; }
                public int Width { get; set; }
                public int Height { get; set; }
                public int OriginalWidth { get; set; }
                public int OriginalHeight { get; set; }
                public long Timestamp { get; set; }
                public string? StatusMessage { get; set; }
                public bool IsDeltaFrame { get; set; }
                public bool IsKeyFrame { get; set; }
                public int OffsetX { get; set; }
                public int OffsetY { get; set; }
                public int RegionWidth { get; set; }
                public int RegionHeight { get; set; }

                public bool TryRegisterChunk(int index, byte[]? data)
                {
                    if (index < 0 || index >= chunks.Length)
                    {
                        return false;
                    }

                    if (chunks[index] != null)
                    {
                        return receivedChunks == chunks.Length;
                    }

                    chunks[index] = data ?? Array.Empty<byte>();
                    receivedChunks++;
                    totalBytes += chunks[index].Length;
                    return receivedChunks == chunks.Length;
                }

                public RemoteDesktopFrameMessage ToFrameMessage()
                {
                    var buffer = new byte[totalBytes];
                    int offset = 0;

                    for (int i = 0; i < chunks.Length; i++)
                    {
                        var chunk = chunks[i];
                        if (chunk == null || chunk.Length == 0)
                        {
                            continue;
                        }

                        Buffer.BlockCopy(chunk, 0, buffer, offset, chunk.Length);
                        offset += chunk.Length;
                    }

                    return new RemoteDesktopFrameMessage
                    {
                        ImageData = buffer,
                        Width = Width,
                        Height = Height,
                        SequenceId = SequenceId,
                        OriginalWidth = OriginalWidth,
                        OriginalHeight = OriginalHeight,
                        Timestamp = Timestamp,
                        StatusMessage = StatusMessage,
                        IsDeltaFrame = IsDeltaFrame,
                        IsKeyFrame = IsKeyFrame,
                        OffsetX = OffsetX,
                        OffsetY = OffsetY,
                        RegionWidth = RegionWidth,
                        RegionHeight = RegionHeight
                    };
                }
            }

            private readonly Dictionary<int, FrameAssembly> pendingFrames = new();

            public HashSet<IRemoteDesktopSubscriber> Subscribers { get; } = new();
            public bool IsActive { get; set; }
            public RemoteDesktopRequestMessage? LastRequest { get; set; }

            public RemoteDesktopFrameMessage? ProcessFrame(RemoteDesktopFrameMessage frame)
            {
                if (frame.TotalChunks <= 1 || frame.ImageData?.Length == 0)
                {
                    frame.OriginalWidth = frame.OriginalWidth > 0 ? frame.OriginalWidth : frame.Width;
                    frame.OriginalHeight = frame.OriginalHeight > 0 ? frame.OriginalHeight : frame.Height;
                    return frame;
                }

                if (frame.ChunkIndex < 0 || frame.ChunkIndex >= frame.TotalChunks)
                {
                    return null;
                }

                if (!pendingFrames.TryGetValue(frame.SequenceId, out var assembly))
                {
                    if (pendingFrames.Count > 64)
                    {
                        pendingFrames.Clear();
                    }

                    assembly = new FrameAssembly(frame.SequenceId, frame.TotalChunks)
                    {
                        Width = frame.Width,
                        Height = frame.Height,
                        OriginalWidth = frame.OriginalWidth > 0 ? frame.OriginalWidth : frame.Width,
                        OriginalHeight = frame.OriginalHeight > 0 ? frame.OriginalHeight : frame.Height,
                        Timestamp = frame.Timestamp,
                        StatusMessage = frame.StatusMessage,
                        IsDeltaFrame = frame.IsDeltaFrame,
                        IsKeyFrame = frame.IsKeyFrame,
                        OffsetX = frame.OffsetX,
                        OffsetY = frame.OffsetY,
                        RegionWidth = frame.RegionWidth,
                        RegionHeight = frame.RegionHeight
                    };

                    pendingFrames[frame.SequenceId] = assembly;
                }

                assembly.Width = frame.Width > 0 ? frame.Width : assembly.Width;
                assembly.Height = frame.Height > 0 ? frame.Height : assembly.Height;
                assembly.OriginalWidth = frame.OriginalWidth > 0 ? frame.OriginalWidth : assembly.OriginalWidth;
                assembly.OriginalHeight = frame.OriginalHeight > 0 ? frame.OriginalHeight : assembly.OriginalHeight;
                assembly.Timestamp = frame.Timestamp > 0 ? frame.Timestamp : assembly.Timestamp;
                assembly.IsDeltaFrame = frame.IsDeltaFrame;
                assembly.IsKeyFrame = frame.IsKeyFrame || frame.IsDeltaFrame == false;
                assembly.OffsetX = frame.OffsetX != 0 || frame.RegionWidth > 0 ? frame.OffsetX : assembly.OffsetX;
                assembly.OffsetY = frame.OffsetY != 0 || frame.RegionHeight > 0 ? frame.OffsetY : assembly.OffsetY;
                assembly.RegionWidth = frame.RegionWidth > 0 ? frame.RegionWidth : assembly.RegionWidth;
                assembly.RegionHeight = frame.RegionHeight > 0 ? frame.RegionHeight : assembly.RegionHeight;

                if (!string.IsNullOrWhiteSpace(frame.StatusMessage))
                {
                    assembly.StatusMessage = frame.StatusMessage;
                }

                if (!assembly.TryRegisterChunk(frame.ChunkIndex, frame.ImageData))
                {
                    return null;
                }

                pendingFrames.Remove(frame.SequenceId);
                return assembly.ToFrameMessage();
            }

            public void ResetAssemblies()
            {
                pendingFrames.Clear();
            }
        }

        private static readonly Lazy<RemoteDesktopSessionManager> instance = new(() => new RemoteDesktopSessionManager());
        public static RemoteDesktopSessionManager Instance => instance.Value;

        private readonly object syncRoot = new();
        private readonly Dictionary<ClientInfo, RemoteDesktopSession> sessions = new();

        private RemoteDesktopSessionManager()
        {
        }

        public void Register(ClientInfo clientInfo, IRemoteDesktopSubscriber subscriber)
        {
            if (clientInfo is null)
            {
                throw new ArgumentNullException(nameof(clientInfo));
            }

            if (subscriber is null)
            {
                throw new ArgumentNullException(nameof(subscriber));
            }

            lock (syncRoot)
            {
                if (!sessions.TryGetValue(clientInfo, out var session))
                {
                    session = new RemoteDesktopSession();
                    sessions[clientInfo] = session;
                }

                bool added = session.Subscribers.Add(subscriber);

                if (session.IsActive && added)
                {
                    subscriber.OnSessionStarted(clientInfo);
                }
            }
        }

        public void Unregister(ClientInfo clientInfo, IRemoteDesktopSubscriber subscriber, string reason = "Subscriber removed")
        {
            if (clientInfo is null || subscriber is null)
            {
                return;
            }

            lock (syncRoot)
            {
                if (!sessions.TryGetValue(clientInfo, out var session))
                {
                    return;
                }

                if (!session.Subscribers.Remove(subscriber))
                {
                    return;
                }

                subscriber.OnSessionStopped(clientInfo, reason);

                if (session.Subscribers.Count == 0)
                {
                    if (session.IsActive)
                    {
                        StopSessionInternal(clientInfo, session, reason);
                    }

                    sessions.Remove(clientInfo);
                }
            }
        }

        public void StartSession(ClientInfo clientInfo, RemoteDesktopRequestMessage? request = null)
        {
            if (clientInfo is null)
            {
                throw new ArgumentNullException(nameof(clientInfo));
            }

            lock (syncRoot)
            {
                if (!sessions.TryGetValue(clientInfo, out var session))
                {
                    session = new RemoteDesktopSession();
                    sessions[clientInfo] = session;
                }

                var effectiveRequest = request ?? session.LastRequest ?? new RemoteDesktopRequestMessage();
                var normalizedRequest = NormalizeRequest(effectiveRequest, true);

                if (session.IsActive && session.LastRequest != null && !RequestsDiffer(session.LastRequest, normalizedRequest))
                {
                    return;
                }

                session.LastRequest = normalizedRequest;

                if (session.IsActive)
                {
                    // Allow updating capture settings for an active session
                    session.ResetAssemblies();
                    SendRequest(clientInfo.TcpClient, NormalizeRequest(session.LastRequest, true));
                    return;
                }

                StartSessionInternal(clientInfo, session, session.LastRequest);
            }
        }

        public void StopSession(ClientInfo clientInfo, string reason = "Session stopped")
        {
            if (clientInfo is null)
            {
                return;
            }

            lock (syncRoot)
            {
                if (!sessions.TryGetValue(clientInfo, out var session))
                {
                    return;
                }

                if (session.IsActive)
                {
                    StopSessionInternal(clientInfo, session, reason);
                }
            }
        }

        public void HandleFrame(ClientInfo clientInfo, RemoteDesktopFrameMessage frame)
        {
            if (clientInfo is null)
            {
                throw new ArgumentNullException(nameof(clientInfo));
            }

            if (frame is null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            List<IRemoteDesktopSubscriber> subscribers;
            RemoteDesktopFrameMessage? processedFrame;

            lock (syncRoot)
            {
                if (!sessions.TryGetValue(clientInfo, out var session))
                {
                    return;
                }

                processedFrame = session.ProcessFrame(frame);

                if (processedFrame is null)
                {
                    return;
                }

                subscribers = session.Subscribers.ToList();

                if (!string.IsNullOrWhiteSpace(processedFrame.StatusMessage) && processedFrame.StatusMessage.Contains("ended", StringComparison.OrdinalIgnoreCase))
                {
                    session.IsActive = false;
                    session.ResetAssemblies();
                }
            }

            foreach (var subscriber in subscribers)
            {
                subscriber.OnFrameReceived(clientInfo, processedFrame);
            }
        }

        private static void StartSessionInternal(ClientInfo clientInfo, RemoteDesktopSession session, RemoteDesktopRequestMessage? request)
        {
            session.ResetAssemblies();
            session.IsActive = true;

            var normalized = NormalizeRequest(request, true);
            foreach (var subscriber in session.Subscribers)
            {
                subscriber.OnSessionStarted(clientInfo);
            }

            SendRequest(clientInfo.TcpClient, normalized);
        }

        private static void StopSessionInternal(ClientInfo clientInfo, RemoteDesktopSession session, string reason)
        {
            session.ResetAssemblies();
            session.IsActive = false;

            var stopMessage = new RemoteDesktopRequestMessage
            {
                IsStart = false,
                IntervalMilliseconds = session.LastRequest?.IntervalMilliseconds ?? 500,
                JpegQuality = session.LastRequest?.JpegQuality ?? 70,
                MaxFrameWidth = session.LastRequest?.MaxFrameWidth ?? 0,
                MaxFrameHeight = session.LastRequest?.MaxFrameHeight ?? 0
            };

            SendRequest(clientInfo.TcpClient, stopMessage);

            foreach (var subscriber in session.Subscribers)
            {
                subscriber.OnSessionStopped(clientInfo, reason);
            }
        }

        private static RemoteDesktopRequestMessage NormalizeRequest(RemoteDesktopRequestMessage? request, bool isStart)
        {
            int interval = Math.Max(100, request?.IntervalMilliseconds ?? 500);
            int quality = Math.Clamp(request?.JpegQuality ?? 70, 30, 100);
            int maxWidth = Math.Clamp(request?.MaxFrameWidth ?? 0, 0, 8192);
            int maxHeight = Math.Clamp(request?.MaxFrameHeight ?? 0, 0, 4320);

            return new RemoteDesktopRequestMessage
            {
                IsStart = isStart,
                IntervalMilliseconds = interval,
                JpegQuality = quality,
                MaxFrameWidth = maxWidth,
                MaxFrameHeight = maxHeight
            };
        }

        private static bool RequestsDiffer(RemoteDesktopRequestMessage existing, RemoteDesktopRequestMessage updated)
        {
            if (existing is null)
            {
                return true;
            }

            return existing.IntervalMilliseconds != updated.IntervalMilliseconds
                || existing.JpegQuality != updated.JpegQuality
                || existing.MaxFrameWidth != updated.MaxFrameWidth
                || existing.MaxFrameHeight != updated.MaxFrameHeight;
        }

        private static void SendRequest(TcpClient client, RemoteDesktopRequestMessage request)
        {
            if (client?.Connected != true)
            {
                return;
            }

            var stream = client.GetStream();
            TcpConnectionManager.SendMessage(stream, request);
        }
    }
}
