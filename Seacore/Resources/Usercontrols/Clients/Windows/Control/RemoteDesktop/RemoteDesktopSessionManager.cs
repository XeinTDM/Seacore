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
            public HashSet<IRemoteDesktopSubscriber> Subscribers { get; } = new();
            public bool IsActive { get; set; }
            public RemoteDesktopRequestMessage? LastRequest { get; set; }
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

                session.LastRequest = request ?? session.LastRequest ?? new RemoteDesktopRequestMessage();

                if (session.IsActive)
                {
                    // Allow updating capture settings for an active session
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

            lock (syncRoot)
            {
                if (!sessions.TryGetValue(clientInfo, out var session))
                {
                    return;
                }

                subscribers = session.Subscribers.ToList();

                if (frame.StatusMessage != null && frame.StatusMessage.Contains("ended", StringComparison.OrdinalIgnoreCase))
                {
                    session.IsActive = false;
                }
            }

            foreach (var subscriber in subscribers)
            {
                subscriber.OnFrameReceived(clientInfo, frame);
            }
        }

        private static void StartSessionInternal(ClientInfo clientInfo, RemoteDesktopSession session, RemoteDesktopRequestMessage? request)
        {
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
            session.IsActive = false;

            var stopMessage = new RemoteDesktopRequestMessage
            {
                IsStart = false,
                IntervalMilliseconds = session.LastRequest?.IntervalMilliseconds ?? 500,
                JpegQuality = session.LastRequest?.JpegQuality ?? 70
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

            return new RemoteDesktopRequestMessage
            {
                IsStart = isStart,
                IntervalMilliseconds = interval,
                JpegQuality = quality
            };
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
