using System;
using System.IO;
using System.Linq;
using System.Text;
using TIMF.Abstractions.Storage;

namespace TIMF.Core.Security
{
    internal sealed class ModStorage : IModStorage
    {
        private readonly string _configRoot;
        private readonly string _legacyConfigRoot;
        private readonly string _modId;
        private readonly string _contentRoot;

        public ModStorage(string configDirectory, string modId, string contentDirectory)
        {
            _modId = modId;
            _legacyConfigRoot = Path.GetFullPath(configDirectory);
            _configRoot = Path.GetFullPath(Path.Combine(configDirectory, "mod-data", SafeSegment(modId)));
            _contentRoot = Path.GetFullPath(contentDirectory ?? "");
        }

        public bool ConfigExists(string name)
        {
            TryMigrateLegacy(name);
            var path = ConfigPath(name);
            if (!File.Exists(path)) return false;
            RejectReparsePoints(path, false);
            return true;
        }

        public string ReadConfigText(string name)
        {
            TryMigrateLegacy(name);
            var path = ConfigPath(name);
            RejectReparsePoints(path, false);
            return File.ReadAllText(path, Encoding.UTF8);
        }

        public void WriteConfigText(string name, string text)
        {
            var path = ConfigPath(name);
            EnsureSafeDirectory(_configRoot);
            if (File.Exists(path)) RejectReparsePoints(path, false);
            var temp = path + ".timf-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(fs, new UTF8Encoding(false)))
                {
                    writer.Write(text ?? "");
                    writer.Flush();
                    // Deliberately no fs.Flush(true): a forced physical disk sync (fsync) here
                    // blocks the calling (render) thread for milliseconds and was the cause of the
                    // severe hitch on every config save — i.e. every in-world feature toggle. The
                    // temp-file + atomic File.Replace/Move below already gives crash-consistency
                    // (readers see either the old or the new file, never a torn one); the OS can
                    // flush its own buffer to disk lazily.
                }
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
            }
        }

        public bool ContentExists(string relativePath)
        {
            var path = ContentPath(relativePath);
            if (!File.Exists(path)) return false;
            RejectReparsePoints(path, false);
            return true;
        }

        public byte[] ReadContentBytes(string relativePath)
        {
            var path = ContentPath(relativePath);
            RejectReparsePoints(path, false);
            return File.ReadAllBytes(path);
        }

        private string ConfigPath(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("Config name must be one safe file name.", nameof(name));
            return Path.Combine(_configRoot, name);
        }

        private void TryMigrateLegacy(string name)
        {
            if (!SameIdentity(Path.GetFileNameWithoutExtension(name), _modId)) return;
            var destination = ConfigPath(name);
            if (File.Exists(destination)) return;
            var legacy = Path.Combine(_legacyConfigRoot, name);
            if (!File.Exists(legacy)) return;
            RejectReparsePoints(legacy, false);
            EnsureSafeDirectory(_configRoot);
            File.Copy(legacy, destination, false);
        }

        private static bool SameIdentity(string a, string b)
        {
            Func<string, string> normalize = value => new string((value ?? "")
                .Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
            return normalize(a) == normalize(b);
        }

        private static void EnsureSafeDirectory(string fullPath)
        {
            var full = Path.GetFullPath(fullPath);
            var root = Path.GetPathRoot(full);
            var current = root;
            foreach (var part in full.Substring(root.Length).Split(Path.DirectorySeparatorChar))
            {
                if (part.Length == 0) continue;
                current = Path.Combine(current, part);
                if (Directory.Exists(current))
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new UnauthorizedAccessException("Reparse points are forbidden in mod storage: " + current);
                }
                else
                {
                    Directory.CreateDirectory(current);
                }
            }
        }

        private string ContentPath(string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
                throw new ArgumentException("Content path must be relative.", nameof(relative));
            var full = Path.GetFullPath(Path.Combine(_contentRoot, relative));
            var prefix = _contentRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Content path escapes the mod content directory.");
            return full;
        }

        private static string SafeSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Mod id is required.");
            foreach (var c in value)
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-'))
                    throw new ArgumentException("Mod id contains an unsafe storage character.");
            return value;
        }

        private static void RejectReparsePoints(string fullPath, bool allowMissingLeaf)
        {
            var full = Path.GetFullPath(fullPath);
            var root = Path.GetPathRoot(full);
            var current = root;
            foreach (var part in full.Substring(root.Length).Split(Path.DirectorySeparatorChar))
            {
                if (part.Length == 0) continue;
                current = Path.Combine(current, part);
                if (!File.Exists(current) && !Directory.Exists(current))
                {
                    if (allowMissingLeaf && current.Equals(full, StringComparison.OrdinalIgnoreCase)) return;
                    throw new FileNotFoundException("Storage path component does not exist.", current);
                }
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new UnauthorizedAccessException("Reparse points are forbidden in mod storage: " + current);
            }
        }
    }
}
