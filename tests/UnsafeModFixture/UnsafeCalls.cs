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

        // Forbidden call hidden inside a lambda closure (a compiler-generated nested type). Proves
        // the verifier descends into nested/closure types, not just top-level ones.
        public static string ReadViaLambda(string path)
        {
            System.Func<string> read = () => File.ReadAllText(path);
            return read();
        }

        // Constructs an object without running its constructor, bypassing the API audit. Caught by
        // the FormatterServices.GetUninitializedObject rule.
        public static object Uninitialized() =>
            System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(UnsafeCalls));
    }
}
