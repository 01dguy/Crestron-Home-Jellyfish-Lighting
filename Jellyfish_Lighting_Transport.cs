using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Crestron.RAD.Common.Transports;

namespace JellyfishLighting.ExtensionDriver
{
    public class Jellyfish_Lighting_Transport : ATransportDriver
    {
        public Action<string> InboundJsonReceived;
        public Action<string> ConnectionLost;
        public Action<bool> ConnectionEstablished;
        public Jellyfish_Lighting Device;

        public volatile bool IsSocketConnected;
        public string ControllerHost = string.Empty;
        public int ControllerPort = 9000;

        // Kept for compatibility, but transport currently forces ws:// only.
        public bool UseSsl;

        public string LastTransportError = string.Empty;

        private readonly object SocketSyncRoot = new object();
        private ClientWebSocket Socket;
        private CancellationTokenSource ReceiveLoopCancellation;
        private CancellationTokenSource ReconnectLoopCancellation;
        private Task ReceiveLoopTask;
        private int ConnectionGeneration;
        private int ReconnectInProgress;
        private bool IsStopping;
        private int OfflineNotifiedGeneration = -1;

        // Endless reconnect with capped backoff:
        // 1s, 2s, 5s, 10s, 30s, 60s, then 60s forever.
        private static readonly int[] ReconnectBackoffSeconds = { 1, 2, 5, 10, 30, 60 };

        public Jellyfish_Lighting_Transport(Jellyfish_Lighting device)
        {
            IsEthernetTransport = true;
            IsConnected = false;
            Device = device;
        }

        public void Configure(string host, int port, bool useSsl)
        {
            ControllerHost = host ?? string.Empty;
            ControllerPort = port <= 0 ? 9000 : port;

            // Force ws:// only for current environment.
            UseSsl = false;

            Log("JellyfishLighting - Transport.Configure host=" + ControllerHost +
                " port=" + ControllerPort +
                " useSsl(requested)=" + useSsl +
                " useSsl(applied)=false");
        }

        public string GetSocketUri()
        {
            // Always use ws://. Never attempt wss://.
            return string.Format("ws://{0}:{1}", ControllerHost, ControllerPort);
        }

        public override void Start()
        {
            lock (SocketSyncRoot)
            {
                if (IsSocketConnected && Socket != null && Socket.State == WebSocketState.Open)
                {
                    Log("JellyfishLighting - Transport.Start ignored because socket is already open.");
                    return;
                }
            }

            Stop();
            IsStopping = false;

            lock (SocketSyncRoot)
            {
                ReconnectLoopCancellation = new CancellationTokenSource();
            }

            Log("JellyfishLighting - Transport.Start host=" + ControllerHost +
                " port=" + ControllerPort +
                " uri=" + GetSocketUri());

            ConnectSocket(false);
        }

        private bool ConnectSocket(bool isReconnectAttempt)
        {
            var generation = Interlocked.Increment(ref ConnectionGeneration);

            if (string.IsNullOrEmpty(ControllerHost))
            {
                LastTransportError = "ControllerHost is required.";
                SetOfflineState(LastTransportError, generation);
                Log("JellyfishLighting - transport " +
                    (isReconnectAttempt ? "reconnect" : "start") +
                    " failed: " + LastTransportError);
                return false;
            }

            var socketUriText = GetSocketUri();
            Log("JellyfishLighting - final socket uri: " + socketUriText);

            Uri socketUri;
            if (!Uri.TryCreate(socketUriText, UriKind.Absolute, out socketUri) ||
                socketUri.Scheme != "ws")
            {
                LastTransportError = "Invalid websocket uri: " + socketUriText;
                SetOfflineState(LastTransportError, generation);
                Log("JellyfishLighting - transport " +
                    (isReconnectAttempt ? "reconnect" : "start") +
                    " failed: " + LastTransportError);
                return false;
            }

            var socket = new ClientWebSocket();
            var cancellation = new CancellationTokenSource();

            try
            {
                LastTransportError = string.Empty;

                Log("JellyfishLighting - transport " +
                    (isReconnectAttempt ? "reconnect" : "start") +
                    " requested for " + socketUri);

                socket.ConnectAsync(socketUri, cancellation.Token).Wait();

                if (socket.State != WebSocketState.Open)
                {
                    throw new InvalidOperationException("Socket state after connect was " + socket.State);
                }

                lock (SocketSyncRoot)
                {
                    var oldSocket = Socket;
                    var oldReceiveCancellation = ReceiveLoopCancellation;

                    Socket = socket;
                    ReceiveLoopCancellation = cancellation;
                    IsSocketConnected = true;
                    IsConnected = true;

                    if (oldReceiveCancellation != null)
                    {
                        try { oldReceiveCancellation.Cancel(); } catch { }
                        oldReceiveCancellation.Dispose();
                    }

                    if (oldSocket != null && !ReferenceEquals(oldSocket, socket))
                    {
                        try { oldSocket.Dispose(); } catch { }
                    }
                }

                // Allow one ConnectionLost event again for this new connection generation.
                Interlocked.Exchange(ref OfflineNotifiedGeneration, -1);

                Log("JellyfishLighting - websocket connected successfully.");

                ReceiveLoopTask = Task.Run(() => ReceiveLoopAsync(socket, cancellation.Token, generation));
                ConnectionEstablished?.Invoke(isReconnectAttempt);
                return true;
            }
            catch (Exception ex)
            {
                LastTransportError = ex.Message;
                Log("JellyfishLighting - transport " +
                    (isReconnectAttempt ? "reconnect" : "start") +
                    " failed: " + LastTransportError);

                try { socket.Dispose(); } catch { }
                cancellation.Dispose();

                SetOfflineState(LastTransportError, generation);
                return false;
            }
        }

        public override void Stop()
        {
            IsStopping = true;
            Log("JellyfishLighting - transport stop requested.");

            ClientWebSocket socketToClose;
            CancellationTokenSource cancellationToDispose;
            CancellationTokenSource reconnectCancellationToDispose;
            Task receiveLoopToWait;

            lock (SocketSyncRoot)
            {
                Interlocked.Increment(ref ConnectionGeneration);

                socketToClose = Socket;
                cancellationToDispose = ReceiveLoopCancellation;
                reconnectCancellationToDispose = ReconnectLoopCancellation;
                receiveLoopToWait = ReceiveLoopTask;

                Socket = null;
                ReceiveLoopCancellation = null;
                ReconnectLoopCancellation = null;
                ReceiveLoopTask = null;
                IsSocketConnected = false;
                IsConnected = false;
            }

            if (reconnectCancellationToDispose != null)
            {
                try { reconnectCancellationToDispose.Cancel(); } catch { }
            }

            if (cancellationToDispose != null)
            {
                try { cancellationToDispose.Cancel(); } catch { }
            }

            if (socketToClose != null)
            {
                try
                {
                    if (socketToClose.State == WebSocketState.Open || socketToClose.State == WebSocketState.CloseReceived)
                    {
                        socketToClose.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Driver stop",
                            CancellationToken.None).Wait();
                    }
                }
                catch (Exception ex)
                {
                    LastTransportError = ex.Message;
                    Log("JellyfishLighting - socket close failed: " + LastTransportError);
                }
                finally
                {
                    socketToClose.Dispose();
                }
            }

            if (receiveLoopToWait != null)
            {
                try { receiveLoopToWait.Wait(2000); } catch { }
            }

            if (cancellationToDispose != null) cancellationToDispose.Dispose();
            if (reconnectCancellationToDispose != null) reconnectCancellationToDispose.Dispose();

            IsSocketConnected = false;
            IsConnected = false;
            Interlocked.Exchange(ref ReconnectInProgress, 0);
        }

        public override void SendMethod(string message, object[] parameters)
        {
            ClientWebSocket socket;
            int generation;

            lock (SocketSyncRoot)
            {
                socket = Socket;
                generation = ConnectionGeneration;
            }

            if (!IsSocketConnected || socket == null || socket.State != WebSocketState.Open)
            {
                Log("JellyfishLighting - SendMethod ignored (socket not connected)");
                return;
            }

            try
            {
                var payload = Encoding.UTF8.GetBytes(message);
                socket.SendAsync(
                    new ArraySegment<byte>(payload),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None).Wait();

                Log("JellyfishLighting TX: " + message);
            }
            catch (Exception ex)
            {
                LastTransportError = ex.Message;
                Log("JellyfishLighting - SendMethod failed: " + LastTransportError);
                HandleConnectionLoss("Send failed: " + LastTransportError, generation);
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken, int generation)
        {
            var buffer = new byte[4096];
            var textBuilder = new StringBuilder();

            try
            {
                while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    var result = await socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        cancellationToken).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        HandleConnectionLoss("Socket closed by remote endpoint.", generation);
                        break;
                    }

                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        continue;
                    }

                    textBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    if (!result.EndOfMessage)
                    {
                        continue;
                    }

                    var inboundJson = textBuilder.ToString();
                    textBuilder.Length = 0;
                    ReceiveJsonFromSocket(inboundJson);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping/canceling loops.
            }
            catch (Exception ex)
            {
                HandleConnectionLoss("Receive loop failed: " + ex.Message, generation);
            }
            finally
            {
                if (generation == ConnectionGeneration)
                {
                    IsSocketConnected = false;
                    IsConnected = false;
                }
            }
        }

        private void HandleConnectionLoss(string reason, int generation)
        {
            SetOfflineState(reason, generation);

            if (IsStopping)
            {
                return;
            }

            StartReconnectLoop();
        }

        private void SetOfflineState(string reason, int generation)
        {
            if (!string.IsNullOrEmpty(reason))
            {
                LastTransportError = reason;
            }

            if (generation == ConnectionGeneration)
            {
                IsSocketConnected = false;
                IsConnected = false;

                var previousNotified = Interlocked.Exchange(ref OfflineNotifiedGeneration, generation);
                if (previousNotified != generation)
                {
                    ConnectionLost?.Invoke(LastTransportError);
                }
            }
        }

        private void StartReconnectLoop()
        {
            if (Interlocked.CompareExchange(ref ReconnectInProgress, 1, 0) != 0)
            {
                return;
            }

            CancellationToken token;
            lock (SocketSyncRoot)
            {
                if (ReconnectLoopCancellation == null)
                {
                    ReconnectLoopCancellation = new CancellationTokenSource();
                }

                token = ReconnectLoopCancellation.Token;
            }

            Task.Run(async () =>
            {
                try
                {
                    var attempt = 0;

                    while (!IsStopping && !token.IsCancellationRequested)
                    {
                        var index = attempt < ReconnectBackoffSeconds.Length
                            ? attempt
                            : ReconnectBackoffSeconds.Length - 1;

                        var delaySeconds = ReconnectBackoffSeconds[index];

                        Log("JellyfishLighting - reconnect attempt " + (attempt + 1) +
                            " in " + delaySeconds + "s");

                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token).ConfigureAwait(false);

                        if (IsStopping || token.IsCancellationRequested)
                        {
                            return;
                        }

                        if (ConnectSocket(true))
                        {
                            Log("JellyfishLighting - reconnect successful.");
                            return;
                        }

                        Log("JellyfishLighting - reconnect attempt failed. Last error: " + LastTransportError);
                        attempt++;
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected on stop.
                }
                finally
                {
                    Interlocked.Exchange(ref ReconnectInProgress, 0);
                }
            }, token);
        }

        public void SendJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            SendMethod(json, null);
        }

        public void ReceiveJsonFromSocket(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            Log("JellyfishLighting RX: " + json);
            InboundJsonReceived?.Invoke(json);
        }
    }
}