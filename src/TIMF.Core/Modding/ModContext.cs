using System.IO;
using TIMF.Abstractions;

namespace TIMF.Core.Modding
{
    internal sealed class ModContext : IModContext
    {
        public ILogger Log { get; }
        public string HomeDirectory { get; }
        public string ConfigDirectory { get; }
        public string ModDirectory { get; }
        public string ContentDirectory { get; }
        public string ModAssemblyPath { get; }
        public IServiceRegistry Services { get; }

        public ModContext(
            ILogger log,
            string home,
            string configDir,
            string modDir,
            string assemblyPath,
            IServiceRegistry services)
        {
            Log = log;
            HomeDirectory = home;
            ConfigDirectory = configDir;
            ModDirectory = modDir;
            ModAssemblyPath = assemblyPath;
            Services = services;

            // Prefer a dedicated Content/ subfolder when present, else the mod folder itself.
            var content = Path.Combine(modDir ?? "", "Content");
            ContentDirectory = Directory.Exists(content) ? content : modDir;
        }
    }
}
