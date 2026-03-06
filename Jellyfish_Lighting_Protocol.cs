using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
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
		public int LastSpeed;
		public string LastZoneSummary = "Unknown";

		private string ControllerHost = string.Empty;
		private int ControllerPort = 80;

		private string LastPatternFile = string.Empty;
		private string[] LastZoneNames = new string[0];
		private string[] KnownZones = new string[0];
		private string CachedPatternData = string.Empty;
		private readonly object _stateLock = new object();
		private static readonly JavaScriptSerializer JsonSerializer = new JavaScriptSerializer();

		private sealed class FromControllerPayload
		{
			public RunPatternPayload RunPattern;
			public PatternFileListPayload PatternFileList;
			public ZonesPayload Zones;
			public PatternFileDataPayload PatternFileData;
			public bool? LedPower;
		}

		private sealed class RunPatternPayload
		{
			public string File;
			public string Data;
			public string Id;
			public string[] ZoneNames = new string[0];
			public int? Brightness;
			public int? Speed;
		}

		private sealed class PatternFileListPayload
		{
			public int FolderCount;
			public int PatternCount;
		}

		private sealed class ZonesPayload
		{
			public string[] ZoneNames = new string[0];
		}

		private sealed class PatternFileDataPayload
		{
			public string JsonData;
			public int? Brightness;
			public int? Speed;
		}


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
				InvokeUiUpdate();
			}
		}

		public void Stop()
		{
			EnableAutoPolling = false;
			TransportLayer.Stop();
			LastStatus = "Disconnected";
			LastOnlineState = false;
			InvokeUiUpdate();
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
				InvokeUiUpdate();
				return;
			}

			if (string.IsNullOrEmpty(LastPatternFile) || LastZoneNames == null || LastZoneNames.Length == 0)
			{
				LastStatus = "Power command requires a previously selected pattern and zones.";
				InvokeUiUpdate();
				return;
			}

			TransportLayer.SendJson(BuildRunPatternBasicCommand(LastPatternFile, LastZoneNames, state));
			LastStatus = state == 1 ? "Power on command sent" : "Power off command sent";
			InvokeUiUpdate();
		}

		protected override void Poll()
		{
			if (!TransportLayer.IsSocketConnected)
			{
				LastStatus = "WebSocket not connected";
				LastOnlineState = false;
				InvokeUiUpdate();
				return;
			}

			TransportLayer.SendJson(BuildGetPatternListCommand());
			TransportLayer.SendJson(BuildGetZoneListCommand());
			LastStatus = "Polling: requested patternFileList + zones";
			InvokeUiUpdate();
		}

		public void RequestPatternFileData(string folder, string patternName)
		{
			if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(patternName))
			{
				LastStatus = "Pattern file data requires folder and pattern name.";
				InvokeUiUpdate();
				return;
			}

			LastPatternFile = folder + "/" + patternName;
			TransportLayer.SendJson(BuildGetPatternFileDataCommand(folder, patternName));
			LastStatus = "Requested pattern file data.";
			InvokeUiUpdate();
		}

		public void RunPattern(string filePath, string[] zoneNames, int state)
		{
			if (!IsValidBasicRunPatternRequest(filePath, zoneNames, state, KnownZones))
			{
				LastStatus = "RunPattern basic requires file, state (0/1), and at least one known zone.";
				InvokeUiUpdate();
				return;
			}

			LastPatternFile = filePath;
			LastZoneNames = zoneNames;
			TransportLayer.SendJson(BuildRunPatternBasicCommand(filePath, zoneNames, state));
			LastStatus = "RunPattern basic command sent";
			InvokeUiUpdate();
		}

		public void ApplyAdvancedPatternData(string dataJson, string[] zoneNames, int state)
		{
			string validationError;
			if (!ValidateAdvancedPatternData(dataJson, zoneNames, state, out validationError))
			{
				LastStatus = "Advanced pattern validation failed: " + validationError;
				InvokeUiUpdate();
				return;
			}

			CachedPatternData = dataJson;
			LastZoneNames = zoneNames;
			TransportLayer.SendJson(BuildRunPatternAdvancedCommand(dataJson, zoneNames, state));
			LastStatus = "RunPattern advanced command sent";
			InvokeUiUpdate();
		}

		public void SetBrightness(int brightness, string[] zoneNames)
		{
			if (brightness < 0 || brightness > 100)
			{
				LastStatus = "Brightness must be between 0 and 100.";
				InvokeUiUpdate();
				return;
			}

			if (string.IsNullOrEmpty(CachedPatternData))
			{
				LastStatus = "No cached pattern data. Request patternFileData first.";
				InvokeUiUpdate();
				return;
			}

			var updated = ReplaceIntField(CachedPatternData, "brightness", brightness);
			ApplyAdvancedPatternData(updated, zoneNames, 1);
		}

		public void SetSpeed(int speed, string[] zoneNames)
		{
			if (speed < 1)
			{
				LastStatus = "Speed must be >= 1.";
				InvokeUiUpdate();
				return;
			}

			if (string.IsNullOrEmpty(CachedPatternData))
			{
				LastStatus = "No cached pattern data. Request patternFileData first.";
				InvokeUiUpdate();
				return;
			}

			var updated = ReplaceIntField(CachedPatternData, "speed", speed);
			ApplyAdvancedPatternData(updated, zoneNames, 1);
		}

		public void HandleInboundWebSocketJson(string json)
		{
			if (string.IsNullOrEmpty(json))
			{
				Log("JellyfishLighting - ignoring empty inbound frame.");
				return;
			}

			if (json.TrimStart().IndexOf("{", StringComparison.Ordinal) != 0)
			{
				Log("JellyfishLighting - ignoring malformed inbound frame: " + json);
				return;
			}

			if (json.IndexOf("\"cmd\":\"fromCtlr\"", StringComparison.OrdinalIgnoreCase) < 0)
			{
				Log("JellyfishLighting - ignoring non-fromCtlr frame: " + json);
				return;
			}

			FromControllerPayload payload;
			if (!TryParseFromControllerPayload(json, out payload))
			{
				Log("JellyfishLighting - unable to parse fromCtlr payload: " + json);
				return;
			}

			var sceneChanged = false;
			lock (_stateLock)
			{
				var previousScene = LastScene;

				if (payload.RunPattern != null)
				{
					var runPatternFile = payload.RunPattern.File;
					if (!string.IsNullOrEmpty(runPatternFile))
					{
						LastScene = runPatternFile;
						LastPatternFile = runPatternFile;
					}

					var runPatternData = payload.RunPattern.Data;
					if (!string.IsNullOrEmpty(runPatternData))
					{
						CachedPatternData = runPatternData;
						if (payload.RunPattern.Brightness != null)
						{
							LastBrightness = (int)payload.RunPattern.Brightness;
						}
						if (payload.RunPattern.Speed != null)
						{
							LastSpeed = (int)payload.RunPattern.Speed;
						}
					}

					if (payload.RunPattern.Brightness != null)
					{
						LastBrightness = (int)payload.RunPattern.Brightness;
					}

					var runPatternZoneId = payload.RunPattern.Id;
					var runPatternZoneFromArray = payload.RunPattern.ZoneNames.Length > 0 ? payload.RunPattern.ZoneNames[0] : null;
					var runPatternZone = !string.IsNullOrEmpty(runPatternZoneId) ? runPatternZoneId : runPatternZoneFromArray;
					LastStatus = !string.IsNullOrEmpty(runPatternZone)
						? "RunPattern ack: " + runPatternZone
						: "RunPattern update received";
				}

				if (payload.LedPower != null)
				{
					LastStatus = (bool)payload.LedPower ? "LED power is ON" : "LED power is OFF";
				}

				if (payload.PatternFileList != null)
				{
					LastStatus = "Pattern list received";
					LastZoneSummary = string.Format("Folders: {0} Patterns: {1}", payload.PatternFileList.FolderCount, payload.PatternFileList.PatternCount);
				}

				if (payload.Zones != null)
				{
					KnownZones = payload.Zones.ZoneNames;
					LastZoneSummary = string.Format("Zones: {0}", KnownZones.Length);
				}

				if (payload.PatternFileData != null)
				{
					if (!string.IsNullOrEmpty(payload.PatternFileData.JsonData))
					{
						CachedPatternData = payload.PatternFileData.JsonData;
						if (payload.PatternFileData.Brightness != null)
						{
							LastBrightness = (int)payload.PatternFileData.Brightness;
						}
						if (payload.PatternFileData.Speed != null)
						{
							LastSpeed = (int)payload.PatternFileData.Speed;
						}
					}
					LastStatus = "Pattern file data received";
				}

				LastOnlineState = true;
				if (string.IsNullOrEmpty(LastStatus) || LastStatus == "Disconnected")
				{
					LastStatus = "fromCtlr update received";
				}

				sceneChanged = !string.Equals(previousScene, LastScene, StringComparison.OrdinalIgnoreCase);
			}

			if (sceneChanged)
			{
				Device.TriggerSceneUpdatedEvent();
			}

			InvokeUiUpdate();
		}

		public void HandleTransportTextFrame(string frame)
		{
			HandleInboundWebSocketJson(frame);
		}

		public void HandleTransportConnectionChanged(bool isConnected, string reason)
		{
			lock (_stateLock)
			{
				LastOnlineState = isConnected;
				if (isConnected)
				{
					LastStatus = "Connected";
				}
				else
				{
					LastStatus = string.IsNullOrEmpty(reason) ? "Disconnected" : "Disconnected: " + reason;
				}
			}

			InvokeUiUpdate();
		}

		private void InvokeUiUpdate()
		{
			var update = UI_Update;
			if (update != null)
			{
				update();
			}
		}

		public static string BuildGetPatternListCommand()
		{
			return Serialize(new Dictionary<string, object>
			{
				{ "cmd", "toCtlrGet" },
				{ "get", new object[] { new object[] { "patternFileList" } } }
			});
		}

		public static string BuildGetZoneListCommand()
		{
			return Serialize(new Dictionary<string, object>
			{
				{ "cmd", "toCtlrGet" },
				{ "get", new object[] { new object[] { "zones" } } }
			});
		}

		public static string BuildGetPatternFileDataCommand(string folder, string fileName)
		{
			return Serialize(new Dictionary<string, object>
			{
				{ "cmd", "toCtlrGet" },
				{ "get", new object[] { new object[] { "patternFileData", folder ?? string.Empty, fileName ?? string.Empty } } }
			});
		}

		public static string BuildRunPatternBasicCommand(string filePath, string[] zoneNames, int state)
		{
			return Serialize(new Dictionary<string, object>
			{
				{ "cmd", "toCtlrSet" },
				{ "runPattern", BuildRunPatternPayload(filePath, string.Empty, state, zoneNames) }
			});
		}

		public static string BuildRunPatternAdvancedCommand(string dataJson, string[] zoneNames, int state)
		{
			return Serialize(new Dictionary<string, object>
			{
				{ "cmd", "toCtlrSet" },
				{ "runPattern", BuildRunPatternPayload(string.Empty, dataJson ?? string.Empty, state, zoneNames) }
			});
		}

		private static Dictionary<string, object> BuildRunPatternPayload(string filePath, string data, int state, string[] zoneNames)
		{
			return new Dictionary<string, object>
			{
				{ "file", filePath ?? string.Empty },
				{ "data", data ?? string.Empty },
				{ "id", string.Empty },
				{ "state", state },
				{ "zoneName", zoneNames ?? new string[0] }
			};
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

		private static string ReplaceIntField(string json, string fieldName, int value)
		{
			return Regex.Replace(
				json,
				"\\\"" + fieldName + "\\\"\\s*:\\s*-?\\d+",
				"\"" + fieldName + "\":" + value,
				RegexOptions.IgnoreCase);
		}

		private static bool TryParseFromControllerPayload(string json, out FromControllerPayload payload)
		{
			payload = null;
			object rawRoot;
			if (!TryDeserializeJson(json, out rawRoot))
			{
				return false;
			}

			var root = rawRoot as Dictionary<string, object>;
			if (root == null)
			{
				return false;
			}

			payload = new FromControllerPayload
			{
				RunPattern = ParseRunPattern(root),
				PatternFileList = ParsePatternFileList(root),
				Zones = ParseZones(root),
				PatternFileData = ParsePatternFileData(root),
				LedPower = GetBool(root, "ledPower")
			};

			return true;
		}

		private static RunPatternPayload ParseRunPattern(Dictionary<string, object> root)
		{
			var runPatternObject = GetDictionary(root, "runPattern");
			if (runPatternObject == null)
			{
				return null;
			}

			var payload = new RunPatternPayload
			{
				File = GetString(runPatternObject, "file"),
				Data = GetString(runPatternObject, "data"),
				Id = GetString(runPatternObject, "id"),
				ZoneNames = GetStringArray(runPatternObject, "zoneName"),
				Brightness = GetInt(runPatternObject, "brightness")
			};

			Dictionary<string, object> nestedData;
			if (TryParseNestedJson(payload.Data, out nestedData))
			{
				payload.Brightness = payload.Brightness ?? GetIntWithRunDataFallback(nestedData, "brightness");
				payload.Speed = GetIntWithRunDataFallback(nestedData, "speed");
			}

			return payload;
		}

		private static PatternFileListPayload ParsePatternFileList(Dictionary<string, object> root)
		{
			var listObject = GetArray(root, "patternFileList");
			if (listObject == null)
			{
				return null;
			}

			var payload = new PatternFileListPayload();
			for (var i = 0; i < listObject.Length; i++)
			{
				var item = listObject[i] as Dictionary<string, object>;
				if (item == null)
				{
					continue;
				}

				var name = GetString(item, "name") ?? string.Empty;
				if (string.IsNullOrEmpty(name))
				{
					payload.FolderCount++;
				}
				else
				{
					payload.PatternCount++;
				}
			}

			return payload;
		}

		private static ZonesPayload ParseZones(Dictionary<string, object> root)
		{
			var zonesObject = GetDictionary(root, "zones");
			if (zonesObject == null)
			{
				return null;
			}

			var zones = new List<string>();
			foreach (var key in zonesObject.Keys)
			{
				zones.Add(key);
			}

			return new ZonesPayload { ZoneNames = zones.ToArray() };
		}

		private static PatternFileDataPayload ParsePatternFileData(Dictionary<string, object> root)
		{
			var patternFileData = GetDictionary(root, "patternFileData");
			if (patternFileData == null)
			{
				return null;
			}

			var payload = new PatternFileDataPayload
			{
				JsonData = GetString(patternFileData, "jsonData")
			};

			Dictionary<string, object> nested;
			if (TryParseNestedJson(payload.JsonData, out nested))
			{
				payload.Brightness = GetIntWithRunDataFallback(nested, "brightness");
				payload.Speed = GetIntWithRunDataFallback(nested, "speed");
			}

			return payload;
		}

		private static bool TryParseNestedJson(string json, out Dictionary<string, object> nested)
		{
			nested = null;
			if (string.IsNullOrEmpty(json))
			{
				return false;
			}

			object raw;
			if (!TryDeserializeJson(json, out raw))
			{
				return false;
			}

			nested = raw as Dictionary<string, object>;
			return nested != null;
		}

		private static bool TryDeserializeJson(string json, out object result)
		{
			result = null;
			try
			{
				result = JsonSerializer.DeserializeObject(json);
				return true;
			}
			catch
			{
				return false;
			}
		}

		private static bool ContainsKey(string json, string key)
		{
			return json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static string ExtractString(string json, string key)
		{
			Dictionary<string, object> parsed;
			if (!TryParseNestedJson(json, out parsed))
			{
				return null;
			}
			return GetString(parsed, key);
		}

		private static int? ExtractInt(string json, string key)
		{
			Dictionary<string, object> parsed;
			if (!TryParseNestedJson(json, out parsed))
			{
				return null;
			}
			return GetIntWithRunDataFallback(parsed, key);
		}

		private static Dictionary<string, object> GetDictionary(Dictionary<string, object> source, string key)
		{
			if (source == null || !source.ContainsKey(key) || source[key] == null)
			{
				return null;
			}
			return source[key] as Dictionary<string, object>;
		}

		private static object[] GetArray(Dictionary<string, object> source, string key)
		{
			if (source == null || !source.ContainsKey(key) || source[key] == null)
			{
				return null;
			}
			return source[key] as object[];
		}

		private static string GetString(Dictionary<string, object> source, string key)
		{
			if (source == null || !source.ContainsKey(key) || source[key] == null)
			{
				return null;
			}
			return source[key] as string;
		}

		private static int? GetInt(Dictionary<string, object> source, string key)
		{
			if (source == null || !source.ContainsKey(key) || source[key] == null)
			{
				return null;
			}

			if (source[key] is int)
			{
				return (int)source[key];
			}
			if (source[key] is long)
			{
				return (int)(long)source[key];
			}
			if (source[key] is double)
			{
				return (int)(double)source[key];
			}

			int value;
			return int.TryParse(source[key].ToString(), out value) ? value : (int?)null;
		}


		private static int? GetIntWithRunDataFallback(Dictionary<string, object> source, string key)
		{
			var value = GetInt(source, key);
			if (value != null)
			{
				return value;
			}

			var runData = GetDictionary(source, "runData");
			return runData != null ? GetInt(runData, key) : null;
		}
		private static bool? GetBool(Dictionary<string, object> source, string key)
		{
			if (source == null || !source.ContainsKey(key) || source[key] == null)
			{
				return null;
			}
			if (source[key] is bool)
			{
				return (bool)source[key];
			}
			bool value;
			return bool.TryParse(source[key].ToString(), out value) ? value : (bool?)null;
		}

		private static string[] GetStringArray(Dictionary<string, object> source, string key)
		{
			var items = GetArray(source, key);
			if (items == null)
			{
				return new string[0];
			}

			var values = new List<string>();
			for (var i = 0; i < items.Length; i++)
			{
				if (items[i] is string)
				{
					values.Add((string)items[i]);
				}
			}

			return values.ToArray();
		}

		private static string Serialize(object payload)
		{
			return JsonSerializer.Serialize(payload);
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
