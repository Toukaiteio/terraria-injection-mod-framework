using System;
using TIMF.Core.Security;

namespace TIMF.SecurityAuditSmoke
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                Console.Error.WriteLine("Pass one or more mod assembly paths.");
                return 2;
            }

            var expectReject = args[0] == "--expect-reject";
            var start = expectReject ? 1 : 0;
            if (start == args.Length) return 2;
            var failed = false;
            for (var i = start; i < args.Length; i++)
            {
                var path = args[i];
                var findings = AssemblySafetyScanner.ScanModPackage(path);
                if (findings.Count == 0)
                {
                    Console.WriteLine("PASS " + path);
                    if (expectReject) failed = true;
                    continue;
                }

                if (!expectReject) failed = true;
                Console.WriteLine("REJECT " + path);
                foreach (var finding in findings)
                    Console.WriteLine("  " + finding);
            }
            return failed ? 1 : 0;
        }
    }
}
