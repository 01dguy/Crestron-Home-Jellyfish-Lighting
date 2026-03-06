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
		public Jellyfish_Lighting Device;

		public bool IsSocketConnected;
		public string ControllerHost = string.Empty;
		public int ControllerPort = 80;
		public bool UseSsl;
		public string LastTransportError = string.Empty;

		private readonly object SocketSyncRoot = new object();
		private ClientWebSocket Socket;
		private CancellationTokenSource ReceiveLoopCancellation;
		private Task ReceiveLoopTask;
		private int ConnectionGeneration;

		public Jellyfish_Lighting_Transport(Jellyfish_Lighting device)
		{
			IsEthernetTransport = true;
			IsConnected = false;
			Device = device;
		}

		public void Configure(string host, int port, bool useSsl)
		{
			ControllerHost = host ?? string.Empty;
			ControllerPort = port <= 0 ? 80 : port;
			UseSsl = useSsl;
		}

		public string GetSocketUri()
		{
			var scheme = UseSsl ? "wss" : "ws";
			return string.Format("{0}://{1}:{2}", scheme, ControllerHost, ControllerPort);
		}

		public override void Start()
		{
			Stop();

			if (string.IsNullOrEmpty(ControllerHost))
			{
				LastTransportError = "ControllerHost is required.";
				IsSocketConnected = false;
				IsConnected = false;
				Log("JellyfishLighting - transport start failed: " + LastTransportError);
				return;
			}

			var socketUriText = GetSocketUri();
			Uri socketUri;
			if (!Uri.TryCreate(socketUriText, UriKind.Absolute, out socketUri) ||
				(socketUri.Scheme != "ws" && socketUri.Scheme != "wss"))
			{
				LastTransportError = "Invalid websocket uri: " + socketUriText;
				IsSocketConnected = false;
				IsConnected = false;
				Log("JellyfishLighting - transport start failed: " + LastTransportError);
				return;
			}

			var socket = new ClientWebSocket();
			var cancellation = new CancellationTokenSource();
			var generation = Interlocked.Increment(ref ConnectionGeneration);

			try
			{
				LastTransportError = string.Empty;
				Log("JellyfishLighting - transport start requested for " + socketUri);
				socket.ConnectAsync(socketUri, cancellation.Token).Wait();

				if (socket.State != WebSocketState.Open)
				{
					throw new InvalidOperationException("Socket state after connect was " + socket.State);
				}

				lock (SocketSyncRoot)
				{
					Socket = socket;
					ReceiveLoopCancellation = cancellation;
					IsSocketConnected = true;
					IsConnected = true;
				}

				ReceiveLoopTask = Task.Run(() => ReceiveLoopAsync(socket, cancellation.Token, generation));
			}
			catch (Exception ex)
			{
				LastTransportError = ex.Message;
				Log("JellyfishLighting - transport start failed: " + LastTransportError);
				try
				{
					socket.Dispose();
				}
				catch
				{
				}
				cancellation.Dispose();
				IsSocketConnected = false;
				IsConnected = false;
			}
		}

		public override void Stop()
		{
			Log("JellyfishLighting - transport stop requested.");

			ClientWebSocket socketToClose;
			CancellationTokenSource cancellationToDispose;
			Task receiveLoopToWait;

			lock (SocketSyncRoot)
			{
				Interlocked.Increment(ref ConnectionGeneration);
				socketToClose = Socket;
				cancellationToDispose = ReceiveLoopCancellation;
				receiveLoopToWait = ReceiveLoopTask;

				Socket = null;
				ReceiveLoopCancellation = null;
				ReceiveLoopTask = null;
				IsSocketConnected = false;
				IsConnected = false;
			}

			if (cancellationToDispose != null)
			{
				try
				{
					cancellationToDispose.Cancel();
				}
				catch
				{
				}
			}

			if (socketToClose != null)
			{
				try
				{
					if (socketToClose.State == WebSocketState.Open || socketToClose.State == WebSocketState.CloseReceived)
					{
						socketToClose.CloseAsync(WebSocketCloseStatus.NormalClosure, "Driver stop", CancellationToken.None).Wait();
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
				try
				{
					receiveLoopToWait.Wait(2000);
				}
				catch
				{
				}
			}

			if (cancellationToDispose != null)
			{
				cancellationToDispose.Dispose();
			}

			IsSocketConnected = false;
			IsConnected = false;
		}

		public override void SendMethod(string message, object[] parameters)
		{
			ClientWebSocket socket;
			lock (SocketSyncRoot)
			{
				socket = Socket;
			}

			if (!IsSocketConnected || socket == null || socket.State != WebSocketState.Open)
			{
				Log("JellyfishLighting - SendMethod ignored (socket not connected)");
				return;
			}

			try
			{
				var payload = Encoding.UTF8.GetBytes(message);
				socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, CancellationToken.None).Wait();
				Log("JellyfishLighting TX: " + message);
			}
			catch (Exception ex)
			{
				LastTransportError = ex.Message;
				Log("JellyfishLighting - SendMethod failed: " + LastTransportError);
				Stop();
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
					var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);

					if (result.MessageType == WebSocketMessageType.Close)
					{
						if (string.IsNullOrEmpty(LastTransportError))
						{
							LastTransportError = "Socket closed by remote endpoint.";
						}
						Log("JellyfishLighting - remote socket close received.");
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
			}
			catch (Exception ex)
			{
				LastTransportError = ex.Message;
				Log("JellyfishLighting - receive loop failed: " + LastTransportError);
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
