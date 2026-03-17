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
        private const string ZoneSummaryKey = "ZoneSummary";

        private const string PowerStateKey = "PowerState";

        private const string ParentScenesKey = "ParentScenes";
        private const string ChildScenesKey = "ChildScenes";
        private const string SelectedParentSceneIdKey = "SelectedParentSceneId";
        private const string SelectedParentSceneNameKey = "SelectedParentSceneName";

        private const string PatternPathKey = "PatternPath";
        private const string SelectedZonesKey = "SelectedZones";

        private const string UseSslKey = "UseSsl";
        private const string PollIntervalSecondsKey = "PollIntervalSeconds";

        private PropertyValue<string> StatusTextProperty;
        private PropertyValue<bool> IsOnlineProperty;
        private PropertyValue<string> ActiveSceneProperty;
        private PropertyValue<string> ZoneSummaryProperty;

        private PropertyValue<bool> PowerStateProperty;

        private PropertyValue<string> ParentScenesProperty;
        private PropertyValue<string> ChildScenesProperty;
        private PropertyValue<string> SelectedParentSceneIdProperty;
        private PropertyValue<string> SelectedParentSceneNameProperty;

        private PropertyValue<string> PatternPathProperty;
        private PropertyValue<string> SelectedZonesProperty;

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
        }

        private void CreateDeviceDefinition()
        {
            StatusTextProperty = CreateProperty<string>(new PropertyDefinition(StatusTextKey, null, DevicePropertyType.String));
            IsOnlineProperty = CreateProperty<bool>(new PropertyDefinition(IsOnlineKey, null, DevicePropertyType.Boolean));
            ActiveSceneProperty = CreateProperty<string>(new PropertyDefinition(ActiveSceneKey, null, DevicePropertyType.String));
            ZoneSummaryProperty = CreateProperty<string>(new PropertyDefinition(ZoneSummaryKey, null, DevicePropertyType.String));

            PowerStateProperty = CreateProperty<bool>(new PropertyDefinition(PowerStateKey, null, DevicePropertyType.Boolean));

            ParentScenesProperty = CreateProperty<string>(new PropertyDefinition(ParentScenesKey, null, DevicePropertyType.String));
            ChildScenesProperty = CreateProperty<string>(new PropertyDefinition(ChildScenesKey, null, DevicePropertyType.String));
            SelectedParentSceneIdProperty = CreateProperty<string>(new PropertyDefinition(SelectedParentSceneIdKey, null, DevicePropertyType.String));
            SelectedParentSceneNameProperty = CreateProperty<string>(new PropertyDefinition(SelectedParentSceneNameKey, null, DevicePropertyType.String));

            PatternPathProperty = CreateProperty<string>(new PropertyDefinition(PatternPathKey, null, DevicePropertyType.String));
            SelectedZonesProperty = CreateProperty<string>(new PropertyDefinition(SelectedZonesKey, null, DevicePropertyType.String));

            UseSslProperty = CreateProperty<bool>(new PropertyDefinition(UseSslKey, null, DevicePropertyType.Boolean));
            PollIntervalSecondsProperty = CreateProperty<int>(new PropertyDefinition(PollIntervalSecondsKey, null, DevicePropertyType.Int32));

            ParentScenesProperty.Value = "[]";
            ChildScenesProperty.Value = "[]";

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
            ZoneSummaryProperty.Value = Protocol.LastZoneSummary;

            PowerStateProperty.Value = string.Equals(
                Protocol.LastPowerStatus,
                "LED power is ON",
                StringComparison.OrdinalIgnoreCase);

            Connected = Protocol.LastOnlineState;
            RefreshSceneListsFromProtocol();

            Commit();
        }

        [ProgrammableOperation("^RefreshNowLabel")]
        public void RefreshNow()
        {
            Protocol?.PollNow();
            RefreshSceneListsFromProtocol();
        }

        [ProgrammableOperation("^GetPatternsAndZonesLabel")]
        public void GetPatternsAndZones()
        {
            Protocol?.PollNow();
            RefreshSceneListsFromProtocol();
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
                    Protocol?.ApplySettings(Settings.UseSsl, Settings.PollIntervalSeconds);
                    break;

                case "RefreshNow":
                case "GetPatternsAndZones":
                    Protocol?.PollNow();
                    RefreshSceneListsFromProtocol();
                    break;

                case "TogglePatternPower":
                    TogglePatternPower();
                    break;

                case "SelectParentScene":
                    SelectParentScene(parameters);
                    break;

                case "RunChildScene":
                    RunChildScene(parameters);
                    break;

                case "RunSelectedPattern":
                    RunSelectedPattern();
                    break;

                case "PowerOff":
                    Protocol?.SetPowerState(0);
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

                case SelectedParentSceneIdKey:
                    SelectedParentSceneIdProperty.Value = ToText(value);
                    break;

                case SelectedParentSceneNameKey:
                    SelectedParentSceneNameProperty.Value = ToText(value);
                    break;

                case PowerStateKey:
                    var desiredPower = ToBoolean(value);
                    PowerStateProperty.Value = desiredPower;

                    if (desiredPower)
                    {
                        RunSelectedPattern();
                    }
                    else
                    {
                        Protocol?.SetPowerState(0);
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
            if (value == null) return false;
            if (value is bool) return (bool)(object)value;

            bool parsed;
            return bool.TryParse(value.ToString(), out parsed) && parsed;
        }

        private static int? ToInt<T>(T value)
        {
            if (value == null) return null;
            if (value is int) return (int)(object)value;

            int parsed;
            return int.TryParse(value.ToString(), out parsed) ? parsed : (int?)null;
        }

        private static string ToText<T>(T value)
        {
            return value == null ? string.Empty : value.ToString();
        }

        private void TogglePatternPower()
        {
            if (Protocol == null)
            {
                return;
            }

            var isOn = string.Equals(
                Protocol.LastPowerStatus,
                "LED power is ON",
                StringComparison.OrdinalIgnoreCase);

            if (isOn)
            {
                Protocol.SetPowerState(0);
                return;
            }

            RunSelectedPattern();
        }

        private void SelectParentScene(string[] parameters)
        {
            if (parameters == null || parameters.Length < 1)
            {
                return;
            }

            var parentId = parameters[0] ?? string.Empty;
            var parentName = parameters.Length > 1 ? (parameters[1] ?? string.Empty) : parentId;

            SelectedParentSceneIdProperty.Value = parentId;
            SelectedParentSceneNameProperty.Value = parentName;

            ChildScenesProperty.Value = GetChildScenesJsonOrEmpty(parentId);
            Commit();
        }

        private void RunChildScene(string[] parameters)
        {
            if (Protocol == null || parameters == null || parameters.Length < 3)
            {
                return;
            }

            var parentId = !string.IsNullOrEmpty(parameters[0])
                ? parameters[0]
                : SelectedParentSceneIdProperty.Value;

            var childName = parameters.Length > 3 ? parameters[3] : parameters[2];

            if (string.IsNullOrEmpty(parentId) || string.IsNullOrEmpty(childName))
            {
                SetUserStatus("Select a valid parent and child scene.", Protocol.LastOnlineState);
                return;
            }

            PatternPathProperty.Value = parentId + "/" + childName;
            RunSelectedPattern();
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
                SetUserStatus("Select a scene before turning on.", Protocol.LastOnlineState);
                return;
            }

            Protocol.RunPattern(patternPath, zones, 1);
        }

        private void RefreshSceneListsFromProtocol()
        {
            ParentScenesProperty.Value = GetParentScenesJsonOrEmpty();

            var parentId = SelectedParentSceneIdProperty.Value ?? string.Empty;
            ChildScenesProperty.Value = string.IsNullOrEmpty(parentId)
                ? "[]"
                : GetChildScenesJsonOrEmpty(parentId);
        }

        private string GetParentScenesJsonOrEmpty()
        {
            if (Protocol == null)
            {
                return "[]";
            }

            var result = Protocol.GetParentScenesAsUiListJson();
            return string.IsNullOrEmpty(result) ? "[]" : result;
        }

        private string GetChildScenesJsonOrEmpty(string parentId)
        {
            if (Protocol == null || string.IsNullOrEmpty(parentId))
            {
                return "[]";
            }

            var result = Protocol.GetChildScenesAsUiListJson(parentId);
            return string.IsNullOrEmpty(result) ? "[]" : result;
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

            for (var i = 0; i < split.Length; i++)
            {
                var trimmed = split[i].Trim();
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
            SaveSetting(Filename, Settings);

            UseSslProperty.Value = Settings.UseSsl;
            PollIntervalSecondsProperty.Value = Settings.PollIntervalSeconds;
            Protocol.ApplySettings(Settings.UseSsl, Settings.PollIntervalSeconds);

            ParentScenesProperty.Value = "[]";
            ChildScenesProperty.Value = "[]";

            Commit();

            base.Connect();
            Protocol.Start();
            RefreshSceneListsFromProtocol();
        }

        public override void Disconnect()
        {
            Protocol?.Stop();
            base.Disconnect();
            Connected = false;
        }
    }
}
