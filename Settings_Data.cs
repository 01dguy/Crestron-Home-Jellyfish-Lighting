namespace JellyfishLighting.ExtensionDriver
{
	public class Settings_Data
	{
		public bool UseSsl;
		public int PollIntervalSeconds;

		public Settings_Data()
		{
			UseSsl = false;
			PollIntervalSeconds = 60;
		}

		public void Save(bool useSsl, int pollIntervalSeconds)
		{
			UseSsl = useSsl;
			PollIntervalSeconds = pollIntervalSeconds < 10 ? 10 : pollIntervalSeconds;
		}
	}
}
