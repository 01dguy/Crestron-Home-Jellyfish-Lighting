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
        private const string HasParentScenesKey = "HasParentScenes"; //Validation helper for UI - true if ParentScenes list is non-empty
        private const string SelectedParentSceneIdKey = "SelectedParentSceneId";
        private const string SelectedParentSceneNameKey = "SelectedParentSceneName";

        private const string PatternPathKey = "PatternPath";
        private const string SelectedZonesKey = "SelectedZones";
        private const string ControllerHostDisplayKey = "ControllerHostDisplay";

        private const string UseSslKey = "UseSsl";
        private const string PollIntervalSecondsKey = "PollIntervalSeconds";

        private PropertyValue<string> StatusTextProperty;
        private PropertyValue<bool> IsOnlineProperty;
        private PropertyValue<string> ActiveSceneProperty;
        private PropertyValue<string> ZoneSummaryProperty;

        private PropertyValue<bool> PowerStateProperty;

        private ObjectList ParentScenesProperty;
        private ObjectList ChildScenesProperty;
        private PropertyValue<bool> HasParentScenesProperty; //Validation helper for UI - true if ParentScenes list is non-empty
        private PropertyValue<string> SelectedParentSceneIdProperty;
        private PropertyValue<string> SelectedParentSceneNameProperty;

        private PropertyValue<string> PatternPathProperty;
        private PropertyValue<string> SelectedZonesProperty;
        private PropertyValue<string> ControllerHostDisplayProperty;

        private PropertyValue<bool> UseSslProperty;
        private PropertyValue<int> PollIntervalSecondsProperty;
        private ClassDefinition SceneListItemClass;

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

            Log("JellyfishLighting - Initialize complete.");
        }

        private void CreateDeviceDefinition()
        {
            StatusTextProperty = CreateProperty<string>(new PropertyDefinition(StatusTextKey, null, DevicePropertyType.String));
            IsOnlineProperty = CreateProperty<bool>(new PropertyDefinition(IsOnlineKey, null, DevicePropertyType.Boolean));
            ActiveSceneProperty = CreateProperty<string>(new PropertyDefinition(ActiveSceneKey, null, DevicePropertyType.String));
            ZoneSummaryProperty = CreateProperty<string>(new PropertyDefinition(ZoneSummaryKey, null, DevicePropertyType.String));

            PowerStateProperty = CreateProperty<bool>(new PropertyDefinition(PowerStateKey, null, DevicePropertyType.Boolean));

            SceneListItemClass = CreateClassDefinition("SceneListItem");
            SceneListItemClass.AddProperty(new PropertyDefinition("id", string.Empty, DevicePropertyType.String));
            SceneListItemClass.AddProperty(new PropertyDefinition("name", string.Empty, DevicePropertyType.String));

            ParentScenesProperty = CreateList(
                new PropertyDefinition(ParentScenesKey, string.Empty, DevicePropertyType.ObjectList, SceneListItemClass));

            ChildScenesProperty = CreateList(
                new PropertyDefinition(ChildScenesKey, string.Empty, DevicePropertyType.ObjectList, SceneListItemClass));

            SelectedParentSceneIdProperty = CreateProperty<string>(new PropertyDefinition(SelectedParentSceneIdKey, null, DevicePropertyType.String));
            SelectedParentSceneNameProperty = CreateProperty<string>(new PropertyDefinition(SelectedParentSceneNameKey, null, DevicePropertyType.String));

            PatternPathProperty = CreateProperty<string>(new PropertyDefinition(PatternPathKey, null, DevicePropertyType.String));
            SelectedZonesProperty = CreateProperty<string>(new PropertyDefinition(SelectedZonesKey, null, DevicePropertyType.String));
            ControllerHostDisplayProperty = CreateProperty<string>(new PropertyDefinition(ControllerHostDisplayKey, null, DevicePropertyType.String));

            UseSslProperty = CreateProperty<bool>(new PropertyDefinition(UseSslKey, null, DevicePropertyType.Boolean));
            PollIntervalSecondsProperty = CreateProperty<int>(new PropertyDefinition(PollIntervalSecondsKey, null, DevicePropertyType.Int32));

            HasParentScenesProperty = CreateProperty<bool>(new PropertyDefinition(HasParentScenesKey, null, DevicePropertyType.Boolean));
            HasParentScenesProperty.Value = false;

            ParentScenesProperty.Clear();
            ChildScenesProperty.Clear();

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
                "Jellyfish is ON",
                StringComparison.OrdinalIgnoreCase);

            Connected = Protocol.LastOnlineState;

            Commit();
        }

        [ProgrammableOperation("^RefreshNowLabel")]
        public void RefreshNow()
        {
            Protocol?.PollNow();
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
                    ControllerHostDisplayProperty.Value = Protocol != null ? Protocol.GetControllerHost() : string.Empty;
                    UseSslProperty.Value = Settings.UseSsl;
                    PollIntervalSecondsProperty.Value = Settings.PollIntervalSeconds;
                    break;

                case "SaveSettings":
                    Settings.Save(UseSslProperty.Value, PollIntervalSecondsProperty.Value);
                    SaveSetting(Filename, Settings);
                    Protocol?.UpdatePollingInterval(Settings.PollIntervalSeconds);

                    Log("JellyfishLighting - SaveSettings useSsl=" + Settings.UseSsl +
                        " pollInterval=" + Settings.PollIntervalSeconds);
                    break;

                case "RefreshNow":
                    Protocol?.PollNow();
                    break;

                case "GetPatternsAndZones":
                    Protocol?.PollNow();
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

                // Compatibility/fallback commands
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

                case ControllerHostDisplayKey:
                    ControllerHostDisplayProperty.Value = ToText(value);
                    if (Protocol != null)
                    {
                        Protocol.SetUserAttribute("ControllerHost", ControllerHostDisplayProperty.Value);
                    }
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
                "Jellyfish is ON",
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
            // Expected: [parentId, parentName]
            if (parameters == null || parameters.Length < 1)
            {
                Log("JellyfishLighting - SelectParentScene called without parameters.");
                return;
            }

            var parentId = parameters[0] ?? string.Empty;
            var parentName = parameters.Length > 1 ? (parameters[1] ?? string.Empty) : parentId;

            SelectedParentSceneIdProperty.Value = parentId;
            SelectedParentSceneNameProperty.Value = parentName;

            RebuildChildScenesObjectList(parentId);

            Log("JellyfishLighting - Scene category selected: id='" + parentId + "' name='" + parentName + "'.");

            Commit();
        }

        //NEW
        private void RunChildScene(string[] parameters)
        {
            // Expected now: [childId, childName]
            if (Protocol == null || parameters == null || parameters.Length < 1)
            {
                return;
            }

            var parentId = SelectedParentSceneIdProperty.Value ?? string.Empty;
            var childName = parameters.Length > 1 ? parameters[1] : parameters[0];

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
                // Fallback: if UI zones are blank, use currently known controller zones.
                zones = Protocol.GetKnownZonesSnapshot();
                if (zones.Length > 0)
                {
                    SelectedZonesProperty.Value = string.Join(",", zones);
                }
            }

            if (zones.Length == 0)
            {
                SetUserStatus("Select at least one zone. Use comma-separated names (example: Front Roof,Garage).", Protocol.LastOnlineState);
                return;
            }

            var patternPath = (PatternPathProperty.Value ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(patternPath))
            {
                patternPath = TryAutoSelectFirstScenePath();
                if (!string.IsNullOrEmpty(patternPath))
                {
                    PatternPathProperty.Value = patternPath;
                }
            }

            if (string.IsNullOrEmpty(patternPath))
            {
                SetUserStatus("Select a scene before turning on.", Protocol.LastOnlineState);
                return;
            }

            Protocol.RunPattern(patternPath, zones, 1);
        }

        // This method tries to auto-select the first available scene if none is currently selected.
        private string TryAutoSelectFirstScenePath()
        {
            if (Protocol == null)
            {
                return string.Empty;
            }

            var parentNames = Protocol.GetParentSceneNames();
            if (parentNames == null || parentNames.Length == 0)
            {
                return string.Empty;
            }

            var parentId = parentNames[0];
            if (string.IsNullOrEmpty(parentId))
            {
                return string.Empty;
            }

            SelectedParentSceneIdProperty.Value = parentId;
            SelectedParentSceneNameProperty.Value = parentId;

            var childNames = Protocol.GetChildSceneNames(parentId);
            if (childNames == null || childNames.Length == 0)
            {
                return string.Empty;
            }

            var childId = childNames[0];
            if (string.IsNullOrEmpty(childId))
            {
                return string.Empty;
            }

            RebuildSceneObjectList(ChildScenesProperty, childNames);
            return parentId + "/" + childId;
        }

        private void RefreshSceneListsFromProtocol()
        {
            if (Protocol == null)
            {
                ParentScenesProperty.Clear();
                ChildScenesProperty.Clear();
                HasParentScenesProperty.Value = false;
                return;
            }

            var parentNames = Protocol.GetParentSceneNames();
            RebuildSceneObjectList(ParentScenesProperty, parentNames);

            HasParentScenesProperty.Value = parentNames != null && parentNames.Length > 0;

            var parentId = SelectedParentSceneIdProperty.Value ?? string.Empty;
            var childNames = string.IsNullOrEmpty(parentId)
                ? new string[0]
                : Protocol.GetChildSceneNames(parentId);

            RebuildSceneObjectList(ChildScenesProperty, childNames);

            if (!HasParentScenesProperty.Value)
            {
                Log("JellyfishLighting - No scene categories available yet.");
            }
        }

        //This does the initial build of ObjectList for scene patterns
        public void RebuildSceneListsAndCommit()
        {
            RefreshSceneListsFromProtocol();
            Commit();
        }

        //NEW
        private void RebuildSceneObjectList(ObjectList targetList, string[] names)
        {
            targetList.Clear();

            if (names == null || names.Length == 0)
            {
                return;
            }

            for (var i = 0; i < names.Length; i++)
            {
                var sceneName = names[i];
                if (string.IsNullOrEmpty(sceneName))
                {
                    continue;
                }

                ObjectValue item = CreateObject(SceneListItemClass);
                item.GetValue<string>("id").Value = sceneName;
                item.GetValue<string>("name").Value = sceneName;

                targetList.AddObject(item);
            }
        }

        //KEEP
        private void RebuildParentScenesObjectList()
        {
            ParentScenesProperty.Clear();

            if (Protocol == null)
            {
                return;
            }

            var names = Protocol.GetParentSceneNames();
            if (names == null || names.Length == 0)
            {
                return;
            }

            for (var i = 0; i < names.Length; i++)
            {
                var sceneName = names[i];
                if (string.IsNullOrEmpty(sceneName))
                {
                    continue;
                }

                ObjectValue item = CreateObject(SceneListItemClass);
                item.GetValue<string>("id").Value = sceneName;
                item.GetValue<string>("name").Value = sceneName;

                ParentScenesProperty.AddObject(item);
            }
        }

        //KEEP
        private void RebuildChildScenesObjectList(string parentId)
        {
            ChildScenesProperty.Clear();

            if (Protocol == null || string.IsNullOrEmpty(parentId))
            {
                return;
            }

            var names = Protocol.GetChildSceneNames(parentId);
            if (names == null || names.Length == 0)
            {
                return;
            }

            for (var i = 0; i < names.Length; i++)
            {
                var sceneName = names[i];
                if (string.IsNullOrEmpty(sceneName))
                {
                    continue;
                }

                ObjectValue item = CreateObject(SceneListItemClass);
                item.GetValue<string>("id").Value = sceneName;
                item.GetValue<string>("name").Value = sceneName;

                ChildScenesProperty.AddObject(item);
            }
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
            SaveSetting(Filename, Settings);

            UseSslProperty.Value = Settings.UseSsl;
            PollIntervalSecondsProperty.Value = Settings.PollIntervalSeconds;
            ControllerHostDisplayProperty.Value = Protocol.GetControllerHost();

            ParentScenesProperty.Clear();
            ChildScenesProperty.Clear();
            HasParentScenesProperty.Value = false;

            Commit();

            Log("JellyfishLighting - Connect settings loaded: useSsl=" + Settings.UseSsl +
                " pollInterval=" + Settings.PollIntervalSeconds);

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
