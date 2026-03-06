using System;
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

			LastTransportError = string.Empty;
			Log("JellyfishLighting - transport start requested for " + GetSocketUri());
			// TODO: Replace scaffold with an actual WebSocket connection implementation.
			// Once connected, inbound text frames should call ReceiveJsonFromSocket(...).
			IsSocketConnected = true;
			IsConnected = true;
		}

		public override void Stop()
		{
			Log("JellyfishLighting - transport stop requested.");
			IsSocketConnected = false;
			IsConnected = false;
		}

		public override void SendMethod(string message, object[] parameters)
		{
			if (!IsSocketConnected)
			{
				Log("JellyfishLighting - SendMethod ignored (socket not connected)");
				return;
			}

			Log("JellyfishLighting TX: " + message);
			// TODO: Send JSON message over websocket stream.
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
