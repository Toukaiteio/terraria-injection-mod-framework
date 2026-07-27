using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace TIMF.Core.Security
{
    internal sealed class AssemblySafetyFinding
    {
        public string AssemblyPath;
        public string Method;
        public string Evidence;
        public override string ToString() => Path.GetFileName(AssemblyPath) + ": " + Method + " -> " + Evidence;
    }

    internal static class AssemblySafetyScanner
    {
        private static readonly OpCode[] OneByte = new OpCode[256];
        private static readonly OpCode[] TwoByte = new OpCode[256];

        static AssemblySafetyScanner()
        {
            foreach (var f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!(f.GetValue(null) is OpCode op)) continue;
                var value = unchecked((ushort)op.Value);
                if (value < 0x100) OneByte[value] = op;
                else if ((value & 0xff00) == 0xfe00) TwoByte[value & 0xff] = op;
            }
        }

        public static List<AssemblySafetyFinding> ScanModPackage(string mainAssemblyPath)
        {
            var findings = new List<AssemblySafetyFinding>();
            var dir = Path.GetDirectoryName(mainAssemblyPath);
            var files = string.IsNullOrEmpty(dir)
                ? new[] { mainAssemblyPath }
                : Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly);

            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                if (IsFrameworkDependency(name))
                {
                    findings.Add(New(file, "<package>",
                        "framework dependency must not be bundled or shadowed by a mod"));
                    continue;
                }
                try
                {
                    var asm = Assembly.LoadFrom(file);
                    if (!string.Equals(Path.GetFullPath(asm.Location), Path.GetFullPath(file),
                        StringComparison.OrdinalIgnoreCase))
                    {
                        findings.Add(New(file, "<audit>",
                            "CLR resolved a different assembly path; package identity collision"));
                        continue;
                    }
                    ScanAssembly(file, asm, findings);
                }
                catch (BadImageFormatException)
                {
                    findings.Add(New(file, "<module>", "native or invalid DLL bundled with mod"));
                }
                catch (Exception ex)
                {
                    findings.Add(New(file, "<audit>", "assembly could not be audited: " + ex.GetType().Name));
                }
            }
            return findings;
        }

        private static void ScanAssembly(string path, Assembly asm, List<AssemblySafetyFinding> findings)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(x => x != null).ToArray();
                findings.Add(New(path, "<types>", "not all types could be resolved; audit is incomplete"));
            }

            foreach (var type in types)
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).Cast<MethodBase>()
                    .Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Static | BindingFlags.Instance)))
                {
                    var label = (type.FullName ?? type.Name) + "." + method.Name;
                    if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0)
                        findings.Add(New(path, label, "P/Invoke/native entry point"));
                    var impl = method.GetMethodImplementationFlags();
                    if ((impl & (MethodImplAttributes.Native | MethodImplAttributes.Unmanaged |
                                 MethodImplAttributes.InternalCall)) != 0)
                        findings.Add(New(path, label, "native/internal-call method implementation"));

                    MethodBody body;
                    try { body = method.GetMethodBody(); }
                    catch (Exception ex)
                    {
                        findings.Add(New(path, label, "method body could not be audited: " + ex.GetType().Name));
                        continue;
                    }
                    if (body == null) continue;
                    ScanBody(path, method, label, body, findings);
                }
            }
        }

        private static void ScanBody(string path, MethodBase owner, string label, MethodBody body,
            List<AssemblySafetyFinding> findings)
        {
            var il = body.GetILAsByteArray();
            if (il == null) return;
            var pos = 0;
            while (pos < il.Length)
            {
                OpCode op;
                var first = il[pos++];
                if (first == 0xfe)
                {
                    if (pos >= il.Length) { findings.Add(New(path, label, "truncated IL opcode")); return; }
                    op = TwoByte[il[pos++]];
                }
                else op = OneByte[first];

                if (op.Size == 0) { findings.Add(New(path, label, "unknown IL opcode")); return; }
                if (op == OpCodes.Calli)
                    findings.Add(New(path, label, "indirect calli instruction can bypass managed API audit"));
                try
                {
                    switch (op.OperandType)
                    {
                        case OperandType.InlineMethod:
                        case OperandType.InlineField:
                        case OperandType.InlineType:
                        case OperandType.InlineTok:
                        {
                            var token = ReadI4(il, ref pos);
                            var member = owner.Module.ResolveMember(token,
                                owner.DeclaringType?.GetGenericArguments(),
                                owner.IsGenericMethod ? owner.GetGenericArguments() : null);
                            string reason;
                            if (IsForbiddenMember(member, out reason))
                                findings.Add(New(path, label, reason + " [" + Describe(member) + "]"));
                            break;
                        }
                        case OperandType.InlineString:
                        {
                            var token = ReadI4(il, ref pos);
                            var value = owner.Module.ResolveString(token);
                            if (LooksLikeForbiddenReflectionName(value))
                                findings.Add(New(path, label, "suspicious sensitive API name loaded as a string"));
                            break;
                        }
                        case OperandType.InlineSwitch:
                            var count = ReadI4(il, ref pos);
                            if (count < 0 || count > (il.Length - pos) / 4) throw new InvalidDataException();
                            pos += count * 4;
                            break;
                        default:
                            pos += OperandSize(op.OperandType);
                            break;
                    }
                    if (pos > il.Length) throw new InvalidDataException();
                }
                catch
                {
                    findings.Add(New(path, label, "IL metadata could not be resolved safely"));
                    return;
                }
            }
        }

        private static bool IsForbiddenMember(MemberInfo member, out string reason)
        {
            reason = null;
            var type = member as Type ?? member?.DeclaringType;
            var name = type?.FullName ?? "";
            var memberName = member?.Name ?? "";

            if (name.StartsWith("System.IO.", StringComparison.Ordinal) &&
                name != "System.IO.Path" && name != "System.IO.MemoryStream" &&
                name != "System.IO.StringReader" && name != "System.IO.StringWriter" &&
                name != "System.IO.BinaryReader" && name != "System.IO.BinaryWriter")
                reason = "direct file-system API bypasses IModStorage/permission proxy";
            else if (name == "System.Diagnostics.Process" || name == "System.Diagnostics.ProcessStartInfo")
                reason = "direct process execution bypasses permission proxy";
            else if (name.StartsWith("System.Net.", StringComparison.Ordinal) ||
                     name.StartsWith("System.Net.Sockets.", StringComparison.Ordinal))
                reason = "direct network access is not an approved TIMF capability";
            else if (name.StartsWith("Microsoft.Win32.Registry", StringComparison.Ordinal))
                reason = "direct registry access is forbidden";
            else if (name == "System.Environment" &&
                     (memberName.StartsWith("GetEnvironmentVariable", StringComparison.Ordinal) ||
                      memberName.StartsWith("SetEnvironmentVariable", StringComparison.Ordinal)))
                reason = "direct environment-variable access is not an approved TIMF capability";
            else if (name.StartsWith("System.CodeDom.Compiler.", StringComparison.Ordinal) ||
                     name == "Microsoft.CSharp.CSharpCodeProvider")
                reason = "runtime compilation can bypass package and API audit";
            else if (name.StartsWith("System.Reflection.Emit.", StringComparison.Ordinal))
                reason = "runtime code generation can bypass static safety audit";
            else if (name == "System.Reflection.Assembly" && memberName.StartsWith("Load", StringComparison.Ordinal))
                reason = "dynamic assembly access can bypass package audit";
            else if (name == "System.AppDomain" && memberName.StartsWith("Load", StringComparison.Ordinal))
                reason = "dynamic assembly loading can bypass package audit";
            else if ((name == "System.Reflection.MethodBase" || name == "System.Reflection.MethodInfo" ||
                      name == "System.Reflection.ConstructorInfo") && memberName == "Invoke")
                reason = "reflection invocation can bypass sensitive API audit";
            else if ((name == "System.Reflection.FieldInfo" || name == "System.Reflection.PropertyInfo") &&
                     (memberName == "GetValue" || memberName == "SetValue"))
                reason = "direct reflection member access can tamper with framework state";
            else if (name == "System.Type" && memberName == "InvokeMember")
                reason = "late-bound reflection can bypass sensitive API audit";
            else if (name == "System.Delegate" && memberName == "DynamicInvoke")
                reason = "dynamic delegate invocation can bypass sensitive API audit";
            else if ((name == "System.Delegate" || name == "System.Reflection.MethodInfo") &&
                     memberName == "CreateDelegate")
                reason = "runtime delegate creation can bypass sensitive API audit";
            else if (name.StartsWith("System.Linq.Expressions.", StringComparison.Ordinal) && memberName == "Compile")
                reason = "compiled expression can bypass sensitive API audit";
            else if (name == "System.RuntimeMethodHandle" && memberName == "GetFunctionPointer")
                reason = "raw method pointers can bypass managed API audit";
            else if (name == "System.Activator" && memberName.StartsWith("CreateInstance", StringComparison.Ordinal))
                reason = "dynamic activation can bypass sensitive API audit";
            else if (name.StartsWith("System.Runtime.InteropServices.Marshal", StringComparison.Ordinal))
                reason = "unmanaged interop is forbidden for in-process mods";
            else if (name.StartsWith("HarmonyLib.", StringComparison.Ordinal) && name != "HarmonyLib.AccessTools")
                reason = "direct Harmony patching bypasses the per-mod patch broker";
            else if (name == "TIMF.Abstractions.IServiceRegistry" && memberName == "Register")
                reason = "raw service registration can replace framework security/UI services; use ServicePublisher";
            else if (name.StartsWith("Terraria.", StringComparison.Ordinal) &&
                     HasSensitiveName(memberName))
                reason = "sensitive Terraria API could proxy host file/process access";

            return reason != null;
        }

        private static bool LooksLikeForbiddenReflectionName(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            return value.IndexOf("System.IO.File", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("System.IO.Directory", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("System.Diagnostics.Process", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("System.Reflection.Emit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("System.Runtime.InteropServices", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasSensitiveName(string value)
        {
            value = (value ?? "").ToLowerInvariant();
            return value.Contains("file") || value.Contains("directory") || value.Contains("save") ||
                   value.Contains("load") || value.Contains("process") || value.Contains("shell") ||
                   value.Contains("registry");
        }

        private static int OperandSize(OperandType type)
        {
            switch (type)
            {
                case OperandType.InlineNone: return 0;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar: return 1;
                case OperandType.InlineVar: return 2;
                case OperandType.InlineBrTarget:
                case OperandType.InlineI:
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.ShortInlineR: return 4;
                case OperandType.InlineI8:
                case OperandType.InlineR: return 8;
                default: throw new InvalidDataException("Unsupported IL operand type: " + type);
            }
        }

        private static int ReadI4(byte[] il, ref int pos)
        {
            if (pos + 4 > il.Length) throw new InvalidDataException();
            var value = BitConverter.ToInt32(il, pos);
            pos += 4;
            return value;
        }

        private static string Describe(MemberInfo member) =>
            (member?.DeclaringType?.FullName ?? (member as Type)?.FullName ?? "?") + "." + (member?.Name ?? "?");
        private static AssemblySafetyFinding New(string path, string method, string evidence) =>
            new AssemblySafetyFinding { AssemblyPath = path, Method = method, Evidence = evidence };
        private static bool IsFrameworkDependency(string name) =>
            name.Equals("TIMF.Abstractions.dll", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("TIMF.Content.dll", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("0Harmony.dll", StringComparison.OrdinalIgnoreCase);
    }
}
