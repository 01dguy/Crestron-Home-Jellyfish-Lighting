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
		public Jellyfish_Lighting Device;

		public bool IsSocketConnected;
		public string ControllerHost = string.Empty;
		public int ControllerPort = 80;
		public bool UseSsl;
		public string LastTransportError = string.Empty;

		private readonly object _socketStateLock = new object();
		private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
		private ClientWebSocket _socketClient;
		private int _connectionVersion;

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
			if (string.IsNullOrEmpty(ControllerHost))
			{
				LastTransportError = "ControllerHost is required.";
				IsSocketConnected = false;
				IsConnected = false;
				Log("JellyfishLighting - transport start failed: " + LastTransportError);
				return;
			}

			Uri socketUri;
			if (!Uri.TryCreate(GetSocketUri(), UriKind.Absolute, out socketUri))
			{
				LastTransportError = "Invalid socket URI.";
				IsSocketConnected = false;
				IsConnected = false;
				Log("JellyfishLighting - transport start failed: " + LastTransportError);
				return;
			}

			LastTransportError = string.Empty;
			Log("JellyfishLighting - transport start requested for " + socketUri);

			int connectVersion;
			lock (_socketStateLock)
			{
				connectVersion = ++_connectionVersion;
			}

			Task.Run(async () =>
			{
				ClientWebSocket socket = null;
				try
				{
					socket = new ClientWebSocket();
					await socket.ConnectAsync(socketUri, CancellationToken.None).ConfigureAwait(false);

					lock (_socketStateLock)
					{
						if (connectVersion != _connectionVersion)
						{
							socket.Dispose();
							socket = null;
							return;
						}

						ReplaceSocketUnsafe(socket);
						socket = null;
						IsSocketConnected = true;
						IsConnected = true;
						LastTransportError = string.Empty;
					}

					Log("JellyfishLighting - transport connected: " + socketUri);
				}
				catch (Exception ex)
				{
					LastTransportError = ex.Message;
					lock (_socketStateLock)
					{
						IsSocketConnected = false;
						IsConnected = false;
					}

					if (socket != null)
					{
						socket.Dispose();
					}

					Log("JellyfishLighting - transport start failed: " + LastTransportError);
				}
			});
		}

		public override void Stop()
		{
			Log("JellyfishLighting - transport stop requested.");

			ClientWebSocket socketToClose = null;
			lock (_socketStateLock)
			{
				_connectionVersion++;
				socketToClose = _socketClient;
				_socketClient = null;
				IsSocketConnected = false;
				IsConnected = false;
			}

			if (socketToClose == null)
			{
				return;
			}

			Task.Run(async () =>
			{
				try
				{
					if (socketToClose.State == WebSocketState.Open || socketToClose.State == WebSocketState.CloseReceived)
					{
						await socketToClose.CloseAsync(WebSocketCloseStatus.NormalClosure, "Transport stop", CancellationToken.None).ConfigureAwait(false);
					}
				}
				catch (Exception ex)
				{
					LastTransportError = ex.Message;
					Log("JellyfishLighting - transport stop close failed: " + LastTransportError);
				}
				finally
				{
					socketToClose.Dispose();
				}
			});
		}

		public override void SendMethod(string message, object[] parameters)
		{
			if (!IsSocketConnected)
			{
				Log("JellyfishLighting - SendMethod ignored (socket not connected)");
				return;
			}

			ClientWebSocket socket;
			lock (_socketStateLock)
			{
				socket = _socketClient;
			}

			if (socket == null || socket.State != WebSocketState.Open)
			{
				Log("JellyfishLighting - SendMethod ignored (socket not open)");
				return;
			}

			Log("JellyfishLighting TX: " + message);

			Task.Run(async () =>
			{
				try
				{
					var payload = Encoding.UTF8.GetBytes(message);
					await _sendLock.WaitAsync().ConfigureAwait(false);
					try
					{
						await socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
					}
					finally
					{
						_sendLock.Release();
					}
				}
				catch (Exception ex)
				{
					LastTransportError = ex.Message;
					Log("JellyfishLighting - SendMethod failed: " + LastTransportError);
				}
			});
		}

		private void ReplaceSocketUnsafe(ClientWebSocket newSocket)
		{
			if (_socketClient != null)
			{
				try
				{
					_socketClient.Dispose();
				}
				catch
				{
				}
			}

			_socketClient = newSocket;
		}

		public void SendJson(string json)
		{
			if (string.IsNullOrEmpty(json))
			{
				return;
			}

			SendMethod(json, null);
		}
	}
}
