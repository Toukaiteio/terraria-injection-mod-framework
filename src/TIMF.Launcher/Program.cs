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
            // Double-clicking the .exe opens a console that closes on exit — always pause
            // on failure so users can read the error. Success still exits immediately.
            // Opt out with --no-pause (scripts / CI).
            var noPause = HasFlag(args, "--no-pause") || HasFlag(args, "-no-pause");
            var exitCode = 0;
            try
            {
                exitCode = Run(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("========== TIMF launcher error ==========");
                Console.Error.WriteLine(ex);
                Console.Error.WriteLine("=========================================");
                exitCode = 99;
            }

            if (exitCode != 0 && !noPause)
                PauseBeforeExit(exitCode);

            return exitCode;
        }

        private static int Run(string[] args)
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
            var wantServer = HasFlag(args, "--server") || HasFlag(args, "-server");
            var gamePath = ResolveGamePath(configPath, args, wantServer);
            if (string.IsNullOrEmpty(gamePath) || !File.Exists(gamePath))
            {
                Console.Error.WriteLine(wantServer
                    ? "TerrariaServer.exe not found."
                    : "Terraria.exe not found.");
                Console.Error.WriteLine("Pass path as argument, use --server, or set gamePath/serverPath in " + configPath);
                Log(logPath, "Game executable not found (server=" + wantServer + ")");
                return 1;
            }

            SaveConfig(configPath, gamePath, wantServer);
            Log(logPath, (wantServer ? "Server: " : "Game: ") + gamePath);

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

            var peCheck = Injector.PreflightBootstrap(Path.GetFullPath(bootstrap));
            if (peCheck != null)
            {
                Console.Error.WriteLine(peCheck);
                Log(logPath, "Bootstrap preflight failed: " + peCheck.Replace(Environment.NewLine, " | "));
                return 2;
            }

            try
            {
                ushort machine;
                string peDetail;
                var peKind = PeMachine.Probe(bootstrap, out machine, out peDetail);
                Log(logPath, "Bootstrap PE: " + PeMachine.Describe(peKind, machine, peDetail)
                             + " size=" + new FileInfo(bootstrap).Length
                             + " path=" + bootstrap);
            }
            catch (Exception ex)
            {
                Log(logPath, "Bootstrap PE probe: " + ex.Message);
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
            Console.WriteLine("Started " + Path.GetFileName(gamePath) + " pid=" + pi.dwProcessId);

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
                Console.Error.WriteLine();
                Console.Error.WriteLine("========== TIMF injection failed ==========");
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine("===========================================");
                Console.Error.WriteLine("Home: " + home);
                Console.Error.WriteLine("Bootstrap: " + bootstrap);
                Console.Error.WriteLine("Game: " + gamePath);
                Console.Error.WriteLine("Log: " + logPath);
                Console.Error.WriteLine();
                Console.Error.WriteLine("Quick fixes:");
                Console.Error.WriteLine("  • Move TIMF out of Downloads (e.g. C:\\TIMF) and re-run.");
                Console.Error.WriteLine("  • Allow TIMF + Terraria in antivirus / Defender exclusions.");
                Console.Error.WriteLine("  • Confirm TIMF.Bootstrap.dll is the 32-bit build from the win-x86 package.");
                Console.Error.WriteLine("  • Do not mix 64-bit native DLLs into the TIMF folder.");
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

        /// <summary>
        /// Keep the console open after a failure so double-click users can read the message.
        /// Skipped when stdin is redirected (piped / non-interactive) or console is unavailable.
        /// </summary>
        private static void PauseBeforeExit(int exitCode)
        {
            try
            {
                // No console attached (e.g. some hosts) — nothing to wait for.
                if (Console.IsInputRedirected)
                    return;

                Console.Error.WriteLine();
                Console.Error.WriteLine("Exit code: " + exitCode);
                Console.Error.WriteLine("Press any key to close this window...");
                try
                {
                    // Drain buffered keys so a leftover Enter from launching doesn't skip the pause.
                    while (Console.KeyAvailable)
                        Console.ReadKey(intercept: true);
                }
                catch { /* ignore */ }

                Console.ReadKey(intercept: true);
            }
            catch
            {
                // Last resort: short sleep so a flash of text is slightly more readable.
                try { System.Threading.Thread.Sleep(8000); } catch { /* ignore */ }
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

        private static bool HasFlag(string[] args, string flag)
        {
            if (args == null) return false;
            foreach (var a in args)
            {
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string FirstExistingPathArg(string[] args)
        {
            if (args == null) return null;
            foreach (var a in args)
            {
                if (string.IsNullOrWhiteSpace(a) || a.StartsWith("-", StringComparison.Ordinal))
                    continue;
                if (File.Exists(a))
                    return Path.GetFullPath(a);
            }
            return null;
        }

        private static string ResolveGamePath(string configPath, string[] args, bool wantServer)
        {
            var fromArg = FirstExistingPathArg(args);
            if (!string.IsNullOrEmpty(fromArg))
                return fromArg;

            if (File.Exists(configPath))
            {
                try
                {
                    var text = File.ReadAllText(configPath);
                    if (wantServer)
                    {
                        var sp = ExtractJsonString(text, "serverPath");
                        if (!string.IsNullOrEmpty(sp) && File.Exists(sp))
                            return sp;
                    }
                    var path = ExtractJsonString(text, "gamePath");
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        if (wantServer)
                        {
                            var dir = Path.GetDirectoryName(path);
                            var server = Path.Combine(dir ?? "", "TerrariaServer.exe");
                            if (File.Exists(server))
                                return server;
                        }
                        else
                        {
                            return path;
                        }
                    }
                }
                catch { /* ignore */ }
            }

            var exeName = wantServer ? "TerrariaServer.exe" : "Terraria.exe";
            var roots = new[]
            {
                @"E:\SteamLibrary\steamapps\common\Terraria",
                @"C:\Program Files (x86)\Steam\steamapps\common\Terraria",
                @"D:\SteamLibrary\steamapps\common\Terraria",
                @"D:\Program Files (x86)\Steam\steamapps\common\Terraria",
            };
            foreach (var root in roots)
            {
                var c = Path.Combine(root, exeName);
                if (File.Exists(c))
                    return c;
            }

            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    var steamPath = k?.GetValue("SteamPath") as string;
                    if (!string.IsNullOrEmpty(steamPath))
                    {
                        var p = Path.Combine(steamPath.Replace('/', '\\'), "steamapps", "common", "Terraria", exeName);
                        if (File.Exists(p))
                            return p;
                    }
                }
            }
            catch { /* ignore */ }

            return null;
        }

        private static void SaveConfig(string configPath, string gamePath, bool isServer)
        {
            // Preserve the other path key when possible.
            string otherKey = isServer ? "gamePath" : "serverPath";
            string otherVal = null;
            if (File.Exists(configPath))
            {
                try { otherVal = ExtractJsonString(File.ReadAllText(configPath), otherKey); }
                catch { /* ignore */ }
            }

            var primaryKey = isServer ? "serverPath" : "gamePath";
            var sb = new StringBuilder();
            sb.Append("{\n  \"").Append(primaryKey).Append("\": \"")
              .Append(gamePath.Replace("\\", "\\\\")).Append("\"");
            if (!string.IsNullOrEmpty(otherVal))
            {
                sb.Append(",\n  \"").Append(otherKey).Append("\": \"")
                  .Append(otherVal.Replace("\\", "\\\\")).Append("\"");
            }
            else if (isServer)
            {
                var client = Path.Combine(Path.GetDirectoryName(gamePath) ?? "", "Terraria.exe");
                if (File.Exists(client))
                {
                    sb.Append(",\n  \"gamePath\": \"")
                      .Append(client.Replace("\\", "\\\\")).Append("\"");
                }
            }
            sb.Append("\n}\n");
            File.WriteAllText(configPath, sb.ToString(), Encoding.UTF8);
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
