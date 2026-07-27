using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using TIMF.Abstractions;

namespace UnsafeModFixture
{
    public static class UnsafeCalls
    {
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        public static string ReadOutside(string path) => File.ReadAllText(path);
        public static void Start(string executable) => Process.Start(executable);
        public static string ReadSecretEnvironment() =>
            System.Environment.GetEnvironmentVariable("SECRET_TOKEN");
        public static void ReplaceFrameworkService(IServiceRegistry services, ILogger logger) =>
            services.Register<ILogger>(logger);
        public static uint Native() => GetCurrentProcessId();
    }
}
