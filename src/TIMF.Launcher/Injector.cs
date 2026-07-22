using System;
using System.IO;
using System.Text;

namespace TIMF.Launcher
{
    internal static class Injector
    {
        /// <summary>
        /// Inject a native DLL into the target process via LoadLibraryW remote thread.
        /// Returns true if LoadLibrary returned a non-null module handle.
        /// </summary>
        public static bool Inject(IntPtr processHandle, string dllPath)
        {
            if (!File.Exists(dllPath))
                throw new FileNotFoundException("Bootstrap DLL not found", dllPath);

            var fullPath = Path.GetFullPath(dllPath);
            var preflight = PreflightBootstrap(fullPath);
            if (preflight != null)
                throw new InvalidOperationException(preflight);

            var bytes = Native.EncodePath(fullPath);
            var size = (UIntPtr)bytes.Length;

            var remote = Native.VirtualAllocEx(
                processHandle,
                IntPtr.Zero,
                size,
                Native.MEM_COMMIT | Native.MEM_RESERVE,
                Native.PAGE_READWRITE);
            if (remote == IntPtr.Zero)
                throw new InvalidOperationException("VirtualAllocEx failed: " + Native.GetLastErrorMessage());

            try
            {
                if (!Native.WriteProcessMemory(processHandle, remote, bytes, size, out _))
                    throw new InvalidOperationException("WriteProcessMemory failed: " + Native.GetLastErrorMessage());

                var kernel32 = Native.GetModuleHandle("kernel32.dll");
                if (kernel32 == IntPtr.Zero)
                    throw new InvalidOperationException("GetModuleHandle(kernel32) failed");

                // LoadLibraryW is at the same address in every process (ASLR-shared system DLL on Windows for kernel32).
                var loadLibrary = Native.GetProcAddress(kernel32, "LoadLibraryW");
                if (loadLibrary == IntPtr.Zero)
                    throw new InvalidOperationException("GetProcAddress(LoadLibraryW) failed");

                var thread = Native.CreateRemoteThread(
                    processHandle,
                    IntPtr.Zero,
                    UIntPtr.Zero,
                    loadLibrary,
                    remote,
                    0,
                    out _);
                if (thread == IntPtr.Zero)
                    throw new InvalidOperationException("CreateRemoteThread failed: " + Native.GetLastErrorMessage()
                        + Environment.NewLine
                        + "Common causes: antivirus / Controlled Folder Access blocking remote threads; "
                        + "or the game process already exited.");

                try
                {
                    var wait = Native.WaitForSingleObject(thread, 15000);
                    if (wait != 0) // WAIT_OBJECT_0
                    {
                        throw new InvalidOperationException(
                            "Remote LoadLibraryW thread did not finish in time (WaitForSingleObject=" + wait + "). "
                            + "Security software may be blocking DLL injection.");
                    }

                    if (!Native.GetExitCodeThread(thread, out var exit) || exit == 0)
                    {
                        throw new InvalidOperationException(BuildLoadLibraryFailureMessage(fullPath));
                    }

                    return true;
                }
                finally
                {
                    Native.CloseHandle(thread);
                }
            }
            finally
            {
                Native.VirtualFreeEx(processHandle, remote, UIntPtr.Zero, Native.MEM_RELEASE);
            }
        }

        /// <summary>
        /// Returns null if OK; otherwise a user-facing reason string.
        /// </summary>
        public static string PreflightBootstrap(string fullPath)
        {
            try
            {
                var fi = new FileInfo(fullPath);
                if (!fi.Exists)
                    return "TIMF.Bootstrap.dll not found: " + fullPath;
                if (fi.Length < 1024)
                    return "TIMF.Bootstrap.dll is suspiciously small (" + fi.Length + " bytes). Re-download/rebuild the package.";

                ushort machine;
                string detail;
                var kind = PeMachine.Probe(fullPath, out machine, out detail);
                if (kind == PeMachine.Kind.Pe32Plus)
                {
                    return "TIMF.Bootstrap.dll is 64-bit (PE32+), but Terraria 1.4.x is 32-bit."
                           + Environment.NewLine
                           + "Rebuild Bootstrap with MinGW i686 (-m32), or re-download the win-x86 package.";
                }

                if (kind != PeMachine.Kind.Pe32)
                {
                    return "TIMF.Bootstrap.dll is not a valid 32-bit PE image: "
                           + PeMachine.Describe(kind, machine, detail)
                           + Environment.NewLine
                           + "Path: " + fullPath;
                }

                // LoadLibraryW needs an absolute path; very long paths can fail without \\?\ support.
                if (fullPath.Length >= 240)
                {
                    return "Bootstrap path is very long (" + fullPath.Length + " chars). "
                           + "Move TIMF to a shorter path (e.g. C:\\TIMF) and retry.";
                }

                return null;
            }
            catch (Exception ex)
            {
                return "Failed to inspect TIMF.Bootstrap.dll: " + ex.Message;
            }
        }

        private static string BuildLoadLibraryFailureMessage(string fullPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("LoadLibraryW failed inside Terraria (module handle = 0).");
            sb.AppendLine("DLL: " + fullPath);
            sb.AppendLine();
            sb.AppendLine("Most common causes (check in order):");
            sb.AppendLine("  1) Bootstrap depends on a MinGW DLL not visible to Terraria (e.g. libwinpthread-1.dll).");
            sb.AppendLine("     → Use a package where Bootstrap is linked with -static-libgcc/-lpthread,");
            sb.AppendLine("       or copy libwinpthread-1.dll next to Terraria.exe / TIMF.Bootstrap.dll.");
            sb.AppendLine("  2) Antivirus / Windows Defender / 360 / Huorong blocked the inject.");
            sb.AppendLine("     → Add TIMF folder + Terraria.exe to exclusions, or temporarily disable real-time protection.");
            sb.AppendLine("  3) Package is incomplete or Bootstrap is the wrong architecture.");
            sb.AppendLine("     → TIMF.Bootstrap.dll must be 32-bit (PE32). Re-download the official win-x86 zip.");
            sb.AppendLine("  4) Folder is under Downloads / Desktop and is being sandboxed.");
            sb.AppendLine("     → Move the whole TIMF folder to a simple path like C:\\TIMF and run from there.");
            sb.AppendLine("  5) Controlled Folder Access / ransomware protection blocking remote LoadLibrary.");
            sb.AppendLine();
            sb.AppendLine("Also check: %TEMP%\\timf-bootstrap.log (only written if LoadLibrary succeeded).");
            sb.AppendLine("If that log is missing, the DLL never loaded into Terraria.");
            return sb.ToString().TrimEnd();
        }
    }
}
