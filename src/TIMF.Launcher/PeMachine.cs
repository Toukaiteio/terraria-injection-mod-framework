using System;
using System.IO;

namespace TIMF.Launcher
{
    /// <summary>Lightweight PE header probe (no full PE parser).</summary>
    internal static class PeMachine
    {
        public const ushort IMAGE_FILE_MACHINE_I386 = 0x014c;
        public const ushort IMAGE_FILE_MACHINE_AMD64 = 0x8664;

        // COFF FileHeader is 20 bytes after PE signature (4 bytes).
        public const int PeSignatureSize = 4;
        public const int CoffFileHeaderSize = 20;
        // OptionalHeader starts at peOff + 24; first field is Magic (PE32=0x10B, PE32+=0x20B).
        public const int OptionalHeaderMagicOffset = PeSignatureSize + CoffFileHeaderSize; // 24

        public enum Kind
        {
            NotPe,
            Pe32,
            Pe32Plus,
            Unknown,
        }

        public static Kind Probe(string path, out ushort machine, out string detail)
        {
            machine = 0;
            detail = "";
            try
            {
                using (var fs = File.OpenRead(path))
                using (var br = new BinaryReader(fs))
                {
                    if (fs.Length < 0x40)
                    {
                        detail = "file too small";
                        return Kind.NotPe;
                    }

                    if (br.ReadUInt16() != 0x5A4D) // MZ
                    {
                        detail = "missing MZ";
                        return Kind.NotPe;
                    }

                    fs.Seek(0x3C, SeekOrigin.Begin);
                    var peOff = br.ReadInt32();
                    if (peOff <= 0 || peOff + OptionalHeaderMagicOffset + 2 >= fs.Length)
                    {
                        detail = "bad e_lfanew";
                        return Kind.NotPe;
                    }

                    fs.Seek(peOff, SeekOrigin.Begin);
                    if (br.ReadUInt32() != 0x00004550) // PE\0\0
                    {
                        detail = "missing PE signature";
                        return Kind.NotPe;
                    }

                    // IMAGE_FILE_HEADER
                    machine = br.ReadUInt16();                 // Machine
                    /* NumberOfSections */ br.ReadUInt16();
                    /* TimeDateStamp */ br.ReadUInt32();
                    /* PointerToSymbolTable */ br.ReadUInt32();
                    /* NumberOfSymbols */ br.ReadUInt32();
                    var sizeOfOptionalHeader = br.ReadUInt16(); // often 0x00E0 for PE32 — NOT magic
                    /* Characteristics */ br.ReadUInt16();

                    // Optional header begins immediately after the 20-byte file header.
                    // Must read Magic from peOff+24, NOT peOff+20 (that was SizeOfOptionalHeader — bug).
                    fs.Seek(peOff + OptionalHeaderMagicOffset, SeekOrigin.Begin);
                    var magic = br.ReadUInt16(); // 0x10B = PE32, 0x20B = PE32+

                    if (machine == IMAGE_FILE_MACHINE_I386 && magic == 0x10B)
                    {
                        detail = "PE32 i386, SizeOfOptionalHeader=0x" + sizeOfOptionalHeader.ToString("X");
                        return Kind.Pe32;
                    }

                    if (machine == IMAGE_FILE_MACHINE_AMD64 && magic == 0x20B)
                    {
                        detail = "PE32+ x64, SizeOfOptionalHeader=0x" + sizeOfOptionalHeader.ToString("X");
                        return Kind.Pe32Plus;
                    }

                    // i386 machine alone is a strong signal even if magic is unexpected.
                    if (machine == IMAGE_FILE_MACHINE_I386)
                    {
                        detail = "i386 machine, optional magic=0x" + magic.ToString("X4")
                                 + " (treating as PE32)";
                        return Kind.Pe32;
                    }

                    detail = "machine=0x" + machine.ToString("X4") + " magic=0x" + magic.ToString("X4");
                    return Kind.Unknown;
                }
            }
            catch (Exception ex)
            {
                detail = ex.Message;
                return Kind.NotPe;
            }
        }

        public static string Describe(Kind k, ushort machine, string detail)
        {
            return k + " (" + detail + ", machine=0x" + machine.ToString("X4") + ")";
        }
    }
}
