using System;
using System.IO;

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
                    throw new InvalidOperationException("CreateRemoteThread failed: " + Native.GetLastErrorMessage());

                try
                {
                    Native.WaitForSingleObject(thread, 15000);
                    if (!Native.GetExitCodeThread(thread, out var exit) || exit == 0)
                        throw new InvalidOperationException(
                            "LoadLibraryW returned NULL in remote process. " +
                            "Check that TIMF.Bootstrap.dll is 32-bit and all dependent native DLLs are available. " +
                            "Win32 last error may be stale; inspect logs under TIMF home.");
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
    }
}
