namespace TIMF.Core
{
    public static class TimfInfo
    {
        public const string Version = "1.0.0";
        public const string DisplayName = "TIMF";

        /// <summary>Text shown on the main menu above the game version.</summary>
        public static string MenuVersionText => DisplayName + " v" + Version;
    }
}
