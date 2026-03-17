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
		public int ControllerPort = 9000;
		public bool UseSsl;
		public string LastTransportError = string.Empty;

		private ClientWebSocket Socket;
		private CancellationTokenSource ReceiveCancellation;
		private Task ReceiveTask;
		private readonly object SocketLock = new object();

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

			try
			{
				LastTransportError = string.Empty;
				Socket = new ClientWebSocket();
				ReceiveCancellation = new CancellationTokenSource();

				var uri = new Uri(GetSocketUri());
				Log("JellyfishLighting - connecting websocket to " + uri);
				Socket.ConnectAsync(uri, ReceiveCancellation.Token).GetAwaiter().GetResult();

				IsSocketConnected = Socket.State == WebSocketState.Open;
				IsConnected = IsSocketConnected;
				if (!IsSocketConnected)
				{
					LastTransportError = "WebSocket connect returned state: " + Socket.State;
					Log("JellyfishLighting - transport start failed: " + LastTransportError);
					return;
				}

				ReceiveTask = Task.Run(() => ReceiveLoop(ReceiveCancellation.Token));
				Log("JellyfishLighting - websocket connected");
			}
			catch (Exception ex)
			{
				LastTransportError = ex.Message;
				IsSocketConnected = false;
				IsConnected = false;
				Log("JellyfishLighting - transport start exception: " + ex.Message);
				SafeCleanupSocket();
			}
		}

		public override void Stop()
		{
			Log("JellyfishLighting - transport stop requested.");
			IsSocketConnected = false;
			IsConnected = false;

			try
			{
				if (ReceiveCancellation != null && !ReceiveCancellation.IsCancellationRequested)
				{
					ReceiveCancellation.Cancel();
				}
			}
			catch
			{
			}

			lock (SocketLock)
			{
				if (Socket != null)
				{
					try
					{
						if (Socket.State == WebSocketState.Open || Socket.State == WebSocketState.CloseReceived)
						{
							Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "disconnect", CancellationToken.None).GetAwaiter().GetResult();
						}
					}
					catch
					{
					}
					finally
					{
						Socket.Dispose();
						Socket = null;
					}
				}
			}

			if (ReceiveTask != null)
			{
				try
				{
					ReceiveTask.Wait(250);
				}
				catch
				{
				}
			}

			if (ReceiveCancellation != null)
			{
				ReceiveCancellation.Dispose();
				ReceiveCancellation = null;
			}

			ReceiveTask = null;
		}

		public override void SendMethod(string message, object[] parameters)
		{
			if (string.IsNullOrEmpty(message))
			{
				return;
			}

			if (!IsSocketConnected || Socket == null || Socket.State != WebSocketState.Open)
			{
				Log("JellyfishLighting - SendMethod ignored (socket not connected)");
				return;
			}

			try
			{
				Log("JellyfishLighting TX: " + message);
				var bytes = Encoding.UTF8.GetBytes(message);
				lock (SocketLock)
				{
					Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).GetAwaiter().GetResult();
				}
			}
			catch (Exception ex)
			{
				LastTransportError = ex.Message;
				Log("JellyfishLighting - send exception: " + ex.Message);
				Stop();
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

		private async Task ReceiveLoop(CancellationToken cancellationToken)
		{
			var buffer = new byte[4096];
			while (!cancellationToken.IsCancellationRequested)
			{
				try
				{
					if (Socket == null || Socket.State != WebSocketState.Open)
					{
						break;
					}

					var builder = new StringBuilder();
					WebSocketReceiveResult result;
					do
					{
						result = await Socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
						if (result.MessageType == WebSocketMessageType.Close)
						{
							LastTransportError = "WebSocket closed by remote endpoint.";
							Log("JellyfishLighting - " + LastTransportError);
							IsSocketConnected = false;
							IsConnected = false;
							return;
						}

						builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
					}
					while (!result.EndOfMessage);

					ReceiveJsonFromSocket(builder.ToString());
				}
				catch (OperationCanceledException)
				{
					return;
				}
				catch (Exception ex)
				{
					LastTransportError = ex.Message;
					Log("JellyfishLighting - receive exception: " + ex.Message);
					IsSocketConnected = false;
					IsConnected = false;
					return;
				}
			}
		}

		private void SafeCleanupSocket()
		{
			lock (SocketLock)
			{
				if (Socket != null)
				{
					try
					{
						Socket.Dispose();
					}
					catch
					{
					}
					Socket = null;
				}
			}
		}
	}
}
