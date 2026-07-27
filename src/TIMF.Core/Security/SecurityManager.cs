using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TIMF.Abstractions;
using TIMF.Abstractions.Security;

namespace TIMF.Core.Security
{
    internal sealed class SecurityManager : ISecurityCenter
    {
        private sealed class Record
        {
            public SensitiveOperationRequest Request;
            public string Identity;
            public bool Overwrite;
        }

        private sealed class Grant
        {
            public string Identity;
            public SensitiveOperationKind Kind;
            public string Target;
            public string Arguments;
            public string WorkingDirectory;
            public string Purpose;
            public SensitiveAuthorizationScope Scope;
        }

        private readonly ILogger _log;
        private readonly string _storePath;
        private readonly object _sync = new object();
        private readonly Dictionary<string, Record> _requests =
            new Dictionary<string, Record>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Grant> _grants = new List<Grant>();
        private readonly List<AssemblySafetyFinding> _blockedLoads = new List<AssemblySafetyFinding>();
        private bool _show;
        private string _selectedGrantKey;

        public SecurityManager(ILogger log, string configDirectory)
        {
            _log = log;
            _storePath = Path.Combine(configDirectory, "security-grants.v1");
            LoadPersistentGrants();
        }

        public int PendingRequestCount
        {
            get { lock (_sync) return _requests.Values.Count(x => x.Request.Status == SensitiveOperationStatus.Pending); }
        }

        public int PersistentGrantCount
        {
            get { lock (_sync) return _grants.Count(x => x.Scope == SensitiveAuthorizationScope.Persistent); }
        }

        public int BlockedModCount
        {
            get { lock (_sync) return _blockedLoads.Select(x => x.AssemblyPath).Distinct(StringComparer.OrdinalIgnoreCase).Count(); }
        }

        public string BoundaryWarning =>
            "TIMF can enforce only operations performed through its security proxy. " +
            "Loaded .NET mod DLLs are trusted code; direct System.IO, Process or native calls cannot currently be sandboxed.";

        public void Show() { _show = true; }

        internal void RecordBlockedLoad(IEnumerable<AssemblySafetyFinding> findings)
        {
            if (findings == null) return;
            lock (_sync)
            {
                _blockedLoads.AddRange(findings);
                _show = true;
            }
        }

        public ISensitiveOperationService CreateFacade(string modId, string assemblyPath)
        {
            return new ModSecurityFacade(this, modId, ComputeIdentity(modId, assemblyPath));
        }

        internal SensitiveOperationRequest Request(
            string modId, string identity, SensitiveOperationKind kind, string target,
            string arguments, string workingDirectory, bool overwrite, string purpose)
        {
            if (string.IsNullOrWhiteSpace(purpose))
                throw new ArgumentException("A clear user-facing purpose is required.", nameof(purpose));

            target = NormalizeTarget(kind, target);
            workingDirectory = kind == SensitiveOperationKind.ProcessExecution
                ? NormalizeDirectory(workingDirectory) : null;
            arguments = arguments ?? "";

            var request = new SensitiveOperationRequest
            {
                Id = Guid.NewGuid().ToString("N"),
                ModId = modId,
                Kind = kind,
                Target = target,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                Purpose = purpose.Trim(),
                CreatedUtc = DateTime.UtcNow,
                Status = SensitiveOperationStatus.Pending,
            };
            var record = new Record { Request = request, Identity = identity, Overwrite = overwrite };

            lock (_sync)
            {
                if (IsDedicated())
                {
                    request.Status = SensitiveOperationStatus.Denied;
                    request.DecisionReason = "No interactive authorization UI is available on a dedicated server.";
                }
                else
                {
                    var grant = _grants.FirstOrDefault(x => Matches(x, record));
                    if (grant != null)
                    {
                        request.Status = SensitiveOperationStatus.Granted;
                        request.GrantedScope = grant.Scope;
                        request.DecisionReason = "Matched an existing exact authorization.";
                    }
                    else
                    {
                        _show = true;
                    }
                }
                _requests.Add(request.Id, record);
            }

            Audit(record, "requested");
            return Snapshot(request);
        }

        internal SensitiveOperationRequest Get(string ownerIdentity, string id)
        {
            lock (_sync)
            {
                var r = Owned(ownerIdentity, id);
                return Snapshot(r.Request);
            }
        }

        internal void Cancel(string ownerIdentity, string id)
        {
            lock (_sync)
            {
                var r = Owned(ownerIdentity, id);
                if (r.Request.Status == SensitiveOperationStatus.Pending)
                {
                    r.Request.Status = SensitiveOperationStatus.Cancelled;
                    r.Request.DecisionReason = "Cancelled by the requesting mod.";
                    Audit(r, "cancelled");
                }
            }
        }

        internal byte[] ReadAllBytes(string ownerIdentity, string id)
        {
            var r = BeginUse(ownerIdentity, id, SensitiveOperationKind.FileRead);
            try
            {
                RejectReparsePoints(r.Request.Target, false);
                var value = File.ReadAllBytes(r.Request.Target);
                Audit(r, "completed bytes=" + value.Length);
                return value;
            }
            catch (Exception ex)
            {
                Audit(r, "failed " + ex.GetType().Name);
                throw;
            }
        }

        internal void WriteAllBytes(string ownerIdentity, string id, byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var r = BeginUse(ownerIdentity, id, SensitiveOperationKind.FileWrite);
            try
            {
                RejectReparsePoints(r.Request.Target, true);
                if (!r.Overwrite && File.Exists(r.Request.Target))
                    throw new IOException("The authorized request did not permit overwriting the target.");

                var dir = Path.GetDirectoryName(r.Request.Target);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    throw new DirectoryNotFoundException("Target directory does not exist: " + dir);
                var temp = r.Request.Target + ".timf-" + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        fs.Write(data, 0, data.Length);
                        fs.Flush(true);
                    }
                    if (File.Exists(r.Request.Target))
                        File.Replace(temp, r.Request.Target, null);
                    else
                        File.Move(temp, r.Request.Target);
                }
                finally
                {
                    try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
                }
                Audit(r, "completed bytes=" + data.Length);
            }
            catch (Exception ex)
            {
                Audit(r, "failed " + ex.GetType().Name);
                throw;
            }
        }

        internal SensitiveProcessResult RunProcess(string ownerIdentity, string id, int timeoutMilliseconds)
        {
            if (timeoutMilliseconds < 1 || timeoutMilliseconds > 300000)
                throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds), "Timeout must be 1..300000 ms.");
            var r = BeginUse(ownerIdentity, id, SensitiveOperationKind.ProcessExecution);
            try
            {
                RejectReparsePoints(r.Request.Target, false);
                RejectReparsePoints(r.Request.WorkingDirectory, false);
                var psi = new ProcessStartInfo
                {
                    FileName = r.Request.Target,
                    Arguments = r.Request.Arguments,
                    WorkingDirectory = r.Request.WorkingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) throw new InvalidOperationException("Process did not start.");
                    // Drain both pipes concurrently. Sequential ReadToEnd can deadlock when the
                    // child fills the pipe that the parent has not started reading yet.
                    var stdoutTask = p.StandardOutput.ReadToEndAsync();
                    var stderrTask = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMilliseconds))
                    {
                        try { p.Kill(); } catch { /* best effort */ }
                        throw new TimeoutException("Authorized process exceeded its timeout.");
                    }
                    System.Threading.Tasks.Task.WaitAll(stdoutTask, stderrTask);
                    Audit(r, "completed exit=" + p.ExitCode);
                    return new SensitiveProcessResult
                    {
                        ExitCode = p.ExitCode,
                        StandardOutput = stdoutTask.Result,
                        StandardError = stderrTask.Result,
                    };
                }
            }
            catch (Exception ex)
            {
                Audit(r, "failed " + ex.GetType().Name);
                throw;
            }
        }

        private Record BeginUse(string ownerIdentity, string id, SensitiveOperationKind expected)
        {
            lock (_sync)
            {
                var r = Owned(ownerIdentity, id);
                if (r.Request.Kind != expected)
                    throw new InvalidOperationException("Authorization kind does not match this operation.");
                if (r.Request.Status != SensitiveOperationStatus.Granted)
                    throw new UnauthorizedAccessException(r.Request.DecisionReason ?? "Sensitive operation is not authorized.");
                if (r.Request.GrantedScope == SensitiveAuthorizationScope.Once)
                    r.Request.Status = SensitiveOperationStatus.Consumed;
                return r;
            }
        }

        private Record Owned(string ownerIdentity, string id)
        {
            Record r;
            if (string.IsNullOrEmpty(id) || !_requests.TryGetValue(id, out r) ||
                !string.Equals(r.Identity, ownerIdentity, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("Request does not belong to this mod identity.");
            return r;
        }

        internal void Draw(IImmediateModeUi ui)
        {
            if (ui == null || !_show || IsDedicated()) return;
            var open = _show;
            if (ui.Begin(Zh() ? "TIMF 安全中心" : "TIMF Security Center", ref open))
            {
                ui.TextColored(Zh()
                    ? "警告：模组 DLL 是同进程受信任代码；TIMF 只能约束经安全代理执行的操作。"
                    : BoundaryWarning, new Microsoft.Xna.Framework.Color(255, 175, 90));
                ui.TextColored(Zh()
                    ? "未经授权的直接 System.IO / Process / 原生调用目前无法由框架拦截。"
                    : "Direct System.IO / Process / native calls remain outside the current isolation boundary.",
                    new Microsoft.Xna.Framework.Color(255, 145, 100));
                ui.Separator();

                Record pending;
                lock (_sync) pending = _requests.Values
                    .Where(x => x.Request.Status == SensitiveOperationStatus.Pending)
                    .OrderBy(x => x.Request.CreatedUtc).FirstOrDefault();
                if (pending != null)
                    DrawPending(ui, pending);
                else
                    ui.Text(Zh() ? "当前没有待处理的授权申请。" : "No pending authorization requests.");

                ui.Spacing(8f);
                ui.Separator();
                DrawPersistent(ui);

                List<AssemblySafetyFinding> blocked;
                lock (_sync) blocked = _blockedLoads.ToList();
                if (blocked.Count > 0)
                {
                    ui.Spacing(8f);
                    ui.Separator();
                    ui.TextColored(Zh() ? "已拒绝加载的模组" : "Mods rejected before load",
                        new Microsoft.Xna.Framework.Color(255, 120, 100));
                    foreach (var finding in blocked.Take(20))
                        DrawFullText(ui, "", finding.ToString());
                    if (blocked.Count > 20)
                        ui.Text((Zh() ? "其余结果请查看日志：" : "More findings are available in the log: ") + (blocked.Count - 20));
                }
            }
            ui.End();
            _show = open || PendingRequestCount > 0;
        }

        internal void Poll(bool uiAvailable)
        {
            List<Record> denied = null;
            lock (_sync)
            {
                var now = DateTime.UtcNow;
                foreach (var r in _requests.Values)
                {
                    if (r.Request.Status != SensitiveOperationStatus.Pending) continue;
                    if (uiAvailable && now - r.Request.CreatedUtc < TimeSpan.FromMinutes(5)) continue;
                    r.Request.Status = SensitiveOperationStatus.Denied;
                    r.Request.DecisionReason = uiAvailable
                        ? "Authorization request timed out after five minutes."
                        : "TIMF.UI is unavailable; sensitive operations default to denied.";
                    if (denied == null) denied = new List<Record>();
                    denied.Add(r);
                }
            }
            if (denied != null)
                foreach (var r in denied) Audit(r, "denied " + r.Request.DecisionReason);
        }

        private void DrawPending(IImmediateModeUi ui, Record r)
        {
            var q = r.Request;
            ui.TextColored((Zh() ? "模组：" : "Mod: ") + q.ModId,
                new Microsoft.Xna.Framework.Color(255, 220, 120));
            ui.Text((Zh() ? "行为：" : "Operation: ") + KindLabel(q.Kind));
            DrawFullText(ui, Zh() ? "目标：" : "Target: ", q.Target);
            if (!string.IsNullOrEmpty(q.Arguments)) DrawFullText(ui, Zh() ? "参数：" : "Arguments: ", q.Arguments);
            if (!string.IsNullOrEmpty(q.WorkingDirectory)) DrawFullText(ui, Zh() ? "工作目录：" : "Working directory: ", q.WorkingDirectory);
            if (q.Kind == SensitiveOperationKind.FileWrite)
                ui.Text((Zh() ? "允许覆盖：" : "Overwrite: ") + (r.Overwrite ? (Zh() ? "是" : "yes") : (Zh() ? "否" : "no")));
            DrawFullText(ui, Zh() ? "用途：" : "Purpose: ", q.Purpose);
            ui.TextColored(Zh()
                ? "只在你理解目标和用途时授权；拒绝不会执行任何操作。"
                : "Approve only if you understand the exact target and purpose. Denial has no side effects.",
                new Microsoft.Xna.Framework.Color(220, 190, 130));

            if (ui.Button(Zh() ? "拒绝" : "Deny")) Decide(r, null);
            ui.SameLine();
            if (ui.Button(Zh() ? "允许一次" : "Allow once")) Decide(r, SensitiveAuthorizationScope.Once);
            ui.SameLine();
            if (ui.Button(Zh() ? "本次会话允许" : "Allow for session")) Decide(r, SensitiveAuthorizationScope.Session);
            if (q.Kind != SensitiveOperationKind.ProcessExecution &&
                ui.Button(Zh() ? "始终允许此精确操作" : "Always allow this exact operation"))
                Decide(r, SensitiveAuthorizationScope.Persistent);
            else if (q.Kind == SensitiveOperationKind.ProcessExecution)
                ui.TextColored(Zh() ? "进程执行不提供持久授权。" : "Process execution cannot be authorized persistently.",
                    new Microsoft.Xna.Framework.Color(180, 160, 130));
        }

        private void Decide(Record r, SensitiveAuthorizationScope? scope)
        {
            lock (_sync)
            {
                if (r.Request.Status != SensitiveOperationStatus.Pending) return;
                if (!scope.HasValue)
                {
                    r.Request.Status = SensitiveOperationStatus.Denied;
                    r.Request.DecisionReason = "Denied by user.";
                    Audit(r, "denied");
                    return;
                }
                r.Request.Status = SensitiveOperationStatus.Granted;
                r.Request.GrantedScope = scope.Value;
                r.Request.DecisionReason = "Authorized by user (" + scope.Value + ").";
                if (scope.Value != SensitiveAuthorizationScope.Once)
                    _grants.Add(ToGrant(r, scope.Value));
                if (scope.Value == SensitiveAuthorizationScope.Persistent)
                    SavePersistentGrants();
                Audit(r, "authorized scope=" + scope.Value);
            }
        }

        private void DrawPersistent(IImmediateModeUi ui)
        {
            ui.Text(Zh() ? "持久授权（可撤销）" : "Persistent grants (revocable)");
            List<Grant> list;
            lock (_sync) list = _grants.Where(x => x.Scope == SensitiveAuthorizationScope.Persistent).ToList();
            if (list.Count == 0)
            {
                ui.TextColored(Zh() ? "无持久授权。" : "No persistent grants.",
                    new Microsoft.Xna.Framework.Color(150, 150, 150));
                return;
            }
            foreach (var g in list)
            {
                var key = GrantKey(g);
                if (ui.Selectable(g.Identity.Split('|')[0] + " · " + KindLabel(g.Kind) + " · " + g.Target,
                    string.Equals(_selectedGrantKey, key, StringComparison.Ordinal)))
                    _selectedGrantKey = key;
            }
            var selected = list.FirstOrDefault(x => GrantKey(x) == _selectedGrantKey);
            if (selected != null && ui.Button(Zh() ? "撤销所选授权" : "Revoke selected grant"))
            {
                lock (_sync) _grants.RemoveAll(x => GrantKey(x) == _selectedGrantKey);
                _selectedGrantKey = null;
                SavePersistentGrants();
            }
        }

        private static Grant ToGrant(Record r, SensitiveAuthorizationScope scope) => new Grant
        {
            Identity = r.Identity, Kind = r.Request.Kind, Target = r.Request.Target,
            Arguments = r.Request.Arguments, WorkingDirectory = r.Request.WorkingDirectory,
            Purpose = r.Request.Purpose, Scope = scope,
        };

        private static bool Matches(Grant g, Record r) =>
            g.Identity == r.Identity && g.Kind == r.Request.Kind && g.Target == r.Request.Target &&
            g.Arguments == r.Request.Arguments && g.WorkingDirectory == r.Request.WorkingDirectory &&
            g.Purpose == r.Request.Purpose;

        private static string GrantKey(Grant g) => g.Identity + "\n" + g.Kind + "\n" + g.Target + "\n" + g.Arguments + "\n" + g.WorkingDirectory + "\n" + g.Purpose;

        private static void DrawFullText(IImmediateModeUi ui, string label, string value)
        {
            var text = label + (value ?? "");
            const int width = 92;
            for (var i = 0; i < text.Length; i += width)
                ui.Text(text.Substring(i, Math.Min(width, text.Length - i)));
        }

        private void SavePersistentGrants()
        {
            try
            {
                var lines = _grants.Where(x => x.Scope == SensitiveAuthorizationScope.Persistent)
                    .Select(x => string.Join("\t", B64(x.Identity), ((int)x.Kind).ToString(), B64(x.Target),
                        B64(x.Arguments), B64(x.WorkingDirectory), B64(x.Purpose))).ToArray();
                var temp = _storePath + ".tmp";
                File.WriteAllLines(temp, lines, Encoding.UTF8);
                if (File.Exists(_storePath)) File.Replace(temp, _storePath, null);
                else File.Move(temp, _storePath);
            }
            catch (Exception ex) { _log.Warn("Failed to persist security grants: " + ex.Message); }
        }

        private void LoadPersistentGrants()
        {
            try
            {
                if (!File.Exists(_storePath)) return;
                foreach (var line in File.ReadAllLines(_storePath, Encoding.UTF8))
                {
                    var p = line.Split('\t');
                    int kind;
                    if (p.Length != 6 || !int.TryParse(p[1], out kind) || kind < 0 || kind > 2) continue;
                    _grants.Add(new Grant { Identity = UB64(p[0]), Kind = (SensitiveOperationKind)kind,
                        Target = UB64(p[2]), Arguments = UB64(p[3]), WorkingDirectory = UB64(p[4]),
                        Purpose = UB64(p[5]),
                        Scope = SensitiveAuthorizationScope.Persistent });
                }
            }
            catch (Exception ex)
            {
                _grants.Clear();
                _log.Warn("Security grant store rejected; defaulting to deny: " + ex.Message);
            }
        }

        private static string NormalizeTarget(SensitiveOperationKind kind, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Target is required.");
            if (!Path.IsPathRooted(value)) throw new ArgumentException("Sensitive targets must be absolute paths.");
            var full = Path.GetFullPath(value);
            if (kind == SensitiveOperationKind.ProcessExecution && !File.Exists(full))
                throw new FileNotFoundException("Executable does not exist.", full);
            return full;
        }

        private static string NormalizeDirectory(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Working directory is required.");
            var full = Path.GetFullPath(value);
            if (!Directory.Exists(full)) throw new DirectoryNotFoundException(full);
            return full;
        }

        private static void RejectReparsePoints(string path, bool allowMissingLeaf)
        {
            if (string.IsNullOrEmpty(path)) throw new UnauthorizedAccessException("Path is empty.");
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            var current = root;
            foreach (var part in full.Substring(root.Length).Split(Path.DirectorySeparatorChar))
            {
                if (string.IsNullOrEmpty(part)) continue;
                current = Path.Combine(current, part);
                if (!File.Exists(current) && !Directory.Exists(current))
                {
                    if (allowMissingLeaf && string.Equals(current, full, StringComparison.OrdinalIgnoreCase)) return;
                    throw new FileNotFoundException("Authorized path component does not exist.", current);
                }
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new UnauthorizedAccessException("Reparse points are not accepted for sensitive operations: " + current);
            }
        }

        private static string ComputeIdentity(string modId, string assemblyPath)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(assemblyPath))
                return modId + "|" + BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }

        private void Audit(Record r, string outcome) => _log.Info("Security: mod=" + r.Request.ModId +
            " kind=" + r.Request.Kind + " target=" + r.Request.Target + " outcome=" + outcome);
        private static bool IsDedicated() { try { return Terraria.Main.dedServ; } catch { return true; } }
        private static bool Zh() { try { return Terraria.Localization.Language.ActiveCulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase); } catch { return false; } }
        private static string KindLabel(SensitiveOperationKind kind) => kind == SensitiveOperationKind.FileRead ? (Zh() ? "读取文件" : "Read file") : kind == SensitiveOperationKind.FileWrite ? (Zh() ? "写入文件" : "Write file") : (Zh() ? "执行进程" : "Execute process");
        private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? ""));
        private static string UB64(string s) => Encoding.UTF8.GetString(Convert.FromBase64String(s));
        private static SensitiveOperationRequest Snapshot(SensitiveOperationRequest x) => new SensitiveOperationRequest
        {
            Id = x.Id, ModId = x.ModId, Kind = x.Kind, Target = x.Target, Arguments = x.Arguments,
            WorkingDirectory = x.WorkingDirectory, Purpose = x.Purpose, Status = x.Status,
            GrantedScope = x.GrantedScope, DecisionReason = x.DecisionReason, CreatedUtc = x.CreatedUtc,
        };
    }

    internal sealed class ModSecurityFacade : ISensitiveOperationService
    {
        private readonly SecurityManager _manager;
        private readonly string _modId;
        private readonly string _identity;
        public ModSecurityFacade(SecurityManager manager, string modId, string identity)
        { _manager = manager; _modId = modId; _identity = identity; }
        public SensitiveOperationRequest RequestFileRead(string path, string purpose) =>
            _manager.Request(_modId, _identity, SensitiveOperationKind.FileRead, path, "", null, false, purpose);
        public SensitiveOperationRequest RequestFileWrite(string path, bool overwrite, string purpose) =>
            _manager.Request(_modId, _identity, SensitiveOperationKind.FileWrite, path,
                "overwrite=" + (overwrite ? "true" : "false"), null, overwrite, purpose);
        public SensitiveOperationRequest RequestProcess(string executable, string arguments, string workingDirectory, string purpose) =>
            _manager.Request(_modId, _identity, SensitiveOperationKind.ProcessExecution,
                executable, arguments, workingDirectory, false, purpose);
        public SensitiveOperationRequest GetRequest(string requestId) => _manager.Get(_identity, requestId);
        public void Cancel(string requestId) => _manager.Cancel(_identity, requestId);
        public byte[] ReadAllBytes(string requestId) => _manager.ReadAllBytes(_identity, requestId);
        public void WriteAllBytes(string requestId, byte[] data) => _manager.WriteAllBytes(_identity, requestId, data);
        public SensitiveProcessResult RunProcess(string requestId, int timeoutMilliseconds = 30000) =>
            _manager.RunProcess(_identity, requestId, timeoutMilliseconds);
    }
}
