using System;
using System.Text.RegularExpressions;
using Crestron.RAD.Common.BasicDriver;

namespace JellyfishLighting.ExtensionDriver
{
	public class Jellyfish_Lighting_Protocol : ABaseDriverProtocol, IDisposable
	{
		private readonly Jellyfish_Lighting Device;
		private readonly Jellyfish_Lighting_Transport TransportLayer;

		public Jellyfish_Lighting.UiUpdateDelegate UI_Update;

		public string LastStatus = "Disconnected";
		public bool LastOnlineState;
		public string LastScene = "Unknown";
		public int LastBrightness;
		public string LastZoneSummary = "Unknown";

		private string ControllerHost = string.Empty;
		private int ControllerPort = 80;

		private string LastPatternFile = string.Empty;
		private string[] LastZoneNames = new string[0];
		private string[] KnownZones = new string[0];

		public Jellyfish_Lighting_Protocol(Jellyfish_Lighting_Transport transport, byte id) : base(transport, id)
		{
			Transport = transport;
			TransportLayer = transport;
			Device = transport.Device;
		}

		public void Start()
		{
			UpdatePollingInterval(Device.Settings.PollIntervalSeconds);
			EnableAutoPolling = true;
			ApplyTransportConfiguration();
			TransportLayer.Start();

			if (TransportLayer.IsSocketConnected)
			{
				LastStatus = "Connected (WebSocket scaffold)";
				LastOnlineState = true;
				PollNow();
			}
			else
			{
				LastStatus = "Disconnected: " + TransportLayer.LastTransportError;
				LastOnlineState = false;
				UI_Update?.Invoke();
			}
		}

		public void Stop()
		{
			EnableAutoPolling = false;
			TransportLayer.Stop();
			LastStatus = "Disconnected";
			LastOnlineState = false;
			UI_Update?.Invoke();
		}

		public void PollNow()
		{
			Poll();
		}

		public void UpdatePollingInterval(int pollIntervalSeconds)
		{
			if (pollIntervalSeconds < 10)
			{
				pollIntervalSeconds = 10;
			}
			PollingInterval = pollIntervalSeconds * 1000;
		}

		public void SetPowerState(int state)
		{
			if (state != 0 && state != 1)
			{
				LastStatus = "Invalid power state. Use 0 or 1.";
				UI_Update?.Invoke();
				return;
			}

			if (string.IsNullOrEmpty(LastPatternFile) || LastZoneNames == null || LastZoneNames.Length == 0)
			{
				LastStatus = "Power command requires a previously selected pattern and zones.";
				UI_Update?.Invoke();
				return;
			}

			TransportLayer.SendJson(BuildRunPatternBasicCommand(LastPatternFile, LastZoneNames, state));
			LastStatus = state == 1 ? "Power on command sent" : "Power off command sent";
			UI_Update?.Invoke();
		}

		protected override void Poll()
		{
			if (!TransportLayer.IsSocketConnected)
			{
				LastStatus = "WebSocket not connected";
				LastOnlineState = false;
				UI_Update?.Invoke();
				return;
			}

			TransportLayer.SendJson(BuildGetPatternListCommand());
			TransportLayer.SendJson(BuildGetZoneListCommand());
			LastStatus = "Polling: requested patternFileList + zones";
			UI_Update?.Invoke();
		}

		public void RequestPatternFileData(string folder, string patternName)
		{
			if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(patternName))
			{
				LastStatus = "Pattern file data requires folder and pattern name.";
				UI_Update?.Invoke();
				return;
			}

			TransportLayer.SendJson(BuildGetPatternFileDataCommand(folder, patternName));
			LastStatus = "Requested pattern file data.";
			UI_Update?.Invoke();
		}

		public void RunPattern(string filePath, string[] zoneNames, int state)
		{
			if (!IsValidBasicRunPatternRequest(filePath, zoneNames, state, KnownZones))
			{
				LastStatus = "RunPattern basic requires file, state (0/1), and at least one zone.";
				UI_Update?.Invoke();
				return;
			}

			LastPatternFile = filePath;
			LastZoneNames = zoneNames;
			TransportLayer.SendJson(BuildRunPatternBasicCommand(filePath, zoneNames, state));
			LastStatus = "RunPattern basic command sent";
			UI_Update?.Invoke();
		}

		public void ApplyAdvancedPatternData(string dataJson, string[] zoneNames, int state)
		{
			string validationError;
			if (!ValidateAdvancedPatternData(dataJson, zoneNames, state, out validationError))
			{
				LastStatus = "Advanced pattern validation failed: " + validationError;
				UI_Update?.Invoke();
				return;
			}

			LastZoneNames = zoneNames;
			TransportLayer.SendJson(BuildRunPatternAdvancedCommand(dataJson, zoneNames, state));
			LastStatus = "RunPattern advanced command sent";
			UI_Update?.Invoke();
		}

		public void HandleInboundWebSocketJson(string json)
		{
			if (string.IsNullOrEmpty(json) || json.IndexOf("\"cmd\":\"fromCtlr\"", StringComparison.OrdinalIgnoreCase) < 0)
			{
				return;
			}

			var previousScene = LastScene;

			if (json.IndexOf("\"runPattern\"", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				var runPatternFile = ExtractString(json, "file");
				if (!string.IsNullOrEmpty(runPatternFile))
				{
					LastScene = runPatternFile;
					LastPatternFile = runPatternFile;
				}

				var runPatternZoneId = ExtractString(json, "id");
				var runPatternZoneFromArray = ExtractFirstArrayValue(json, "zoneName");
				var runPatternZone = !string.IsNullOrEmpty(runPatternZoneId) ? runPatternZoneId : runPatternZoneFromArray;
				LastStatus = !string.IsNullOrEmpty(runPatternZone)
					? "RunPattern ack: " + runPatternZone
					: "RunPattern update received";
			}

			var brightness = ExtractInt(json, "brightness");
			if (brightness != null)
			{
				LastBrightness = (int)brightness;
			}

			var ledPower = ExtractBool(json, "ledPower");
			if (ledPower != null)
			{
				LastStatus = (bool)ledPower ? "LED power is ON" : "LED power is OFF";
			}

			if (json.IndexOf("\"patternFileList\"", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				int folderCount;
				int patternCount;
				ExtractPatternCounts(json, out folderCount, out patternCount);
				LastStatus = "Pattern list received";
				LastZoneSummary = string.Format("Folders: {0} Patterns: {1}", folderCount, patternCount);
			}

			if (json.IndexOf("\"zones\"", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				KnownZones = ExtractZoneNames(json);
				LastZoneSummary = string.Format("Zones: {0}", KnownZones.Length);
			}

			if (json.IndexOf("\"patternFileData\"", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				LastStatus = "Pattern file data received";
			}

			LastOnlineState = true;
			if (string.IsNullOrEmpty(LastStatus) || LastStatus == "Disconnected")
			{
				LastStatus = "fromCtlr update received";
			}

			if (!string.Equals(previousScene, LastScene, StringComparison.OrdinalIgnoreCase))
			{
				Device.TriggerSceneUpdatedEvent();
			}

			UI_Update?.Invoke();
		}

		public static string BuildGetPatternListCommand()
		{
			return "{\"cmd\":\"toCtlrGet\",\"get\":[[\"patternFileList\"]]}";
		}

		public static string BuildGetZoneListCommand()
		{
			return "{\"cmd\":\"toCtlrGet\",\"get\":[[\"zones\"]]}";
		}

		public static string BuildGetPatternFileDataCommand(string folder, string fileName)
		{
			return string.Format("{{\"cmd\":\"toCtlrGet\",\"get\":[[\"patternFileData\",\"{0}\",\"{1}\"]]}}", Escape(folder), Escape(fileName));
		}

		public static string BuildRunPatternBasicCommand(string filePath, string[] zoneNames, int state)
		{
			var zones = BuildZoneArray(zoneNames);
			return string.Format("{{\"cmd\":\"toCtlrSet\",\"runPattern\":{{\"file\":\"{0}\",\"data\":\"\",\"id\":\"\",\"state\":{1},\"zoneName\":[{2}]}}}}", Escape(filePath), state, zones);
		}

		public static string BuildRunPatternAdvancedCommand(string dataJson, string[] zoneNames, int state)
		{
			var zones = BuildZoneArray(zoneNames);
			var escapedData = Escape(dataJson);
			return string.Format("{{\"cmd\":\"toCtlrSet\",\"runPattern\":{{\"file\":\"\",\"data\":\"{0}\",\"id\":\"\",\"state\":{1},\"zoneName\":[{2}]}}}}", escapedData, state, zones);
		}

		public static string BuildAdvancedPatternDataJson(
			int[] colors,
			int spaceBetweenPixels,
			string effectBetweenPixels,
			string type,
			int skip,
			int numOfLeds,
			int speed,
			int brightness,
			string direction)
		{
			var colorsPayload = colors == null ? string.Empty : string.Join(",", colors);
			return string.Format("{{\"colors\":[{0}],\"spaceBetweenPixels\":{1},\"effectBetweenPixels\":\"{2}\",\"type\":\"{3}\",\"skip\":{4},\"numOfLeds\":{5},\"runData\":{{\"speed\":{6},\"brightness\":{7},\"effect\":\"No Effect\",\"effectValue\":0,\"rgbAdj\":[100,100,100]}},\"direction\":\"{8}\"}}",
				colorsPayload,
				spaceBetweenPixels,
				Escape(effectBetweenPixels),
				Escape(type),
				skip,
				numOfLeds,
				speed,
				brightness,
				Escape(direction));
		}

		private static bool IsValidBasicRunPatternRequest(string filePath, string[] zoneNames, int state, string[] knownZones)
		{
			if (string.IsNullOrEmpty(filePath) || zoneNames == null || zoneNames.Length == 0 || (state != 0 && state != 1))
			{
				return false;
			}

			if (knownZones == null || knownZones.Length == 0)
			{
				return true;
			}

			for (var i = 0; i < zoneNames.Length; i++)
			{
				var matched = false;
				for (var j = 0; j < knownZones.Length; j++)
				{
					if (string.Equals(zoneNames[i], knownZones[j], StringComparison.Ordinal))
					{
						matched = true;
						break;
					}
				}
				if (!matched)
				{
					return false;
				}
			}

			return true;
		}

		private static bool ValidateAdvancedPatternData(string dataJson, string[] zoneNames, int state, out string error)
		{
			error = string.Empty;
			if (string.IsNullOrEmpty(dataJson))
			{
				error = "data JSON is required";
				return false;
			}

			if (zoneNames == null || zoneNames.Length == 0)
			{
				error = "at least one zone is required";
				return false;
			}

			if (state != 0 && state != 1)
			{
				error = "state must be 0 or 1";
				return false;
			}

			if (!ContainsKey(dataJson, "colors") || !ContainsKey(dataJson, "spaceBetweenPixels") || !ContainsKey(dataJson, "effectBetweenPixels") ||
				!ContainsKey(dataJson, "type") || !ContainsKey(dataJson, "skip") || !ContainsKey(dataJson, "numOfLeds") ||
				!ContainsKey(dataJson, "runData") || !ContainsKey(dataJson, "direction"))
			{
				error = "one or more required advanced data fields are missing";
				return false;
			}

			if (!ContainsKey(dataJson, "speed") || !ContainsKey(dataJson, "brightness") || !ContainsKey(dataJson, "effect") || !ContainsKey(dataJson, "effectValue") || !ContainsKey(dataJson, "rgbAdj"))
			{
				error = "runData fields are incomplete";
				return false;
			}

			var effectValue = ExtractString(dataJson, "effect");
			if (!string.Equals(effectValue, "No Effect", StringComparison.OrdinalIgnoreCase))
			{
				error = "runData.effect must be 'No Effect'";
				return false;
			}

			var effectValueInt = ExtractInt(dataJson, "effectValue");
			if (effectValueInt == null || effectValueInt != 0)
			{
				error = "runData.effectValue must be 0";
				return false;
			}

			if (dataJson.IndexOf("\"rgbAdj\":[100,100,100]", StringComparison.OrdinalIgnoreCase) < 0)
			{
				error = "runData.rgbAdj must be [100,100,100]";
				return false;
			}

			var brightness = ExtractInt(dataJson, "brightness");
			if (brightness == null || brightness < 0 || brightness > 100)
			{
				error = "runData.brightness must be between 0 and 100";
				return false;
			}

			var colorsMatch = Regex.Match(dataJson, "\\\"colors\\\"\\s*:\\s*\\[(?<values>[^\\]]*)\\]", RegexOptions.IgnoreCase);
			if (!colorsMatch.Success)
			{
				error = "colors array is missing";
				return false;
			}

			var rawColors = colorsMatch.Groups["values"].Value;
			if (string.IsNullOrEmpty(rawColors))
			{
				error = "colors cannot be empty";
				return false;
			}

			var values = rawColors.Split(',');
			if (values.Length % 3 != 0 || values.Length > 90)
			{
				error = "colors length must be a multiple of 3 and <= 90";
				return false;
			}

			for (var i = 0; i < values.Length; i++)
			{
				int colorValue;
				if (!int.TryParse(values[i].Trim(), out colorValue) || colorValue < 0 || colorValue > 255)
				{
					error = "each colors value must be between 0 and 255";
					return false;
				}
			}

			return true;
		}


		private static string[] ExtractZoneNames(string json)
		{
			if (string.IsNullOrEmpty(json))
			{
				return new string[0];
			}

			var matches = Regex.Matches(json, "\"(?<zone>[^\"]+)\"\\s*:\\s*\\{\\s*\"numPixels\"", RegexOptions.IgnoreCase);
			if (matches.Count == 0)
			{
				return new string[0];
			}

			var zones = new string[matches.Count];
			for (var i = 0; i < matches.Count; i++)
			{
				zones[i] = matches[i].Groups["zone"].Value;
			}
			return zones;
		}

		private static void ExtractPatternCounts(string json, out int folderCount, out int patternCount)
		{
			folderCount = 0;
			patternCount = 0;
			if (string.IsNullOrEmpty(json))
			{
				return;
			}

			var folderHeaderMatches = Regex.Matches(json, "\"folders\"\\s*:\\s*\"[^\"]*\"\\s*,\\s*\"name\"\\s*:\\s*\"\"", RegexOptions.IgnoreCase);
			folderCount = folderHeaderMatches.Count;

			var allNameMatches = Regex.Matches(json, "\"name\"\\s*:\\s*\"(?<name>[^\"]*)\"", RegexOptions.IgnoreCase);
			for (var i = 0; i < allNameMatches.Count; i++)
			{
				if (!string.IsNullOrEmpty(allNameMatches[i].Groups["name"].Value))
				{
					patternCount++;
				}
			}
		}

		private static bool ContainsKey(string json, string key)
		{
			return json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static string BuildZoneArray(string[] zoneNames)
		{
			if (zoneNames == null || zoneNames.Length == 0)
			{
				return string.Empty;
			}

			var parts = new string[zoneNames.Length];
			for (var i = 0; i < zoneNames.Length; i++)
			{
				parts[i] = "\"" + Escape(zoneNames[i]) + "\"";
			}

			return string.Join(",", parts);
		}

		private static string Escape(string value)
		{
			return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\\", "\\\\").Replace("\"", "\\\"");
		}

		private static string ExtractString(string json, string key)
		{
			var match = Regex.Match(json, "\\\"" + key + "\\\"\\s*:\\s*\\\"(?<v>[^\\\"]*)\\\"", RegexOptions.IgnoreCase);
			return match.Success ? match.Groups["v"].Value : null;
		}

		private static int? ExtractInt(string json, string key)
		{
			var match = Regex.Match(json, "\\\"" + key + "\\\"\\s*:\\s*(?<v>-?\\d+)", RegexOptions.IgnoreCase);
			if (!match.Success)
			{
				return null;
			}

			int value;
			return int.TryParse(match.Groups["v"].Value, out value) ? value : (int?)null;
		}

		private static bool? ExtractBool(string json, string key)
		{
			var match = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(?<v>true|false)", RegexOptions.IgnoreCase);
			if (!match.Success)
			{
				return null;
			}

			return string.Equals(match.Groups["v"].Value, "true", StringComparison.OrdinalIgnoreCase);
		}

		private static string ExtractFirstArrayValue(string json, string key)
		{
			var match = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\\[(?<v>[^\\]]*)\\]", RegexOptions.IgnoreCase);
			if (!match.Success)
			{
				return null;
			}

			var arr = match.Groups["v"].Value;
			var first = Regex.Match(arr, "\"(?<item>[^\"]*)\"");
			return first.Success ? first.Groups["item"].Value : null;
		}

		private static int ToPort(string value)
		{
			int parsed;
			return int.TryParse(value, out parsed) && parsed > 0 ? parsed : 80;
		}

		private void ApplyTransportConfiguration()
		{
			TransportLayer.Configure(ControllerHost, ControllerPort, Device.Settings.UseSsl);
		}

		protected override void ConnectionChangedEvent(bool connection)
		{
			Log("JellyfishLighting - Connection changed: " + connection);
		}

		protected override void ChooseDeconstructMethod(ValidatedRxData validatedData)
		{
		}

		public override void SetUserAttribute(string attributeId, string attributeValue)
		{
			switch (attributeId)
			{
				case "ControllerHost":
					ControllerHost = attributeValue ?? string.Empty;
					break;
				case "ControllerPort":
					ControllerPort = ToPort(attributeValue);
					break;
			}

			ApplyTransportConfiguration();
		}
	}
}
