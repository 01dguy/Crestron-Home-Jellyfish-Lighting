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

        private string CachedPatternFileListJson = string.Empty;
        private string[] CachedPatternPaths = new string[0];

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
            lock (_stateLock)
            {
                if (HasStarted)
                {
                    return;
                }
                HasStarted = true;
            }

            UpdatePollingInterval(Device.Settings.PollIntervalSeconds);
            EnableAutoPolling = true;

            if (string.IsNullOrEmpty(ControllerHost))
            {
                lock (_stateLock)
                {
                    LastStatus = "ControllerHost not set";
                    LastOnlineState = false;
                }
                UI_Update?.Invoke();
                return;
            }

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

        public void ApplySettings(bool useSsl, int pollIntervalSeconds)
        {
            UpdatePollingInterval(pollIntervalSeconds);
            Device.Settings.UseSsl = useSsl;
            ApplyTransportConfiguration();
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
                if ((zoneNames == null || zoneNames.Length == 0) && KnownZones.Length > 0)
                {
                    zoneNames = KnownZones;
                    LastZoneNames = KnownZones;
                }
            }

            if (string.IsNullOrEmpty(patternFile) || zoneNames == null || zoneNames.Length == 0)
            {
                lock (_stateLock) { LastStatus = "Power command requires a selected pattern and one or more zones."; }
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

        public void HandleInboundWebSocketJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            var cmd = ExtractString(json, "cmd");
            if (!string.Equals(cmd, "fromCtlr", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

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
                newPowerStatus = (bool)ledPower ? "Jellyfish is ON" : "Jellyfish is OFF";
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

                Device.RebuildSceneListsAndCommit();
            }

            if (json.IndexOf("\"zones\"", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                newKnownZones = ExtractZoneNames(json);
                newZoneSummary = (newKnownZones == null || newKnownZones.Length == 0)
                    ? "Zones: none"
                    : "Zones: " + string.Join(", ", newKnownZones);

                if (newAckStatus == null)
                {
                    newAckStatus = "Zone list received";
                }
            }

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

            if (reconnectTicks > 0 && lastOutboundTicks > reconnectTicks)
            {
                lock (_stateLock)
                {
                    LastAckStatus = "Reconnected - skipped cached restore (new command already sent)";
                    UpdateLastStatus();
                }
                return;
            }

            var restoreState = string.Equals(powerStatus, "Jellyfish is OFF", StringComparison.OrdinalIgnoreCase) ? 0 : 1;

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

        public string[] GetParentSceneNames()
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
            return list.ToArray();
        }

        public string[] GetChildSceneNames(string parentId)
        {
            if (string.IsNullOrEmpty(parentId))
            {
                return new string[0];
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
                if (string.IsNullOrEmpty(path) || !path.StartsWith(parentPrefix, StringComparison.OrdinalIgnoreCase))
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
            return list.ToArray();
        }

        //Populate a known zone if none selected, to allow power commands to succeed without explicit zone selection
        public string[] GetKnownZonesSnapshot()
        {
            lock (_stateLock)
            {
                if (KnownZones == null || KnownZones.Length == 0)
                {
                    return new string[0];
                }

                var copy = new string[KnownZones.Length];
                Array.Copy(KnownZones, copy, KnownZones.Length);
                return copy;
            }
        }

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

        public static string BuildGetPatternListCommand()
        {
            return "{\"cmd\":\"toCtlrGet\",\"get\":[[\"patternFileList\"]]}";
        }

        public static string BuildGetZoneListCommand()
        {
            return "{\"cmd\":\"toCtlrGet\",\"get\":[[\"zones\"]]}";
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

        private static void ExtractPatternCounts(string json, out int folderCount, out int patternCount)
        {
            folderCount = 0;
            patternCount = 0;

            var paths = ExtractPatternPaths(json);
            if (paths.Length == 0)
            {
                return;
            }

            var folders = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < paths.Length; i++)
            {
                var path = paths[i];
                var idx = path.IndexOf('/');
                if (idx > 0)
                {
                    folders.Add(path.Substring(0, idx));
                }
                patternCount++;
            }

            folderCount = folders.Count;
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

        private static string[] ExtractPatternPaths(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return new string[0];
            }

            var listMatch = Regex.Match(
                json,
                @"""patternFileList""\s*:\s*\[(?<body>.*)\]",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!listMatch.Success)
            {
                return new string[0];
            }

            var body = listMatch.Groups["body"].Value;
            var entryMatches = Regex.Matches(body, "\\{[^{}]*\\}", RegexOptions.Singleline);
            if (entryMatches.Count == 0)
            {
                return new string[0];
            }

            var result = new System.Collections.Generic.List<string>();
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < entryMatches.Count; i++)
            {
                var entry = entryMatches[i].Value;
                var folder = ExtractString(entry, "folders");
                var name = ExtractString(entry, "name");

                // folder headers have empty name, skip those
                if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var path = folder.Trim() + "/" + name.Trim();
                if (seen.Add(path))
                {
                    result.Add(path);
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

                sb.Append("{\"id\":\"").Append(escaped)
                  .Append("\",\"name\":\"").Append(escaped)
                  .Append("\",\"label\":\"").Append(escaped)
                  .Append("\",\"value\":\"").Append(escaped)
                  .Append("\"}");
            }
            sb.Append("]");
            return sb.ToString();
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
            var match = Regex.Match(json, "\\\"" + key + "\\\"\\s*:\\s*\\\"(?<v>(?:[^\\\"\\\\]|\\\\.)*)\\\"", RegexOptions.IgnoreCase);
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
                        case '"': sb.Append('"'); i++; continue;
                        case 'n': sb.Append('\n'); i++; continue;
                        case 'r': sb.Append('\r'); i++; continue;
                        case 't': sb.Append('\t'); i++; continue;
                        default: sb.Append(n); i++; continue;
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