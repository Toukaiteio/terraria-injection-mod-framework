using System;
using System.IO;
using System.Reflection;
using System.Threading;
using TIMF.Core.Hooks;
using TIMF.Core.Logging;
using TIMF.Core.Modding;

namespace TIMF.Core
{
    /// <summary>
    /// Entry point invoked by the native bootstrap via CLR ExecuteInDefaultAppDomain.
    /// Signature must remain: public static int Initialize(string argument)
    /// </summary>
    public static class Loader
    {
        private static int _started;
        private static FileLogger _log;
        private static ModLoader _modLoader;
        private static GameHooks _hooks;
        private static string _home;

        /// <summary>
        /// argument: absolute path to TIMF home directory (contains Core DLLs, Mods, logs).
        /// </summary>
        public static int Initialize(string argument)
        {
            if (Interlocked.Exchange(ref _started, 1) == 1)
                return 0;

            try
            {
                _home = ResolveHome(argument);
                Directory.CreateDirectory(Path.Combine(_home, "logs"));
                Directory.CreateDirectory(Path.Combine(_home, "Mods"));
                Directory.CreateDirectory(Path.Combine(_home, "config"));

                _log = new FileLogger(Path.Combine(_home, "logs", "timf-core.log"), "Core");
                _log.Info("=== TIMF Core starting ===");
                _log.Info("TIMF version: " + TimfInfo.Version);
                _log.Info("Home: " + _home);
                _log.Info("Argument: " + (argument ?? "(null)"));
                _log.Info("CLR: " + Environment.Version);
                _log.Info("Is64BitProcess: " + Environment.Is64BitProcess);

                AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                {
                    try
                    {
                        _log?.Error("UnhandledException", e.ExceptionObject as Exception
                            ?? new Exception(Convert.ToString(e.ExceptionObject)));
                    }
                    catch { /* ignore */ }
                };

                // Resolve assemblies next to Core (Abstractions, mod deps).
                AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

                // Game may still be booting when we are injected; wait for Terraria.Main.
                if (!WaitForTerraria(TimeSpan.FromSeconds(120)))
                {
                    _log.Error("Timed out waiting for Terraria.Main to become available");
                    return 1;
                }

                try
                {
                    var ver = typeof(Terraria.Main).Assembly.GetName().Version;
                    _log.Info("Terraria assembly version: " + ver);
                }
                catch (Exception ex)
                {
                    _log.Warn("Could not read Terraria version: " + ex.Message);
                }

                _modLoader = new ModLoader(_log, _home);

                _hooks = new GameHooks(_log, _modLoader);
                // Register framework services before mods Load() so they can resolve them.
                _hooks.RegisterServices();

                _modLoader.LoadAll();

                _hooks.Install();

                _log.Info("TIMF Core loaded successfully");
                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    if (_log != null)
                        _log.Error("Initialize failed", ex);
                    else
                        File.AppendAllText(
                            Path.Combine(Path.GetTempPath(), "timf-bootstrap-error.log"),
                            DateTime.Now + " " + ex + Environment.NewLine);
                }
                catch { /* ignore */ }
                return 2;
            }
        }

        private static string ResolveHome(string argument)
        {
            if (!string.IsNullOrWhiteSpace(argument) && Directory.Exists(argument))
                return Path.GetFullPath(argument);

            var env = Environment.GetEnvironmentVariable("TIMF_HOME");
            if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
                return Path.GetFullPath(env);

            // Fallback: directory of TIMF.Core.dll
            var asm = Assembly.GetExecutingAssembly().Location;
            var dir = Path.GetDirectoryName(asm);
            if (!string.IsNullOrEmpty(dir))
                return dir;

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TIMF");
        }

        private static bool WaitForTerraria(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    // Touching the type forces resolution; also proves game assembly is loaded.
                    var t = typeof(Terraria.Main);
                    if (t != null)
                    {
                        // OnPostDraw field/event exists on type even before first frame.
                        var evt = t.GetEvent("OnPostDraw",
                            BindingFlags.Public | BindingFlags.Static);
                        if (evt != null)
                            return true;
                    }
                }
                catch
                {
                    // assembly not ready
                }

                Thread.Sleep(100);
            }
            return false;
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                var name = new AssemblyName(args.Name).Name + ".dll";

                // 1. TIMF home root (Core, Abstractions, Harmony, ...)
                var candidate = Path.Combine(_home ?? "", name);
                if (File.Exists(candidate))
                    return Assembly.LoadFrom(candidate);

                // 2. Next to Core DLL
                var coreDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(coreDir))
                {
                    candidate = Path.Combine(coreDir, name);
                    if (File.Exists(candidate))
                        return Assembly.LoadFrom(candidate);
                }

                // 3. Any per-mod folder under Mods/ (bundled private dependencies)
                var modsDir = Path.Combine(_home ?? "", "Mods");
                if (Directory.Exists(modsDir))
                {
                    foreach (var dir in Directory.GetDirectories(modsDir))
                    {
                        candidate = Path.Combine(dir, name);
                        if (File.Exists(candidate))
                            return Assembly.LoadFrom(candidate);
                    }
                }
            }
            catch
            {
                // ignore
            }
            return null;
        }
    }
}
