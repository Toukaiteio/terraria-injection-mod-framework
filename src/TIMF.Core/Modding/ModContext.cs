using System.IO;
using TIMF.Abstractions;
using TIMF.Abstractions.Security;
using TIMF.Abstractions.Storage;

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
        public IModLocalization L { get; }
        public IClientServices Client { get; }
        public IAuthorityServices Authority { get; }
        public ISensitiveOperationService Security { get; }
        public IModStorage Storage { get; }
        public IModPatchService Patches { get; }
        public IModServicePublisher ServicePublisher { get; }

        public ModContext(
            ILogger log,
            string home,
            string configDir,
            string modDir,
            string assemblyPath,
            IServiceRegistry services,
            IModLocalization localization,
            IClientServices client,
            IAuthorityServices authority,
            ISensitiveOperationService security,
            IModStorage storage,
            IModPatchService patches,
            IModServicePublisher servicePublisher)
        {
            Log = log;
            HomeDirectory = home;
            ConfigDirectory = configDir;
            ModDirectory = modDir;
            ModAssemblyPath = assemblyPath;
            Services = services;
            L = localization;
            Client = client;
            Authority = authority;
            Security = security;
            Storage = storage;
            Patches = patches;
            ServicePublisher = servicePublisher;

            var content = Path.Combine(modDir ?? "", "Content");
            ContentDirectory = Directory.Exists(content) ? content : modDir;
        }
    }
}
