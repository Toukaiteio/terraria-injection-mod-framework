using TIMF.Abstractions;

namespace TIMF.Core.Modding
{
    internal sealed class ModContext : IModContext
    {
        public ILogger Log { get; }
        public string HomeDirectory { get; }
        public string ConfigDirectory { get; }
        public string ModDirectory { get; }
        public string ModAssemblyPath { get; }

        public ModContext(ILogger log, string home, string configDir, string modDir, string assemblyPath)
        {
            Log = log;
            HomeDirectory = home;
            ConfigDirectory = configDir;
            ModDirectory = modDir;
            ModAssemblyPath = assemblyPath;
        }
    }
}
