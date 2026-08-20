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
                Console.Error.WriteLine(SafeExceptionText(ex));
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
            Log(logPath, "TIMF home initialized");

            var configPath = Path.Combine(home, "timf.json");
            var wantServer = HasFlag(args, "--server") || HasFlag(args, "-server");
            var gamePath = ResolveGamePath(configPath, args, wantServer);
            if (string.IsNullOrEmpty(gamePath) || !File.Exists(gamePath))
            {
                Console.Error.WriteLine(wantServer
                    ? "TerrariaServer.exe not found."
                    : "Terraria.exe not found.");
                Console.Error.WriteLine("Pass the executable path as an argument, use --server, or configure gamePath/serverPath in timf.json.");
                Log(logPath, "Game executable not found (server=" + wantServer + ")");
                return 1;
            }

            SaveConfig(configPath, gamePath, wantServer);
            Log(logPath, (wantServer ? "Server executable selected: " : "Game executable selected: ") + Path.GetFileName(gamePath));

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
                Console.Error.WriteLine("TIMF.Bootstrap.dll was not found next to the launcher.");
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
                             + " size=" + new FileInfo(bootstrap).Length);
            }
            catch (Exception ex)
            {
                Log(logPath, "Bootstrap PE probe: " + ex.GetType().Name);
            }

            EnsureCorePresent(home, logPath);

            var workDir = Path.GetDirectoryName(gamePath);
            var si = new Native.STARTUPINFO { cb = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.STARTUPINFO)) };

            // Pass TIMF_HOME through the inherited environment. The bootstrap falls back to
            // its own module directory, so no absolute-path sidecar file is needed.
            Environment.SetEnvironmentVariable("TIMF_HOME", home);

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

                Log(logPath, "Injecting TIMF.Bootstrap.dll");
                Injector.Inject(pi.hProcess, bootstrap);
                Log(logPath, "Injection OK");
                Console.WriteLine("Injected TIMF.Bootstrap.dll");
            }
            catch (Exception ex)
            {
                Log(logPath, "Injection failed: " + SafeExceptionText(ex));
                Console.Error.WriteLine();
                Console.Error.WriteLine("========== TIMF injection failed ==========");
                Console.Error.WriteLine(SafeExceptionText(ex));
                Console.Error.WriteLine("===========================================");
                Console.Error.WriteLine("Home: launcher directory");
                Console.Error.WriteLine("Bootstrap: TIMF.Bootstrap.dll");
                Console.Error.WriteLine("Game: " + Path.GetFileName(gamePath));
                Console.Error.WriteLine("Log: logs\\launcher.log");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Quick fixes:");
                Console.Error.WriteLine("  • Move TIMF to a short, writable installation directory and re-run.");
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

            Console.WriteLine("TIMF home: launcher directory");
            Console.WriteLine("Logs: logs\\");
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

            var fromEnvironment = Environment.GetEnvironmentVariable(
                wantServer ? "TIMF_TERRARIA_SERVER" : "TIMF_TERRARIA");
            if (!string.IsNullOrWhiteSpace(fromEnvironment) && File.Exists(fromEnvironment))
                return Path.GetFullPath(fromEnvironment);

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

            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    var steamPath = k?.GetValue("SteamPath") as string;
                    if (!string.IsNullOrEmpty(steamPath))
                    {
                        var exeName = wantServer ? "TerrariaServer.exe" : "Terraria.exe";
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

        private static void EnsureCorePresent(string home, string logPath)
        {
            var core = Path.Combine(home, "TIMF.Core.dll");
            var abs = Path.Combine(home, "TIMF.Abstractions.dll");
            if (!File.Exists(core) || !File.Exists(abs))
            {
                Log(logPath, "Warning: TIMF.Core.dll / TIMF.Abstractions.dll missing in home. Build and copy them first.");
                Console.WriteLine("Warning: managed framework DLLs are missing next to the launcher.");
            }
        }

        private static void Log(string path, string message)
        {
            var line = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}", DateTime.Now, message);
            try { File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8); } catch { /* ignore */ }
            Console.WriteLine(message);
        }

        private static string SafeExceptionText(Exception ex)
        {
            return ex == null ? "Unknown error" : ex.GetType().Name;
        }
    }
}
