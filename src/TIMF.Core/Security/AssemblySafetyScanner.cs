using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace TIMF.Core.Security
{
    internal sealed class AssemblySafetyFinding
    {
        public string AssemblyPath;
        public string Method;
        public string Evidence;
        public override string ToString() => Path.GetFileName(AssemblyPath) + ": " + Method + " -> " + Evidence;
    }

    /// <summary>
    /// Static, load-free capability verifier for untrusted mod assemblies.
    ///
    /// This is TIMF's sandbox gate. Because mods run in-process with direct access to Terraria and
    /// Harmony (isolation via AppDomain/CAS/OS is impossible without breaking that access), the only
    /// enforceable boundary is <em>verification before load</em>: an assembly must be proven free of
    /// forbidden capabilities before <see cref="System.Reflection.Assembly.LoadFrom"/> is ever called
    /// on it. The reliability of the sandbox therefore equals the completeness of this scan.
    ///
    /// The verifier reads IL and metadata as data via Mono.Cecil and never loads mod code into the
    /// process. This closes two holes that a reflection-based scanner cannot: it inspects the module
    /// initializer and module-level (&lt;Module&gt;) global methods, and it never resolves/executes any
    /// static or module constructor merely to look at a candidate.
    /// </summary>
    internal static class AssemblySafetyScanner
    {
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
                ScanFile(file, dir, findings);
            }
            return findings;
        }

        private static void ScanFile(string file, string packageDir, List<AssemblySafetyFinding> findings)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(file); }
            catch (Exception ex)
            {
                findings.Add(New(file, "<audit>", "assembly could not be read: " + ex.GetType().Name));
                return;
            }

            ModuleDefinition module = null;
            try
            {
                // Read from an in-memory copy so no file handle is held. The later trusted
                // Assembly.LoadFrom (performed only after the whole package passes) then sees an
                // unlocked file, and no type/module initializer runs during the audit.
                var parameters = new ReaderParameters
                {
                    ReadingMode = ReadingMode.Immediate,
                    InMemory = true,
                    ReadSymbols = false,
                    AssemblyResolver = new SafeResolver(packageDir),
                };
                module = ModuleDefinition.ReadModule(new MemoryStream(bytes, writable: false), parameters);
                ScanModule(file, module, findings);
            }
            catch (BadImageFormatException)
            {
                findings.Add(New(file, "<module>", "native or invalid DLL bundled with mod"));
            }
            catch (Exception ex)
            {
                // Fail closed: an assembly we cannot fully audit is treated as unsafe.
                findings.Add(New(file, "<audit>", "assembly could not be audited: " + ex.GetType().Name));
            }
            finally
            {
                module?.Dispose();
            }
        }

        private static void ScanModule(string path, ModuleDefinition module, List<AssemblySafetyFinding> findings)
        {
            // module.Types includes the special <Module> type, whose members are the module
            // initializer and any global methods — the exact surface a reflection scan misses.
            foreach (var type in AllTypes(module))
            {
                foreach (var method in type.Methods)
                {
                    var label = (type.FullName ?? type.Name) + "." + method.Name;
                    ScanMethodShape(path, method, label, findings);
                    if (method.HasBody)
                        ScanBody(path, method, label, findings);
                }
            }
        }

        private static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
        {
            foreach (var top in module.Types)
                foreach (var t in Flatten(top))
                    yield return t;
        }

        private static IEnumerable<TypeDefinition> Flatten(TypeDefinition type)
        {
            yield return type;
            foreach (var nested in type.NestedTypes)
                foreach (var t in Flatten(nested))
                    yield return t;
        }

        private static void ScanMethodShape(string path, MethodDefinition m, string label,
            List<AssemblySafetyFinding> findings)
        {
            if (m.IsPInvokeImpl || m.HasPInvokeInfo)
                findings.Add(New(path, label, "P/Invoke/native entry point"));
            if (m.IsInternalCall || m.IsNative || m.IsUnmanaged)
                findings.Add(New(path, label, "native/internal-call method implementation"));
            if (m.IsUnmanagedExport)
                findings.Add(New(path, label, "unmanaged export bypasses managed API audit"));
        }

        private static void ScanBody(string path, MethodDefinition owner, string label,
            List<AssemblySafetyFinding> findings)
        {
            foreach (var ins in owner.Body.Instructions)
            {
                if (ins.OpCode.Code == Code.Calli)
                {
                    findings.Add(New(path, label, "indirect calli instruction can bypass managed API audit"));
                    continue;
                }

                string reason;
                switch (ins.Operand)
                {
                    case MethodReference mref:
                        if (IsForbiddenMember(TypeName(mref.DeclaringType), mref.Name, out reason))
                            findings.Add(New(path, label, reason + " [" + Describe(mref) + "]"));
                        break;
                    case FieldReference fref:
                        if (IsForbiddenMember(TypeName(fref.DeclaringType), fref.Name, out reason))
                            findings.Add(New(path, label, reason + " [" + Describe(fref) + "]"));
                        break;
                    case TypeReference tref:
                        // Bare type token (ldtoken/newarr/etc.): only namespace-level bans apply.
                        if (IsForbiddenMember(TypeName(tref), "", out reason))
                            findings.Add(New(path, label, reason + " [" + TypeName(tref) + "]"));
                        break;
                    case string s:
                        if (LooksLikeForbiddenReflectionName(s))
                            findings.Add(New(path, label, "suspicious sensitive API name loaded as a string"));
                        break;
                }
            }
        }

        /// <summary>
        /// Ported rule engine. <paramref name="typeFullName"/> is the declaring type of the referenced
        /// member (or the type itself for a bare type token); <paramref name="memberName"/> is empty
        /// for a bare type token so member-specific bans do not trigger on <c>typeof(...)</c>.
        /// </summary>
        private static bool IsForbiddenMember(string typeFullName, string memberName, out string reason)
        {
            reason = null;
            var name = typeFullName ?? "";
            memberName = memberName ?? "";

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
            else if (name == "System.AppDomain" &&
                     (memberName.StartsWith("Load", StringComparison.Ordinal) ||
                      memberName == "ExecuteAssembly" ||
                      memberName == "ExecuteAssemblyByName" ||
                      memberName == "DefineDynamicAssembly" ||
                      memberName.StartsWith("CreateInstance", StringComparison.Ordinal)))
                reason = "dynamic assembly loading/activation can bypass package audit";
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
            else if (name == "System.Runtime.Serialization.FormatterServices" &&
                     memberName == "GetUninitializedObject")
                reason = "uninitialized-object creation bypasses constructors and the API audit";
            else if (name.StartsWith("System.Runtime.InteropServices.Marshal", StringComparison.Ordinal))
                reason = "unmanaged interop is forbidden for in-process mods";
            else if (name.StartsWith("HarmonyLib.", StringComparison.Ordinal) && name != "HarmonyLib.AccessTools")
                reason = "direct Harmony patching bypasses the per-mod patch broker";
            else if (name == "TIMF.Abstractions.IServiceRegistry" && memberName == "Register")
                reason = "raw service registration can replace framework security/UI services; use ServicePublisher";
            else if (name.StartsWith("Terraria.", StringComparison.Ordinal) && HasSensitiveName(memberName))
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

        // TypeReference.FullName of a generic instance carries the instantiation; the element type's
        // FullName is what the bans key on (e.g. "System.IO.File", never "...File<...>").
        private static string TypeName(TypeReference type)
        {
            if (type == null) return "";
            var spec = type as TypeSpecification;
            return spec != null ? TypeName(spec.ElementType) : (type.FullName ?? "");
        }

        private static string Describe(MemberReference member) =>
            (member is MethodReference m ? TypeName(m.DeclaringType)
                : member is FieldReference f ? TypeName(f.DeclaringType)
                : "?") + "." + (member?.Name ?? "?");

        private static AssemblySafetyFinding New(string path, string method, string evidence) =>
            new AssemblySafetyFinding { AssemblyPath = path, Method = method, Evidence = evidence };

        private static bool IsFrameworkDependency(string name) =>
            name.Equals("TIMF.Abstractions.dll", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("TIMF.Content.dll", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("0Harmony.dll", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Resolver used only so Cecil can satisfy the type system if it ever needs to; the audit
        /// itself never calls Resolve(). Failures return null instead of throwing so a missing
        /// reference can never abort (and thus falsely reject) an otherwise clean assembly.
        /// </summary>
        private sealed class SafeResolver : DefaultAssemblyResolver
        {
            public SafeResolver(string packageDir)
            {
                if (!string.IsNullOrEmpty(packageDir) && Directory.Exists(packageDir))
                    AddSearchDirectory(packageDir);
            }

            public override AssemblyDefinition Resolve(AssemblyNameReference name)
            {
                try { return base.Resolve(name); } catch { return null; }
            }

            public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
            {
                try { return base.Resolve(name, parameters); } catch { return null; }
            }
        }
    }
}
