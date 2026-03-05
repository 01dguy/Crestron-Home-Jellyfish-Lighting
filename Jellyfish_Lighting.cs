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
		private const string UseSslKey = "UseSsl";
		private const string PollIntervalSecondsKey = "PollIntervalSeconds";

		private PropertyValue<string> StatusTextProperty;
		private PropertyValue<bool> IsOnlineProperty;
		private PropertyValue<string> ActiveSceneProperty;
		private PropertyValue<int> BrightnessProperty;
		private PropertyValue<string> ZoneSummaryProperty;
		private PropertyValue<int> SpeedProperty;
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
			DeviceProtocol = Protocol;
			DeviceProtocol.Initialize(DriverData);
		}

		private void CreateDeviceDefinition()
		{
			StatusTextProperty = CreateProperty<string>(new PropertyDefinition(StatusTextKey, null, DevicePropertyType.String));
			IsOnlineProperty = CreateProperty<bool>(new PropertyDefinition(IsOnlineKey, null, DevicePropertyType.Boolean));
			ActiveSceneProperty = CreateProperty<string>(new PropertyDefinition(ActiveSceneKey, null, DevicePropertyType.String));
			BrightnessProperty = CreateProperty<int>(new PropertyDefinition(BrightnessKey, null, DevicePropertyType.Int32));
			ZoneSummaryProperty = CreateProperty<string>(new PropertyDefinition(ZoneSummaryKey, null, DevicePropertyType.String));
			SpeedProperty = CreateProperty<int>(new PropertyDefinition(SpeedKey, null, DevicePropertyType.Int32));
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
			Commit();

			base.Connect();
			Connected = true;
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
