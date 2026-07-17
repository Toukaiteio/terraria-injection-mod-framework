using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TIMF.Abstractions;

namespace TIMF.Core.Modding
{
    internal sealed class ModLoader
    {
        private readonly ILogger _log;
        private readonly string _home;
        private readonly string _modsDir;
        private readonly string _configDir;
        private readonly List<IMod> _mods = new List<IMod>();

        public IReadOnlyList<IMod> Mods => _mods;

        public ModLoader(ILogger log, string home)
        {
            _log = log;
            _home = home;
            _modsDir = Path.Combine(home, "Mods");
            _configDir = Path.Combine(home, "config");
            Directory.CreateDirectory(_modsDir);
            Directory.CreateDirectory(_configDir);
        }

        public void LoadAll()
        {
            var files = Directory.GetFiles(_modsDir, "*.dll", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            _log.Info("Scanning mods in " + _modsDir + " (" + files.Length + " dlls)");

            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith("TIMF.", StringComparison.OrdinalIgnoreCase))
                {
                    _log.Debug("Skipping framework assembly: " + name);
                    continue;
                }

                try
                {
                    LoadOne(file);
                }
                catch (Exception ex)
                {
                    _log.Error("Failed to load mod " + name, ex);
                }
            }

            _log.Info("Loaded " + _mods.Count + " mod(s)");
        }

        private void LoadOne(string path)
        {
            // LoadFrom keeps path identity so content next to the DLL is easy to find.
            var asm = Assembly.LoadFrom(path);
            var entryType = FindModType(asm);
            if (entryType == null)
            {
                _log.Warn("No IMod entry type in " + Path.GetFileName(path));
                return;
            }

            var mod = (IMod)Activator.CreateInstance(entryType);
            var modDir = Path.GetDirectoryName(path) ?? _modsDir;
            var modLog = new Logging.FileLogger(
                Path.Combine(_home, "logs", "mod-" + Sanitize(mod.Name) + ".log"),
                mod.Name);
            var ctx = new ModContext(modLog, _home, _configDir, modDir, path);

            _log.Info("Loading mod " + mod.Name + " v" + mod.Version + " from " + Path.GetFileName(path));
            mod.Load(ctx);
            _mods.Add(mod);
            _log.Info("Mod ready: " + mod.Name);
        }

        private static Type FindModType(Assembly asm)
        {
            var types = SafeGetTypes(asm);
            var marked = types.FirstOrDefault(t =>
                typeof(IMod).IsAssignableFrom(t)
                && !t.IsAbstract
                && t.IsClass
                && t.GetCustomAttributes(typeof(TimfModAttribute), false).Length > 0);
            if (marked != null)
                return marked;

            return types.FirstOrDefault(t =>
                typeof(IMod).IsAssignableFrom(t)
                && !t.IsAbstract
                && t.IsClass
                && t.GetConstructor(Type.EmptyTypes) != null);
        }

        private static Type[] SafeGetTypes(Assembly asm)
        {
            try
            {
                return asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null).ToArray();
            }
        }

        private static string Sanitize(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        public void UnloadAll()
        {
            for (var i = _mods.Count - 1; i >= 0; i--)
            {
                try
                {
                    _mods[i].Unload();
                }
                catch (Exception ex)
                {
                    _log.Error("Unload failed for " + _mods[i].Name, ex);
                }
            }
            _mods.Clear();
        }
    }
}
