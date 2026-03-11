using System;
using Crestron.RAD.Common.Attributes.Programming;
using Crestron.RAD.Common.Enums;
using Crestron.RAD.Common.Interfaces;
using Crestron.RAD.Common.Interfaces.ExtensionDevice;
using Crestron.RAD.DeviceTypes.ExtensionDevice;

namespace JellyfishLighting.ExtensionDriver
{
    public class Jellyfish_Lighting : AExtensionDevice, ICloudConnected
    {
        public Jellyfish_Lighting_Transport Transport;
        public Jellyfish_Lighting_Protocol Protocol;
        public Settings_Data Settings;

        private const string Filename = "JellyfishLightingSettings";

        private const string StatusTextKey = "StatusText";
        private const string IsOnlineKey = "IsOnline";
        private const string ActiveSceneKey = "ActiveScene";
        private const string BrightnessKey = "Brightness";
        private const string ZoneSummaryKey = "ZoneSummary";
        private const string SpeedKey = "Speed";
        private const string PatternPathKey = "PatternPath";
        private const string SelectedZonesKey = "SelectedZones";
        private const string RequestedBrightnessKey = "RequestedBrightness";
        private const string RequestedSpeedKey = "RequestedSpeed";
        private const string UseSslKey = "UseSsl";
        private const string PollIntervalSecondsKey = "PollIntervalSeconds";

        private PropertyValue<string> StatusTextProperty;
        private PropertyValue<bool> IsOnlineProperty;
        private PropertyValue<string> ActiveSceneProperty;
        private PropertyValue<int> BrightnessProperty;
        private PropertyValue<string> ZoneSummaryProperty;
        private PropertyValue<int> SpeedProperty;
        private PropertyValue<string> PatternPathProperty;
        private PropertyValue<string> SelectedZonesProperty;
        private PropertyValue<int> RequestedBrightnessProperty;
        private PropertyValue<int> RequestedSpeedProperty;
        private PropertyValue<bool> UseSslProperty;
        private PropertyValue<int> PollIntervalSecondsProperty;

        [ProgrammableEvent]
        public event EventHandler SceneUpdated;

        public delegate void UiUpdateDelegate();

        public Jellyfish_Lighting()
        {
            Settings = new Settings_Data();
        }

        public void Initialize()
        {
            EnableLogging = true;
            CreateDeviceDefinition();

            var uiUpdate = new UiUpdateDelegate(Update_UI);

            Transport = new Jellyfish_Lighting_Transport(this)
            {
                EnableLogging = InternalEnableLogging,
                CustomLogger = InternalCustomLogger,
                EnableRxDebug = InternalEnableRxDebug,
                EnableTxDebug = InternalEnableTxDebug
            };

            ConnectionTransport = Transport;

            Protocol = new Jellyfish_Lighting_Protocol(Transport, Id)
            {
                EnableLogging = InternalEnableLogging,
                CustomLogger = InternalCustomLogger,
                UI_Update = uiUpdate
            };

            Transport.InboundJsonReceived += Protocol.HandleInboundWebSocketJson;

            DeviceProtocol = Protocol;
            DeviceProtocol.Initialize(DriverData);

            // TEST LOG:
            // Confirms driver initialization reached transport/protocol setup.
            Log("JellyfishLighting - Initialize complete.");
        }

        private void CreateDeviceDefinition()
        {
            StatusTextProperty = CreateProperty<string>(new PropertyDefinition(StatusTextKey, null, DevicePropertyType.String));
            IsOnlineProperty = CreateProperty<bool>(new PropertyDefinition(IsOnlineKey, null, DevicePropertyType.Boolean));
            ActiveSceneProperty = CreateProperty<string>(new PropertyDefinition(ActiveSceneKey, null, DevicePropertyType.String));
            BrightnessProperty = CreateProperty<int>(new PropertyDefinition(BrightnessKey, null, DevicePropertyType.Int32));
            ZoneSummaryProperty = CreateProperty<string>(new PropertyDefinition(ZoneSummaryKey, null, DevicePropertyType.String));
            SpeedProperty = CreateProperty<int>(new PropertyDefinition(SpeedKey, null, DevicePropertyType.Int32));
            PatternPathProperty = CreateProperty<string>(new PropertyDefinition(PatternPathKey, null, DevicePropertyType.String));
            SelectedZonesProperty = CreateProperty<string>(new PropertyDefinition(SelectedZonesKey, null, DevicePropertyType.String));
            RequestedBrightnessProperty = CreateProperty<int>(new PropertyDefinition(RequestedBrightnessKey, null, DevicePropertyType.Int32));
            RequestedSpeedProperty = CreateProperty<int>(new PropertyDefinition(RequestedSpeedKey, null, DevicePropertyType.Int32));
            UseSslProperty = CreateProperty<bool>(new PropertyDefinition(UseSslKey, null, DevicePropertyType.Boolean));
            PollIntervalSecondsProperty = CreateProperty<int>(new PropertyDefinition(PollIntervalSecondsKey, null, DevicePropertyType.Int32));

            Commit();
        }

        public void Update_UI()
        {
            if (Protocol == null)
            {
                return;
            }

            StatusTextProperty.Value = Protocol.LastStatus;
            IsOnlineProperty.Value = Protocol.LastOnlineState;
            ActiveSceneProperty.Value = Protocol.LastScene;
            BrightnessProperty.Value = Protocol.LastBrightness;
            ZoneSummaryProperty.Value = Protocol.LastZoneSummary;
            SpeedProperty.Value = Protocol.LastSpeed;

            // CHANGED:
            // Keep framework/device Connected state aligned with real protocol state.
            // This avoids the earlier false "online" behavior from Connect().
            Connected = Protocol.LastOnlineState;

            Commit();
        }

        [ProgrammableOperation("^RefreshNowLabel")]
        public void RefreshNow()
        {
            Protocol?.PollNow();
        }

        [ProgrammableOperation("^PowerOnLabel")]
        public void PowerOn()
        {
            Protocol?.SetPowerState(1);
        }

        [ProgrammableOperation("^PowerOffLabel")]
        public void PowerOff()
        {
            Protocol?.SetPowerState(0);
        }

        public void TriggerSceneUpdatedEvent()
        {
            SceneUpdated?.Invoke(this, EventArgs.Empty);
        }

        protected override IOperationResult DoCommand(string command, string[] parameters)
        {
            switch (command)
            {
                case "ShowSettings":
                    UseSslProperty.Value = Settings.UseSsl;
                    PollIntervalSecondsProperty.Value = Settings.PollIntervalSeconds;
                    break;

                case "SaveSettings":
                    Settings.Save(UseSslProperty.Value, PollIntervalSecondsProperty.Value);
                    SaveSetting(Filename, Settings);
                    Protocol?.UpdatePollingInterval(Settings.PollIntervalSeconds);

                    // TEST LOG:
                    Log("JellyfishLighting - SaveSettings useSsl=" + Settings.UseSsl +
                        " pollInterval=" + Settings.PollIntervalSeconds);
                    break;

                case "RefreshNow":
                    Protocol?.PollNow();
                    break;

                case "GetPatternsAndZones":
                    Protocol?.PollNow();
                    break;

                case "PowerOn":
                    Protocol?.SetPowerState(1);
                    break;

                case "PowerOff":
                    Protocol?.SetPowerState(0);
                    break;

                case "RequestPatternData":
                    RequestPatternData();
                    break;

                case "RunSelectedPattern":
                    RunSelectedPattern();
                    break;

                case "SetSelectedBrightness":
                    SetSelectedBrightness();
                    break;

                case "SetSelectedSpeed":
                    SetSelectedSpeed();
                    break;
            }

            Commit();
            return new OperationResult(OperationResultCode.Success);
        }

        protected override IOperationResult SetDriverPropertyValue<T>(string propertyKey, T value)
        {
            switch (propertyKey)
            {
                case UseSslKey:
                    // CHANGED:
                    // Persisted for compatibility, but transport ignores SSL and forces ws://.
                    UseSslProperty.Value = ToBoolean(value);
                    break;

                case PollIntervalSecondsKey:
                    var pollInterval = ToInt(value);
                    if (pollInterval != null)
                    {
                        PollIntervalSecondsProperty.Value = (int)pollInterval;
                    }
                    break;

                case PatternPathKey:
                    PatternPathProperty.Value = ToText(value);
                    break;

                case SelectedZonesKey:
                    SelectedZonesProperty.Value = ToText(value);
                    break;

                case RequestedBrightnessKey:
                    var requestedBrightness = ToInt(value);
                    if (requestedBrightness != null)
                    {
                        RequestedBrightnessProperty.Value = (int)requestedBrightness;
                    }
                    break;

                case RequestedSpeedKey:
                    var requestedSpeed = ToInt(value);
                    if (requestedSpeed != null)
                    {
                        RequestedSpeedProperty.Value = (int)requestedSpeed;
                    }
                    break;
            }

            Commit();
            return new OperationResult(OperationResultCode.Success);
        }

        protected override IOperationResult SetDriverPropertyValue<T>(string objectId, string propertyKey, T value)
        {
            Commit();
            return new OperationResult(OperationResultCode.Success);
        }

        private static bool ToBoolean<T>(T value)
        {
            if (value == null)
            {
                return false;
            }

            if (value is bool)
            {
                return (bool)(object)value;
            }

            bool parsed;
            return bool.TryParse(value.ToString(), out parsed) && parsed;
        }

        private static int? ToInt<T>(T value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is int)
            {
                return (int)(object)value;
            }

            int parsed;
            return int.TryParse(value.ToString(), out parsed) ? parsed : (int?)null;
        }

        private static string ToText<T>(T value)
        {
            return value == null ? string.Empty : value.ToString();
        }

        private void RequestPatternData()
        {
            if (Protocol == null)
            {
                return;
            }

            var patternPath = (PatternPathProperty.Value ?? string.Empty).Trim();
            var separatorIndex = patternPath.LastIndexOf('/');

            if (separatorIndex <= 0 || separatorIndex == patternPath.Length - 1)
            {
                SetUserStatus("Enter pattern path as folder/pattern (example: Holidays/CandyCane).", Protocol.LastOnlineState);
                return;
            }

            var folder = patternPath.Substring(0, separatorIndex);
            var patternName = patternPath.Substring(separatorIndex + 1);
            Protocol.RequestPatternFileData(folder, patternName);
        }

        private void RunSelectedPattern()
        {
            if (Protocol == null)
            {
                return;
            }

            var zones = ParseZones(SelectedZonesProperty.Value);
            if (zones.Length == 0)
            {
                SetUserStatus("Select at least one zone. Use comma-separated names (example: Front Roof,Garage).", Protocol.LastOnlineState);
                return;
            }

            var patternPath = (PatternPathProperty.Value ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(patternPath))
            {
                SetUserStatus("Enter a pattern path before running (folder/pattern).", Protocol.LastOnlineState);
                return;
            }

            Protocol.RunPattern(patternPath, zones, 1);
        }

        private void SetSelectedBrightness()
        {
            if (Protocol == null)
            {
                return;
            }

            var zones = ParseZones(SelectedZonesProperty.Value);
            if (zones.Length == 0)
            {
                SetUserStatus("Select zones before setting brightness. Use comma-separated names.", Protocol.LastOnlineState);
                return;
            }

            Protocol.SetBrightness(RequestedBrightnessProperty.Value, zones);
        }

        private void SetSelectedSpeed()
        {
            if (Protocol == null)
            {
                return;
            }

            var zones = ParseZones(SelectedZonesProperty.Value);
            if (zones.Length == 0)
            {
                SetUserStatus("Select zones before setting speed. Use comma-separated names.", Protocol.LastOnlineState);
                return;
            }

            Protocol.SetSpeed(RequestedSpeedProperty.Value, zones);
        }

        private void SetUserStatus(string message, bool isOnline)
        {
            if (Protocol == null)
            {
                return;
            }

            Protocol.LastStatus = message;
            Protocol.LastOnlineState = isOnline;
            Update_UI();
        }

        private static string[] ParseZones(string zoneText)
        {
            if (string.IsNullOrEmpty(zoneText))
            {
                return new string[0];
            }

            var split = zoneText.Split(',');
            var zones = new System.Collections.Generic.List<string>();

            foreach (var item in split)
            {
                var trimmed = item.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    zones.Add(trimmed);
                }
            }

            return zones.ToArray();
        }

        public override void Connect()
        {
            if (Protocol == null)
            {
                return;
            }

            var temp = GetSetting(Filename);
            Settings = temp != null ? (Settings_Data)temp : new Settings_Data();

            // Keep the setting file present and normalized.
            SaveSetting(Filename, Settings);

            UseSslProperty.Value = Settings.UseSsl;
            PollIntervalSecondsProperty.Value = Settings.PollIntervalSeconds;
            Commit();

            // TEST LOG:
            // Confirms settings before protocol startup.
            Log("JellyfishLighting - Connect settings loaded: useSsl=" + Settings.UseSsl +
                " pollInterval=" + Settings.PollIntervalSeconds);

            base.Connect();

            // CHANGED:
            // Removed Connected = true here.
            // Actual connection state is now updated in Update_UI() from Protocol.LastOnlineState.
            Protocol.Start();
        }

        public override void Disconnect()
        {
            Protocol?.Stop();
            base.Disconnect();
            Connected = false;
        }
    }
}