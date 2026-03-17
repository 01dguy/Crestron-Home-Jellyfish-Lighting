using System;
using System.Text;
using System.Text.RegularExpressions;
using Crestron.RAD.Common.BasicDriver;

namespace JellyfishLighting.ExtensionDriver
{
    public class Jellyfish_Lighting_Protocol : ABaseDriverProtocol, IDisposable
    {
        private readonly Jellyfish_Lighting Device;
        private readonly Jellyfish_Lighting_Transport TransportLayer;
        private readonly object _stateLock = new object();
        private bool HasStarted;

        public Jellyfish_Lighting.UiUpdateDelegate UI_Update;

        public string LastStatus = "Disconnected";
        public string LastAckStatus = "";
        public string LastPowerStatus = "";
        public bool LastOnlineState;
        public string LastScene = "Unknown";
        public int LastBrightness;
        public int LastSpeed;
        public string LastZoneSummary = "Unknown";

        private string ControllerHost = string.Empty;
        private int ControllerPort = 9000;

        private string LastPatternFile = string.Empty;
        private string[] LastZoneNames = new string[0];
        private string[] KnownZones = new string[0];
        private string CachedPatternData = string.Empty;

        // Pattern list cache for UI scene pickers
        private string CachedPatternFileListJson = string.Empty;
        private string[] CachedPatternPaths = new string[0];

        // Optional reconnect restore guard
        private long LastOutboundCommandTicks;
        private long ReconnectStartedTicks;

        public Jellyfish_Lighting_Protocol(Jellyfish_Lighting_Transport transport, byte id) : base(transport, id)
        {
            Transport = transport;
            TransportLayer = transport;
            Device = transport.Device;

            TransportLayer.ConnectionLost += HandleTransportConnectionLost;
            TransportLayer.ConnectionEstablished += HandleTransportConnectionEstablished;
        }

        public void Start()
        {
            Log("JellyfishLighting - Protocol.Start called");

            lock (_stateLock)
            {
                if (HasStarted)
                {
                    Log("JellyfishLighting - Protocol.Start ignored (already started).");
                    return;
                }

                HasStarted = true;
            }

            UpdatePollingInterval(Device.Settings.PollIntervalSeconds);
            EnableAutoPolling = true;

            Log("JellyfishLighting - Protocol.Start state: " +
                "host=" + ControllerHost +
                " port=" + ControllerPort +
                " useSsl(settings)=" + Device.Settings.UseSsl +
                " socketConnected=" + TransportLayer.IsSocketConnected);

            if (string.IsNullOrEmpty(ControllerHost))
            {
                lock (_stateLock)
                {
                    LastStatus = "ControllerHost not set";
                    LastOnlineState = false;
                }

                Log("JellyfishLighting - Protocol.Start aborted: ControllerHost is blank.");
                UI_Update?.Invoke();
                return;
            }

            // Configure transport only. Transport lifecycle handled by framework/device.
            ApplyTransportConfiguration();

            if (TransportLayer.IsSocketConnected)
            {
                lock (_stateLock)
                {
                    LastStatus = "Connected (WebSocket scaffold)";
                    LastOnlineState = true;
                }

                UI_Update?.Invoke();
                PollNow();
            }
            else
            {
                lock (_stateLock)
                {
                    LastStatus = "Waiting for transport connection";
                    LastOnlineState = false;
                }

                UI_Update?.Invoke();
            }
        }

        public void Stop()
        {
            EnableAutoPolling = false;

            lock (_stateLock)
            {
                HasStarted = false;
            }

            TransportLayer.Stop();

            lock (_stateLock)
            {
                LastStatus = "Disconnected";
                LastOnlineState = false;
            }

            UI_Update?.Invoke();
        }

        public void Dispose()
        {
            TransportLayer.ConnectionLost -= HandleTransportConnectionLost;
            TransportLayer.ConnectionEstablished -= HandleTransportConnectionEstablished;
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
                lock (_stateLock) { LastStatus = "Invalid power state. Use 0 or 1."; }
                UI_Update?.Invoke();
                return;
            }

            string patternFile;
            string[] zoneNames;

            lock (_stateLock)
            {
                patternFile = LastPatternFile;
                zoneNames = LastZoneNames;
            }

            if (string.IsNullOrEmpty(patternFile) || zoneNames == null || zoneNames.Length == 0)
            {
                lock (_stateLock) { LastStatus = "Power command requires a previously selected pattern and zones."; }
                UI_Update?.Invoke();
                return;
            }

            TransportLayer.SendJson(BuildRunPatternBasicCommand(patternFile, zoneNames, state));
            MarkOutboundCommandSent();

            lock (_stateLock)
            {
                LastAckStatus = state == 1 ? "Power on command sent" : "Power off command sent";
                UpdateLastStatus();
            }

            UI_Update?.Invoke();
        }

        protected override void Poll()
        {
            if (!TransportLayer.IsSocketConnected)
            {
                lock (_stateLock)
                {
                    LastStatus = "WebSocket not connected";
                    LastOnlineState = false;
                }
                UI_Update?.Invoke();
                return;
            }

            TransportLayer.SendJson(BuildGetPatternListCommand());
            TransportLayer.SendJson(BuildGetZoneListCommand());
            MarkOutboundCommandSent();

            lock (_stateLock)
            {
                LastAckStatus = "Polling: requested patternFileList + zones";
                UpdateLastStatus();
            }

            UI_Update?.Invoke();
        }

        public void RequestPatternFileData(string folder, string patternName)
        {
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(patternName))
            {
                lock (_stateLock) { LastStatus = "Pattern file data requires folder and pattern name."; }
                UI_Update?.Invoke();
                return;
            }

            lock (_stateLock)
            {
                LastPatternFile = folder + "/" + patternName;
            }

            TransportLayer.SendJson(BuildGetPatternFileDataCommand(folder, patternName));
            MarkOutboundCommandSent();

            lock (_stateLock)
            {
                LastAckStatus = "Requested pattern file data.";
                UpdateLastStatus();
            }

            UI_Update?.Invoke();
        }

        public void RunPattern(string filePath, string[] zoneNames, int state)
        {
            string[] knownZones;
            lock (_stateLock)
            {
                knownZones = KnownZones;
            }

            if (!IsValidBasicRunPatternRequest(filePath, zoneNames, state, knownZones))
            {
                lock (_stateLock) { LastStatus = "RunPattern basic requires file, state (0/1), and at least one known zone."; }
                UI_Update?.Invoke();
                return;
            }

            TransportLayer.SendJson(BuildRunPatternBasicCommand(filePath, zoneNames, state));
            MarkOutboundCommandSent();

            lock (_stateLock)
            {
                LastPatternFile = filePath;
                LastZoneNames = zoneNames;
                LastAckStatus = "RunPattern basic command sent";
                UpdateLastStatus();
            }

            UI_Update?.Invoke();
        }

        public void ApplyAdvancedPatternData(string dataJson, string[] zoneNames, int state)
        {
            string validationError;
            if (!ValidateAdvancedPatternData(dataJson, zoneNames, state, out validationError))
            {
                lock (_stateLock) { LastStatus = "Advanced pattern validation failed: " + validationError; }
                UI_Update?.Invoke();
                return;
            }

            TransportLayer.SendJson(BuildRunPatternAdvancedCommand(dataJson, zoneNames, state));
            MarkOutboundCommandSent();

            lock (_stateLock)
            {
                CachedPatternData = dataJson;
                LastZoneNames = zoneNames;
                LastAckStatus = "RunPattern advanced command sent";
                UpdateLastStatus();
            }

            UI_Update?.Invoke();
        }

        public void SetBrightness(int brightness, string[] zoneNames)
        {
            if (brightness < 0 || brightness > 100)
            {
                lock (_stateLock) { LastStatus = "Brightness must be between 0 and 100."; }
                UI_Update?.Invoke();
                return;
            }

            string cached;
            lock (_stateLock)
            {
                cached = CachedPatternData;
            }

            if (string.IsNullOrEmpty(cached))
            {
                lock (_stateLock) { LastStatus = "No cached pattern data. Request patternFileData first."; }
                UI_Update?.Invoke();
                return;
            }

            var updated = ReplaceIntField(cached, "brightness", brightness);
            ApplyAdvancedPatternData(updated, zoneNames, 1);
        }

        public void SetSpeed(int speed, string[] zoneNames)
        {
            if (speed < 1)
            {
                lock (_stateLock) { LastStatus = "Speed must be >= 1."; }
                UI_Update?.Invoke();
                return;
            }

            string cached;
            lock (_stateLock)
            {
                cached = CachedPatternData;
            }

            if (string.IsNullOrEmpty(cached))
            {
                lock (_stateLock) { LastStatus = "No cached pattern data. Request patternFileData first."; }
                UI_Update?.Invoke();
                return;
            }

            var updated = ReplaceIntField(cached, "speed", speed);
            ApplyAdvancedPatternData(updated, zoneNames, 1);
        }

        public void HandleInboundWebSocketJson(string json)
        {
            if (string.IsNullOrEmpty(json) ||
                json.IndexOf("\"cmd\":\"fromCtlr\"", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            Log("JellyfishLighting - inbound controller JSON: " + json);

            string newScene = null;
            string newPatternFile = null;
            string newCachedPatternData = null;
            int? newBrightness = null;
            int? newSpeed = null;
            string newAckStatus = null;
            string newPowerStatus = null;
            string newZoneSummary = null;
            string[] newKnownZones = null;

            if (json.IndexOf("\"runPattern\"", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var runPatternFile = ExtractString(json, "file");
                if (!string.IsNullOrEmpty(runPatternFile))
                {
                    newScene = runPatternFile;
                    newPatternFile = runPatternFile;
                }

                var runPatternData = ExtractString(json, "data");
                if (!string.IsNullOrEmpty(runPatternData))
                {
                    newCachedPatternData = runPatternData;
                    newBrightness = ExtractInt(runPatternData, "brightness");
                    newSpeed = ExtractInt(runPatternData, "speed");

                    if (newBrightness == null || newSpeed == null)
                    {
                        Log("JellyfishLighting - runPattern.data present but brightness/speed parse incomplete.");
                    }
                }

                var runPatternZoneId = ExtractString(json, "id");
                var runPatternZoneFromArray = ExtractFirstArrayValue(json, "zoneName");
                var runPatternZone = !string.IsNullOrEmpty(runPatternZoneId) ? runPatternZoneId : runPatternZoneFromArray;

                newAckStatus = !string.IsNullOrEmpty(runPatternZone)
                    ? "RunPattern ack: " + runPatternZone
                    : "RunPattern update received";
            }

            if (newBrightness == null)
            {
                newBrightness = ExtractInt(json, "brightness");
            }

            var ledPower = ExtractBool(json, "ledPower");
            if (ledPower != null)
            {
                newPowerStatus = (bool)ledPower ? "LED power is ON" : "LED power is OFF";
            }

            if (json.IndexOf("\"patternFileList\"", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                int folderCount;
                int patternCount;
                ExtractPatternCounts(json, out folderCount, out patternCount);
                newAckStatus = string.Format("Pattern list: {0} folders, {1} patterns", folderCount, patternCount);

                lock (_stateLock)
                {
                    CachedPatternFileListJson = json;
                    CachedPatternPaths = ExtractPatternPaths(json);
                }
            }

            if (json.IndexOf("\"zones\"", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                newKnownZones = ExtractZoneNames(json);

                Log("JellyfishLighting - parsed zones: " +
                    (newKnownZones == null || newKnownZones.Length == 0
                        ? "none"
                        : string.Join(", ", newKnownZones)));

                newZoneSummary = (newKnownZones == null || newKnownZones.Length == 0)
                    ? "Zones: none"
                    : "Zones: " + string.Join(", ", newKnownZones);

                if (newAckStatus == null)
                {
                    newAckStatus = "Zone list received";
                }
            }

            if (json.IndexOf("\"patternFileData\"", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var patternData = ExtractString(json, "jsonData");
                if (!string.IsNullOrEmpty(patternData))
                {
                    newCachedPatternData = patternData;
                    if (newBrightness == null) newBrightness = ExtractInt(patternData, "brightness");
                    if (newSpeed == null) newSpeed = ExtractInt(patternData, "speed");
                }

                newAckStatus = "Pattern file data received";
            }

            Log("JellyfishLighting - parsed inbound values: " +
                "scene=" + (newScene ?? "null") +
                ", brightness=" + (newBrightness != null ? newBrightness.Value.ToString() : "null") +
                ", speed=" + (newSpeed != null ? newSpeed.Value.ToString() : "null") +
                ", ack=" + (newAckStatus ?? "null"));

            Log("JellyfishLighting - applying zone summary: " + (newZoneSummary ?? "null"));

            bool sceneChanged;
            lock (_stateLock)
            {
                var previousScene = LastScene;

                if (newScene != null) LastScene = newScene;
                if (newPatternFile != null) LastPatternFile = newPatternFile;
                if (newCachedPatternData != null) CachedPatternData = newCachedPatternData;
                if (newBrightness != null) LastBrightness = (int)newBrightness;
                if (newSpeed != null) LastSpeed = (int)newSpeed;
                if (newAckStatus != null) LastAckStatus = newAckStatus;
                if (newPowerStatus != null) LastPowerStatus = newPowerStatus;
                if (newKnownZones != null) KnownZones = newKnownZones;
                if (newZoneSummary != null) LastZoneSummary = newZoneSummary;

                LastOnlineState = true;
                UpdateLastStatus();

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

            UI_Update?.Invoke();
        }

        private void HandleTransportConnectionLost(string reason)
        {
            lock (_stateLock)
            {
                LastOnlineState = false;
                LastStatus = "Disconnected";

                if (!string.IsNullOrEmpty(reason))
                {
                    LastStatus += ": " + reason;
                }
            }

            UI_Update?.Invoke();
        }

        private void HandleTransportConnectionEstablished(bool isReconnect)
        {
            var nowTicks = DateTime.UtcNow.Ticks;

            lock (_stateLock)
            {
                LastOnlineState = true;
                LastStatus = isReconnect ? "Reconnected" : "Connected (WebSocket scaffold)";

                if (isReconnect)
                {
                    LastAckStatus = "Reconnected - resyncing patternFileList + zones";
                    ReconnectStartedTicks = nowTicks;
                }
                else
                {
                    ReconnectStartedTicks = 0;
                }

                UpdateLastStatus();
            }

            PollNow();
            RestoreCachedStateAfterReconnect();
            UI_Update?.Invoke();
        }

        private void RestoreCachedStateAfterReconnect()
        {
            if (!TransportLayer.IsSocketConnected)
            {
                return;
            }

            string cachedData;
            string patternFile;
            string[] zoneNames;
            string powerStatus;
            long reconnectTicks;
            long lastOutboundTicks;

            lock (_stateLock)
            {
                cachedData = CachedPatternData;
                patternFile = LastPatternFile;
                zoneNames = LastZoneNames;
                powerStatus = LastPowerStatus;
                reconnectTicks = ReconnectStartedTicks;
                lastOutboundTicks = LastOutboundCommandTicks;
            }

            // Skip stale replay if a newer command was already sent.
            if (reconnectTicks > 0 && lastOutboundTicks > reconnectTicks)
            {
                lock (_stateLock)
                {
                    LastAckStatus = "Reconnected - skipped cached restore (new command already sent)";
                    UpdateLastStatus();
                }
                return;
            }

            var restoreState = string.Equals(powerStatus, "LED power is OFF", StringComparison.OrdinalIgnoreCase) ? 0 : 1;

            if (!string.IsNullOrEmpty(cachedData) && zoneNames != null && zoneNames.Length > 0)
            {
                TransportLayer.SendJson(BuildRunPatternAdvancedCommand(cachedData, zoneNames, restoreState));
                MarkOutboundCommandSent();

                lock (_stateLock)
                {
                    LastAckStatus = "Reconnected - restored cached pattern state";
                    UpdateLastStatus();
                }
                return;
            }

            if (!string.IsNullOrEmpty(patternFile) && zoneNames != null && zoneNames.Length > 0)
            {
                TransportLayer.SendJson(BuildRunPatternBasicCommand(patternFile, zoneNames, restoreState));
                MarkOutboundCommandSent();

                lock (_stateLock)
                {
                    LastAckStatus = "Reconnected - restored last runPattern state";
                    UpdateLastStatus();
                }
            }
        }

        private void MarkOutboundCommandSent()
        {
            lock (_stateLock)
            {
                LastOutboundCommandTicks = DateTime.UtcNow.Ticks;
            }
        }

        // ======================
        // UI Scene List Helpers
        // ======================

        public string GetParentScenesAsUiListJson()
        {
            string[] paths;
            lock (_stateLock)
            {
                paths = CachedPatternPaths ?? new string[0];
            }

            var uniqueParents = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < paths.Length; i++)
            {
                var path = paths[i];
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                var idx = path.IndexOf('/');
                var parent = idx > 0 ? path.Substring(0, idx).Trim() : path.Trim();

                if (!string.IsNullOrEmpty(parent))
                {
                    uniqueParents.Add(parent);
                }
            }

            var list = new System.Collections.Generic.List<string>(uniqueParents);
            list.Sort(StringComparer.OrdinalIgnoreCase);

            return BuildUiListJsonFromNames(list);
        }

        public string GetChildScenesAsUiListJson(string parentId)
        {
            if (string.IsNullOrEmpty(parentId))
            {
                return "[]";
            }

            string[] paths;
            lock (_stateLock)
            {
                paths = CachedPatternPaths ?? new string[0];
            }

            var parentPrefix = parentId + "/";
            var uniqueChildren = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < paths.Length; i++)
            {
                var path = paths[i];
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                if (!path.StartsWith(parentPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var child = path.Substring(parentPrefix.Length).Trim();
                if (!string.IsNullOrEmpty(child))
                {
                    uniqueChildren.Add(child);
                }
            }

            var list = new System.Collections.Generic.List<string>(uniqueChildren);
            list.Sort(StringComparer.OrdinalIgnoreCase);

            return BuildUiListJsonFromNames(list);
        }

        // ======================
        // Status Helpers
        // ======================

        private void UpdateLastStatus()
        {
            if (!string.IsNullOrEmpty(LastPowerStatus))
            {
                LastStatus = LastPowerStatus;

                if (!string.IsNullOrEmpty(LastAckStatus))
                {
                    LastStatus += " | " + LastAckStatus;
                }

                return;
            }

            if (!string.IsNullOrEmpty(LastAckStatus))
            {
                LastStatus = LastAckStatus;
            }
        }

        // ======================
        // Outbound Builders
        // ======================

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

        // ======================
        // Validation / Parsing
        // ======================

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
                    if (string.Equals(zoneNames[i], knownZones[j], StringComparison.OrdinalIgnoreCase))
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

            if (!ContainsKey(dataJson, "colors") ||
                !ContainsKey(dataJson, "spaceBetweenPixels") ||
                !ContainsKey(dataJson, "effectBetweenPixels") ||
                !ContainsKey(dataJson, "type") ||
                !ContainsKey(dataJson, "skip") ||
                !ContainsKey(dataJson, "numOfLeds") ||
                !ContainsKey(dataJson, "runData") ||
                !ContainsKey(dataJson, "direction"))
            {
                error = "one or more required advanced data fields are missing";
                return false;
            }

            if (!ContainsKey(dataJson, "speed") ||
                !ContainsKey(dataJson, "brightness") ||
                !ContainsKey(dataJson, "effect") ||
                !ContainsKey(dataJson, "effectValue") ||
                !ContainsKey(dataJson, "rgbAdj"))
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

        private static string[] ExtractZoneNames(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return new string[0];
            }

            var zonesMatch = Regex.Match(json, "\\\"zones\\\"\\s*:\\s*\\{(?<body>.*)\\}\\s*\\}?", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!zonesMatch.Success)
            {
                return new string[0];
            }

            var body = zonesMatch.Groups["body"].Value;
            var keyMatches = Regex.Matches(body, "\\\"(?<zone>[^\\\"]+)\\\"\\s*:\\s*\\{", RegexOptions.IgnoreCase);
            if (keyMatches.Count == 0)
            {
                return new string[0];
            }

            var zones = new string[keyMatches.Count];
            for (var i = 0; i < keyMatches.Count; i++)
            {
                zones[i] = keyMatches[i].Groups["zone"].Value;
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

            var folderHeaderMatches = Regex.Matches(
                json,
                "\\\"folders\\\"\\s*:\\s*\\\"[^\\\"]*\\\"\\s*,\\s*\\\"name\\\"\\s*:\\s*\\\"\\\"",
                RegexOptions.IgnoreCase);

            folderCount = folderHeaderMatches.Count;

            var allNameMatches = Regex.Matches(json, "\\\"name\\\"\\s*:\\s*\\\"(?<name>[^\\\"]*)\\\"", RegexOptions.IgnoreCase);
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
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string ExtractString(string json, string key)
        {
            var match = Regex.Match(
                json,
                "\\\"" + key + "\\\"\\s*:\\s*\\\"(?<v>(?:[^\\\"\\\\]|\\\\.)*)\\\"",
                RegexOptions.IgnoreCase);

            return match.Success ? Unescape(match.Groups["v"].Value) : null;
        }

        private static string Unescape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            var sb = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c == '\\' && i + 1 < value.Length)
                {
                    var n = value[i + 1];
                    switch (n)
                    {
                        case '\\': sb.Append('\\'); i++; continue;
                        case '"': sb.Append('\"'); i++; continue;
                        case 'n': sb.Append('\n'); i++; continue;
                        case 'r': sb.Append('\r'); i++; continue;
                        case 't': sb.Append('\t'); i++; continue;
                        default:
                            sb.Append(n);
                            i++;
                            continue;
                    }
                }

                sb.Append(c);
            }

            return sb.ToString();
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
            var match = Regex.Match(json, "\\\"" + key + "\\\"\\s*:\\s*(?<v>true|false)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return null;
            }

            return string.Equals(match.Groups["v"].Value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractFirstArrayValue(string json, string key)
        {
            var match = Regex.Match(json, "\\\"" + key + "\\\"\\s*:\\s*\\[(?<v>[^\\]]*)\\]", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return null;
            }

            var arr = match.Groups["v"].Value;
            var first = Regex.Match(arr, "\\\"(?<item>[^\\\"]*)\\\"");
            return first.Success ? first.Groups["item"].Value : null;
        }

        private static string[] ExtractPatternPaths(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return new string[0];
            }

            var folderMatches = Regex.Matches(
                json,
                "\\\"(?<folder>[^\\\"]+)\\\"\\s*:\\s*\\[(?<items>.*?)\\]",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            var result = new System.Collections.Generic.List<string>();

            for (var i = 0; i < folderMatches.Count; i++)
            {
                var folder = folderMatches[i].Groups["folder"].Value;
                var items = folderMatches[i].Groups["items"].Value;

                if (string.IsNullOrEmpty(folder) ||
                    string.Equals(folder, "cmd", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(folder, "fromCtlr", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(folder, "patternFileList", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(folder, "folders", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var nameMatches = Regex.Matches(
                    items,
                    "\\\"name\\\"\\s*:\\s*\\\"(?<name>[^\\\"]+)\\\"",
                    RegexOptions.IgnoreCase);

                for (var j = 0; j < nameMatches.Count; j++)
                {
                    var name = nameMatches[j].Groups["name"].Value;
                    if (!string.IsNullOrEmpty(name))
                    {
                        result.Add(folder + "/" + name);
                    }
                }
            }

            return result.ToArray();
        }

        private static string BuildUiListJsonFromNames(System.Collections.Generic.List<string> names)
        {
            if (names == null || names.Count == 0)
            {
                return "[]";
            }

            var sb = new StringBuilder();
            sb.Append("[");

            for (var i = 0; i < names.Count; i++)
            {
                var n = names[i] ?? string.Empty;
                var escaped = Escape(n);

                if (i > 0)
                {
                    sb.Append(",");
                }

                sb.Append("{\"id\":\"");
                sb.Append(escaped);
                sb.Append("\",\"name\":\"");
                sb.Append(escaped);
                sb.Append("\"}");
            }

            sb.Append("]");
            return sb.ToString();
        }

        private static int ToPort(string value)
        {
            int parsed;
            return int.TryParse(value, out parsed) && parsed > 0 ? parsed : 9000;
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
            Log("JellyfishLighting - SetUserAttribute: " + attributeId + " = " + attributeValue);

            switch (attributeId)
            {
                case "ControllerHost":
                    ControllerHost = attributeValue ?? string.Empty;
                    break;

                case "ControllerPort":
                    ControllerPort = ToPort(attributeValue);
                    break;
            }

            Log("JellyfishLighting - ApplyTransportConfiguration from SetUserAttribute: " +
                "host=" + ControllerHost +
                " port=" + ControllerPort);

            ApplyTransportConfiguration();
        }
    }
}