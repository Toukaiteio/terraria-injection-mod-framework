using System;
using System.IO;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

// Usage: TmodInspect <tmod> <innerFileName> <typeSimpleName> <methodName>
// Extracts an assembly from a .tmod and prints the exception-handling regions of a method,
// so we can tell whether the *shipped* compiled method is guarded (matches source) or stale.
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 4)
        {
            Console.WriteLine("args: <tmod> <innerFileName> <typeSimpleName> <methodName>");
            return 2;
        }

        var tmodPath = args[0];
        var innerName = args[1];
        var typeName = args[2];
        var methodName = args[3];

        byte[] dll = ExtractFile(tmodPath, innerName);
        if (dll == null)
        {
            Console.WriteLine("inner file not found: " + innerName);
            return 3;
        }
        Console.WriteLine($"extracted {innerName}: {dll.Length} bytes");

        using var ms = new MemoryStream(dll);
        using var pe = new PEReader(ms);
        var md = pe.GetMetadataReader();

        foreach (var th in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(th);
            if (md.GetString(td.Name) != typeName)
                continue;
            var ns = md.GetString(td.Namespace);
            foreach (var mh in td.GetMethods())
            {
                var mdef = md.GetMethodDefinition(mh);
                if (md.GetString(mdef.Name) != methodName)
                    continue;

                Console.WriteLine($"found {ns}.{typeName}.{methodName}");
                int rva = mdef.RelativeVirtualAddress;
                if (rva == 0)
                {
                    Console.WriteLine("  (no body)");
                    continue;
                }
                var body = pe.GetMethodBody(rva);
                Console.WriteLine($"  IL size = {body.GetILBytes().Length} bytes");
                var regions = body.ExceptionRegions;
                Console.WriteLine($"  exception-handling regions = {regions.Length}");
                foreach (var r in regions)
                {
                    Console.WriteLine($"    {r.Kind,-8} try[{r.TryOffset:X4}+{r.TryLength:X3}] handler[{r.HandlerOffset:X4}+{r.HandlerLength:X3}]");
                }
            }
        }
        return 0;
    }

    private static byte[] ExtractFile(string tmodPath, string wanted)
    {
        var raw = File.ReadAllBytes(tmodPath);
        if (raw.Length < 4 || Encoding.ASCII.GetString(raw, 0, 4) != "TMOD")
        {
            Console.WriteLine("(raw assembly, not a .tmod)");
            return raw;
        }

        using var fs = File.OpenRead(tmodPath);
        using var r = new BinaryReader(fs);
        r.ReadBytes(4); // TMOD magic

        var loaderVersion = r.ReadString();
        r.ReadBytes(20);   // hash
        r.ReadBytes(256);  // signature
        r.ReadInt32();     // data length

        var name = r.ReadString();
        var version = r.ReadString();
        int count = r.ReadInt32();
        Console.WriteLine($"tmod: {name} v{version} (built with {loaderVersion}), {count} files");

        var names = new string[count];
        var lens = new int[count];
        var clens = new int[count];
        for (int i = 0; i < count; i++)
        {
            names[i] = r.ReadString();
            lens[i] = r.ReadInt32();
            clens[i] = r.ReadInt32();
        }

        byte[] result = null;
        for (int i = 0; i < count; i++)
        {
            var blob = r.ReadBytes(clens[i]);
            if (!string.Equals(names[i], wanted, StringComparison.OrdinalIgnoreCase))
                continue;

            if (clens[i] != lens[i])
            {
                using var comp = new MemoryStream(blob);
                using var ds = new DeflateStream(comp, CompressionMode.Decompress);
                using var outMs = new MemoryStream();
                ds.CopyTo(outMs);
                result = outMs.ToArray();
            }
            else
            {
                result = blob;
            }
        }
        return result;
    }
}
