namespace JellyfishLighting.ExtensionDriver
{
    public class Settings_Data
    {
        // NOTE:
        // UseSsl is kept here for compatibility with existing saved settings,
        // but the transport is now forced to ws:// only.
        // This prevents accidental wss:// attempts.
        public bool UseSsl;

        public int PollIntervalSeconds;

        public Settings_Data()
        {
            // CHANGED:
            // Default remains false, but transport ignores SSL now.
            UseSsl = false;

            PollIntervalSeconds = 180;
        }

        public void Save(bool useSsl, int pollIntervalSeconds)
        {
            // CHANGED:
            // Preserve the field for compatibility, but force false so the rest
            // of the driver never drifts back toward wss:// behavior.
            UseSsl = false;

            // Existing clamp preserved.
            PollIntervalSeconds = pollIntervalSeconds < 10 ? 10 : pollIntervalSeconds;
        }
    }
}