using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace TIMF.Launcher
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                var home = ResolveHome();
                Directory.CreateDirectory(home);
                Directory.CreateDirectory(Path.Combine(home, "logs"));
                Directory.CreateDirectory(Path.Combine(home, "Mods"));
                Directory.CreateDirectory(Path.Combine(home, "config"));

                var logPath = Path.Combine(home, "logs", "launcher.log");
                Log(logPath, "TIMF Launcher starting");
                Log(logPath, "Home: " + home);

                var configPath = Path.Combine(home, "timf.json");
                var gamePath = ResolveTerrariaPath(configPath, args);
                if (string.IsNullOrEmpty(gamePath) || !File.Exists(gamePath))
                {
                    Console.Error.WriteLine("Terraria.exe not found.");
                    Console.Error.WriteLine("Pass path as argument, or set \"gamePath\" in " + configPath);
                    Log(logPath, "Terraria.exe not found");
                    return 1;
                }

                SaveConfig(configPath, gamePath);
                Log(logPath, "Game: " + gamePath);

                var bootstrap = Path.Combine(home, "TIMF.Bootstrap.dll");
                if (!File.Exists(bootstrap))
                {
                    // Also try next to launcher
                    var alt = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TIMF.Bootstrap.dll");
                    if (File.Exists(alt))
                        bootstrap = alt;
                }

                if (!File.Exists(bootstrap))
                {
                    Console.Error.WriteLine("TIMF.Bootstrap.dll not found in " + home);
                    Log(logPath, "Bootstrap missing");
                    return 2;
                }

                EnsureCorePresent(home, logPath);

                var workDir = Path.GetDirectoryName(gamePath);
                var si = new Native.STARTUPINFO { cb = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.STARTUPINFO)) };

                // Pass TIMF_HOME to the child via environment block prepend is complex;
                // write a sidecar file the bootstrap reads, and also set process env before CreateProcess inherit.
                Environment.SetEnvironmentVariable("TIMF_HOME", home);
                WriteHomeSidecar(home, workDir);

                var cmd = "\"" + gamePath + "\"";
                // Prefer suspended start → inject → resume so we load early.
                // lpEnvironment=null inherits this process env (including TIMF_HOME).
                if (!Native.CreateProcess(
                        gamePath,
                        cmd,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        false,
                        Native.CREATE_SUSPENDED,
                        IntPtr.Zero,
                        workDir,
                        ref si,
                        out var pi))
                {
                    Console.Error.WriteLine("CreateProcess failed: " + Native.GetLastErrorMessage());
                    Log(logPath, "CreateProcess failed: " + Native.GetLastErrorMessage());
                    return 3;
                }

                Log(logPath, "Created process pid=" + pi.dwProcessId);
                Console.WriteLine("Started Terraria pid=" + pi.dwProcessId);

                try
                {
                    // Small delay so process PEB/loader is ready (still suspended primary thread).
                    System.Threading.Thread.Sleep(200);

                    Log(logPath, "Injecting " + bootstrap);
                    Injector.Inject(pi.hProcess, bootstrap);
                    Log(logPath, "Injection OK");
                    Console.WriteLine("Injected TIMF.Bootstrap.dll");
                }
                catch (Exception ex)
                {
                    Log(logPath, "Injection failed: " + ex);
                    Console.Error.WriteLine("Injection failed: " + ex.Message);
                    try { Process.GetProcessById(pi.dwProcessId).Kill(); } catch { /* ignore */ }
                    return 4;
                }
                finally
                {
                    Native.ResumeThread(pi.hThread);
                    Native.CloseHandle(pi.hThread);
                    Native.CloseHandle(pi.hProcess);
                }

                Console.WriteLine("TIMF home: " + home);
                Console.WriteLine("Logs: " + Path.Combine(home, "logs"));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 99;
            }
        }

        private static string ResolveHome()
        {
            var env = Environment.GetEnvironmentVariable("TIMF_HOME");
            if (!string.IsNullOrWhiteSpace(env))
                return Path.GetFullPath(env);

            // Prefer dist next to launcher (deploy layout).
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.GetFullPath(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        private static string ResolveTerrariaPath(string configPath, string[] args)
        {
            if (args != null && args.Length > 0 && File.Exists(args[0]))
                return Path.GetFullPath(args[0]);

            if (File.Exists(configPath))
            {
                try
                {
                    var text = File.ReadAllText(configPath);
                    var path = ExtractJsonString(text, "gamePath");
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        return path;
                }
                catch { /* ignore */ }
            }

            var candidates = new[]
            {
                @"E:\SteamLibrary\steamapps\common\Terraria\Terraria.exe",
                @"C:\Program Files (x86)\Steam\steamapps\common\Terraria\Terraria.exe",
                @"D:\SteamLibrary\steamapps\common\Terraria\Terraria.exe",
                @"D:\Program Files (x86)\Steam\steamapps\common\Terraria\Terraria.exe",
            };
            foreach (var c in candidates)
            {
                if (File.Exists(c))
                    return c;
            }

            // Steam registry
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    var steamPath = k?.GetValue("SteamPath") as string;
                    if (!string.IsNullOrEmpty(steamPath))
                    {
                        var p = Path.Combine(steamPath.Replace('/', '\\'), "steamapps", "common", "Terraria", "Terraria.exe");
                        if (File.Exists(p))
                            return p;
                    }
                }
            }
            catch { /* ignore */ }

            return null;
        }

        private static void SaveConfig(string configPath, string gamePath)
        {
            var json = "{\n  \"gamePath\": \"" + gamePath.Replace("\\", "\\\\") + "\"\n}\n";
            File.WriteAllText(configPath, json, Encoding.UTF8);
        }

        private static string ExtractJsonString(string json, string key)
        {
            var token = "\"" + key + "\"";
            var i = json.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            i = json.IndexOf(':', i);
            if (i < 0) return null;
            i = json.IndexOf('"', i + 1);
            if (i < 0) return null;
            var j = json.IndexOf('"', i + 1);
            if (j < 0) return null;
            return json.Substring(i + 1, j - i - 1).Replace("\\\\", "\\");
        }

        private static void WriteHomeSidecar(string home, string gameDir)
        {
            // Bootstrap looks for TIMF_HOME.txt next to the bootstrap DLL and under game dir.
            try
            {
                File.WriteAllText(Path.Combine(home, "TIMF_HOME.txt"), home, Encoding.UTF8);
                if (!string.IsNullOrEmpty(gameDir) && Directory.Exists(gameDir))
                    File.WriteAllText(Path.Combine(gameDir, "TIMF_HOME.txt"), home, Encoding.UTF8);
            }
            catch { /* ignore */ }
        }

        private static void EnsureCorePresent(string home, string logPath)
        {
            var core = Path.Combine(home, "TIMF.Core.dll");
            var abs = Path.Combine(home, "TIMF.Abstractions.dll");
            if (!File.Exists(core) || !File.Exists(abs))
            {
                Log(logPath, "Warning: TIMF.Core.dll / TIMF.Abstractions.dll missing in home. Build and copy them first.");
                Console.WriteLine("Warning: managed framework DLLs missing in " + home);
            }
        }

        private static void Log(string path, string message)
        {
            var line = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}", DateTime.Now, message);
            try { File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8); } catch { /* ignore */ }
            Console.WriteLine(message);
        }
    }
}
