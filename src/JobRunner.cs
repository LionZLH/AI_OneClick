// 任务队列：安装 / 更新 / 卸载 / 下载模型 / 导入模型。逐个执行，进度和输出均来自真实进程。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace AIStation
{
    internal sealed class Job
    {
        public int Id;
        public string AppId;
        public string Action;     // install / upgrade / uninstall / pull-model / import-model
        public string Arg;        // 模型名 / 文件名 等
        public string Target;     // import-model 的目标软件
        public string Status = "queued"; // queued / running / success / failed / cancelled / manual
        public string Message = "排队中";
        public double Progress = -1;     // -1 = 无法获知百分比
        public string Via;
        public int ExitCode;
        public long CreatedAt, StartedAt, FinishedAt;
        public readonly List<string> Lines = new List<string>();
        public string CurrentLine;
        public string Hint;              // running = 因软件正在运行而失败，界面提供“关闭并重试”
        // 安装 / 更新的阶段与下载进度（来自对 winget 下载文件的实际测量）
        public string Phase;             // prepare / download / install
        public long PhaseStartedAt;
        public long Downloaded;          // 已下载字节
        public long Total;               // 安装包总字节，0 = 未知
        public double Speed;             // 字节/秒

        public bool Finished
        {
            get { return Status != "queued" && Status != "running"; }
        }

        public Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object>();
            d["id"] = Id;
            d["appId"] = AppId;
            d["action"] = Action;
            d["arg"] = Arg;
            d["target"] = Target;
            d["status"] = Status;
            d["message"] = Message;
            d["progress"] = Progress < 0 ? null : (object)Math.Round(Progress, 1);
            d["via"] = Via;
            d["exitCode"] = ExitCode;
            d["createdAt"] = CreatedAt;
            d["startedAt"] = StartedAt;
            d["finishedAt"] = FinishedAt;
            var lines = new List<object>();
            int start = Math.Max(0, Lines.Count - 60);
            for (int i = start; i < Lines.Count; i++) lines.Add(Lines[i]);
            if (!string.IsNullOrEmpty(CurrentLine)) lines.Add(CurrentLine);
            d["log"] = lines;
            d["hint"] = Hint;
            d["phase"] = Phase;
            d["phaseStartedAt"] = PhaseStartedAt;
            d["downloaded"] = Downloaded;
            d["total"] = Total;
            d["speed"] = Math.Round(Speed);
            return d;
        }
    }

    internal sealed class JobRunner
    {
        private readonly Core _core;
        private readonly List<Job> _jobs = new List<Job>();
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);
        private int _nextId = 1;
        private Thread _worker;

        public JobRunner(Core core)
        {
            _core = core;
        }

        public void Start()
        {
            _worker = new Thread(WorkLoop) { IsBackground = true, Name = "jobs" };
            _worker.Start();
        }

        public bool Busy
        {
            get
            {
                lock (_jobs)
                {
                    foreach (var j in _jobs) if (!j.Finished) return true;
                }
                return false;
            }
        }

        public Job Enqueue(string appId, string action, string arg, string target)
        {
            lock (_jobs)
            {
                // 同一软件的同类任务已在排队/进行中时不重复添加
                foreach (var j in _jobs)
                    if (!j.Finished && j.AppId == appId && j.Action == action && j.Arg == arg) return j;
                var job = new Job
                {
                    Id = _nextId++,
                    AppId = appId,
                    Action = action,
                    Arg = arg,
                    Target = target,
                    CreatedAt = Time.NowMs()
                };
                _jobs.Add(job);
                _signal.Set();
                Log.Write("任务 #{0} 入队：{1} {2} {3}", job.Id, action, appId, arg);
                return job;
            }
        }

        public bool Cancel(int id)
        {
            lock (_jobs)
            {
                foreach (var j in _jobs)
                {
                    if (j.Id != id || j.Status != "queued") continue;
                    j.Status = "cancelled";
                    j.Message = "已取消";
                    j.FinishedAt = Time.NowMs();
                    return true;
                }
            }
            return false;
        }

        public void ClearFinished()
        {
            lock (_jobs) _jobs.RemoveAll(j => j.Finished);
        }

        public List<object> ToJson()
        {
            var list = new List<object>();
            lock (_jobs)
            {
                foreach (var j in _jobs) list.Add(j.ToJson());
            }
            return list;
        }

        private Job Next()
        {
            lock (_jobs)
            {
                foreach (var j in _jobs)
                {
                    if (j.Status != "queued") continue;
                    j.Status = "running";
                    j.Message = "准备中…";
                    j.StartedAt = Time.NowMs();
                    return j;
                }
            }
            return null;
        }

        private void WorkLoop()
        {
            while (true)
            {
                var job = Next();
                if (job == null)
                {
                    _signal.WaitOne(2000);
                    continue;
                }
                try { Run(job); }
                catch (Exception ex)
                {
                    Log.Write("任务 #{0} 异常: {1}", job.Id, ex);
                    Finish(job, "failed", "出错了：" + ex.Message);
                }
                Log.Write("任务 #{0} 结束：{1} {2}", job.Id, job.Status, job.Message);
                if (job.Action == "install" || job.Action == "uninstall" || job.Action == "upgrade")
                {
                    try { _core.RefreshInstalled(); }
                    catch (Exception ex) { Log.Write("刷新已安装列表失败: {0}", ex.Message); }
                }
            }
        }

        private void Finish(Job job, string status, string message)
        {
            lock (_jobs)
            {
                job.Status = status;
                job.Message = message;
                job.FinishedAt = Time.NowMs();
                if (status == "success") job.Progress = 100;
                if (!string.IsNullOrEmpty(job.CurrentLine)) { job.Lines.Add(job.CurrentLine); job.CurrentLine = null; }
            }
        }

        private void SetMessage(Job job, string message)
        {
            lock (_jobs) job.Message = message;
        }

        private void Run(Job job)
        {
            switch (job.Action)
            {
                case "install": RunInstall(job); break;
                case "upgrade": RunWingetAction(job, "upgrade"); break;
                case "uninstall": RunUninstall(job); break;
                case "pull-model": RunOllamaPull(job); break;
                case "import-model": RunImportModel(job); break;
                default: Finish(job, "failed", "未知的任务类型"); break;
            }
        }

        // ---------- 安装 ----------

        private void RunInstall(Job job)
        {
            var app = _core.FindApp(job.AppId);
            if (app == null) { Finish(job, "failed", "未知的软件"); return; }

            var offline = _core.OfflineInstallerFor(app.Id);
            if (offline != null)
            {
                RunOfflineInstaller(job, app, offline);
                return;
            }
            if (app.UsesWinget)
            {
                if (!_core.WingetChecked) _core.CheckWinget();
                if (_core.WingetVersion == null)
                {
                    Finish(job, "manual", "这台电脑没有可用的 winget，请从官网下载安装");
                    return;
                }
                RunWingetAction(job, "install");
                return;
            }
            Finish(job, "manual", app.Source == "manual" ? "该软件需要从官网下载安装" : "该软件不支持自动安装");
        }

        private void RunWingetAction(Job job, string verb)
        {
            var app = _core.FindApp(job.AppId);
            if (app == null || !app.UsesWinget) { Finish(job, "failed", "该软件不支持通过 winget " + VerbName(verb)); return; }
            if (verb != "install" && IsRunning(job, app)) return;
            if (!_core.WingetChecked) _core.CheckWinget();
            if (_core.WingetVersion == null) { Finish(job, "failed", "这台电脑没有可用的 winget"); return; }

            var args = new StringBuilder();
            args.Append(verb).Append(" --id \"").Append(app.PackageId).Append("\" --exact");
            if (verb != "uninstall") args.Append(" --source ").Append(app.Source);
            args.Append(" --silent --accept-source-agreements");
            if (verb != "uninstall") args.Append(" --accept-package-agreements");
            if (_core.WingetSupportsDisableInteractivity()) args.Append(" --disable-interactivity");

            job.Via = app.Source == "msstore" ? "Microsoft Store" : "winget";
            InstallerInfo installer = null;
            if (verb != "uninstall")
            {
                SetPhase(job, "prepare", "正在查询安装包信息…");
                if (app.Source == "winget") installer = QueryInstaller(job, app);
            }
            else SetMessage(job, "正在检查…");

            var monitor = new DownloadMonitor(this, job, app, installer);
            if (verb != "uninstall") monitor.Start();
            int code;
            try { code = RunProcess(job, _core.WingetPath, args.ToString(), WingetLine); }
            finally { monitor.Stop(); }
            job.ExitCode = code;

            string friendly;
            var outcome = WingetOutcome(code, verb, out friendly);
            if (outcome == "success" && verb == "install") _core.MarkInstalled(app.Id);
            Finish(job, outcome, friendly);
        }

        internal object Lock { get { return _jobs; } }

        internal void SetPhase(Job job, string phase, string message)
        {
            lock (_jobs)
            {
                if (job.Phase == phase) { if (message != null) job.Message = message; return; }
                // 阶段只前进不后退
                if (job.Phase == "install" && phase != "install") return;
                if (job.Phase == "download" && phase == "prepare") return;
                job.Phase = phase;
                job.PhaseStartedAt = Time.NowMs();
                if (message != null) job.Message = message;
                if (phase == "install") { job.Progress = -1; job.Speed = 0; }
            }
        }

        internal sealed class InstallerInfo
        {
            public string Url;
            public string Version;
            public long Size;
        }

        private static readonly Regex UrlRe = new Regex(@"https?://[^\s""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex InstallerUrlRe = new Regex(@"^\s*(Installer\s*Url|安装程序\s*URL)\s*[:：]\s*(https?://\S+)", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);
        private static readonly Regex VersionRe = new Regex(@"^\s*(Version|版本)\s*[:：]\s*(\S+)", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

        /// <summary>用 winget show 查到本机将使用的安装包地址，再读取文件大小，用于计算真实下载进度。</summary>
        private InstallerInfo QueryInstaller(Job job, AppDef app)
        {
            var info = new InstallerInfo();
            try
            {
                var args = "show --id \"" + app.PackageId + "\" --exact --source winget --accept-source-agreements";
                if (_core.WingetSupportsDisableInteractivity()) args += " --disable-interactivity";
                var psi = new ProcessStartInfo(_core.WingetPath, args)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                string output;
                using (var p = Process.Start(psi))
                {
                    var errTask = new Thread(() => { try { p.StandardError.ReadToEnd(); } catch (Exception) { } }) { IsBackground = true };
                    errTask.Start();
                    output = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(60000)) { try { p.Kill(); } catch (Exception) { } }
                }
                var m = InstallerUrlRe.Match(output);
                if (m.Success) info.Url = m.Groups[2].Value;
                else
                {
                    // winget show 的安装程序信息在最后，取最后一个链接
                    var all = UrlRe.Matches(output);
                    if (all.Count > 0) info.Url = all[all.Count - 1].Value;
                }
                var v = VersionRe.Match(output);
                if (v.Success) info.Version = v.Groups[2].Value;
                if (info.Url != null) info.Size = RemoteSize(info.Url);
                Log.Write("安装包信息 {0}：版本 {1}，{2}，大小 {3}", app.Id, info.Version, info.Url, info.Size);
                if (info.Size > 0) AddLine(job, "安装包：" + Path.GetFileName(new Uri(info.Url).AbsolutePath) + "（" + FormatBytes(info.Size) + "）");
            }
            catch (Exception ex) { Log.Write("查询安装包信息失败: {0}", ex.Message); }
            return info;
        }

        private static long RemoteSize(string url)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "HEAD";
                req.Timeout = 8000;
                req.UserAgent = "winget-cli";
                using (var res = (HttpWebResponse)req.GetResponse())
                    if (res.ContentLength > 0) return res.ContentLength;
            }
            catch (Exception) { }
            try
            {
                // 部分 CDN 不支持 HEAD：只请求第一个字节，从 Content-Range 读总大小
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.AddRange(0, 0);
                req.Timeout = 8000;
                req.UserAgent = "winget-cli";
                using (var res = (HttpWebResponse)req.GetResponse())
                {
                    var range = res.Headers["Content-Range"];
                    long total;
                    if (range != null && range.Contains("/") && long.TryParse(range.Substring(range.LastIndexOf('/') + 1), out total)) return total;
                }
            }
            catch (Exception) { }
            return 0;
        }

        internal static string FormatBytes(long bytes)
        {
            if (bytes >= 1073741824) return (bytes / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
            return (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        /// <summary>
        /// winget 的输出被重定向时不打印进度条，这里直接测量它写入临时目录的安装包大小来得到真实下载进度。
        /// </summary>
        private sealed class DownloadMonitor
        {
            private readonly JobRunner _runner;
            private readonly Job _job;
            private readonly AppDef _app;
            private readonly InstallerInfo _installer;
            private readonly DateTime _startUtc = DateTime.UtcNow.AddSeconds(-5);
            private volatile bool _stop;
            private Thread _thread;
            private string _foundPath;

            public DownloadMonitor(JobRunner runner, Job job, AppDef app, InstallerInfo installer)
            {
                _runner = runner; _job = job; _app = app; _installer = installer;
            }

            public void Start()
            {
                if (_app.Source != "winget") return;
                _thread = new Thread(Loop) { IsBackground = true, Name = "download-monitor" };
                _thread.Start();
            }

            public void Stop()
            {
                _stop = true;
                if (_thread != null) _thread.Join(2000);
                if (_foundPath == null && _thread != null) Log.Write("未找到 {0} 的下载文件（查找位置：{1}）", _app.Id, string.Join("; ", Roots().ToArray()));
            }

            private static List<string> Roots()
            {
                var list = new List<string>();
                list.Add(Path.Combine(Path.GetTempPath(), "WinGet"));
                list.Add(Path.Combine(Path.Combine(Paths.LocalAppData, "Temp"), "WinGet"));
                list.Add(Path.Combine(Path.Combine(Path.Combine(Path.Combine(Path.Combine(Paths.LocalAppData, "Packages"),
                    "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe"), "AC"), "Temp"), "WinGet"));
                return list;
            }

            private FileInfo Find()
            {
                FileInfo best = null;
                foreach (var root in Roots())
                {
                    try
                    {
                        if (!Directory.Exists(root)) continue;
                        foreach (var dir in Directory.GetDirectories(root, _app.PackageId + "*"))
                        {
                            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                            {
                                var fi = new FileInfo(f);
                                if (fi.LastWriteTimeUtc < _startUtc) continue;
                                if (best == null || fi.Length > best.Length) best = fi;
                            }
                        }
                    }
                    catch (Exception) { }
                }
                return best;
            }

            private void Loop()
            {
                long lastSize = 0;
                DateTime lastTime = DateTime.UtcNow, lastGrowth = DateTime.UtcNow;
                double speed = 0;
                long total = _installer == null ? 0 : _installer.Size;
                lock (_runner.Lock) _job.Total = total;
                while (!_stop)
                {
                    Thread.Sleep(400);
                    FileInfo f;
                    try { f = Find(); } catch (Exception) { continue; }
                    if (f == null) continue;
                    if (_foundPath == null)
                    {
                        _foundPath = f.FullName;
                        Log.Write("监测下载文件：{0}", _foundPath);
                    }
                    long size;
                    try { f.Refresh(); size = f.Length; } catch (Exception) { continue; }
                    var now = DateTime.UtcNow;
                    var dt = (now - lastTime).TotalSeconds;
                    if (size > lastSize && dt > 0)
                    {
                        var inst = (size - lastSize) / dt;
                        speed = speed <= 0 ? inst : speed * 0.7 + inst * 0.3;
                        lastGrowth = now;
                    }
                    lastSize = size;
                    lastTime = now;
                    bool done = total > 0 ? size >= total : (now - lastGrowth).TotalSeconds > 4 && size > 0;
                    if (done)
                    {
                        lock (_runner.Lock) { _job.Downloaded = size; _job.Speed = 0; }
                        _runner.SetPhase(_job, "install", "下载完成，正在安装…（如弹出权限确认，请点“是”）");
                        return;
                    }
                    _runner.SetPhase(_job, "download", "正在下载…");
                    lock (_runner.Lock)
                    {
                        if (_job.Phase != "download") return;
                        _job.Downloaded = size;
                        _job.Speed = speed;
                        _job.Progress = total > 0 ? Math.Min(99.9, size * 100.0 / total) : -1;
                    }
                }
            }
        }

        private static string VerbName(string verb)
        {
            if (verb == "install") return "安装";
            if (verb == "upgrade") return "更新";
            return "卸载";
        }

        // 常见 winget 返回码，参见 https://github.com/microsoft/winget-cli/blob/master/doc/windows/package-manager/winget/returnCodes.md
        private static string WingetOutcome(int code, string verb, out string message)
        {
            if (code == 0)
            {
                message = verb == "install" ? "安装完成" : verb == "upgrade" ? "已更新到最新版本" : "已卸载";
                return "success";
            }
            switch ((uint)code)
            {
                case 0x8A15002B: message = "已是最新版本"; return "success";
                case 0x8A150061: message = "已经安装过了"; return "success";
                case 0x8A15010D: message = "已经安装过了"; return "success";
                case 0x8A150109: message = "完成，需要重启电脑后生效"; return "success";
                case 0x8A150014: message = "在软件源中找不到该软件"; return "failed";
                case 0x8A150008: message = "下载失败，请检查网络后重试"; return "failed";
                case 0x8A150011: message = "安装包校验失败（软件可能刚发布新版本），请稍后重试"; return "failed";
                case 0x8A150101: message = "软件正在运行，请先关闭它再重试"; return "failed";
                case 0x8A150102: message = "另一个安装正在进行，请稍后重试"; return "failed";
                case 0x8A150103: message = "文件被占用，请关闭相关程序后重试"; return "failed";
                case 0x8A150105: message = "磁盘空间不足"; return "failed";
                case 0x8A150106: message = "内存不足"; return "failed";
                case 0x8A150107: message = "网络连接失败"; return "failed";
                case 0x8A15010A: message = "需要先重启电脑再安装"; return "failed";
                case 0x8A15010C: message = "安装被取消（可能拒绝了管理员权限确认）"; return "failed";
                case 0x8A15010F: message = "被系统策略阻止"; return "failed";
            }
            message = VerbName(verb) + "失败（错误码 0x" + ((uint)code).ToString("X8", CultureInfo.InvariantCulture) + "），可展开详情查看输出";
            return "failed";
        }

        private static readonly Regex SizeProgress = new Regex(
            @"([\d.]+)\s*(B|KB|MB|GB)\s*/\s*([\d.]+)\s*(B|KB|MB|GB)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex PercentProgress = new Regex(@"(\d{1,3})\s*%", RegexOptions.CultureInvariant);

        private void WingetLine(Job job, string line)
        {
            var l = line.ToLowerInvariant();
            if (l.Contains("downloading") || line.Contains("正在下载")) SetPhase(job, "download", "正在下载…");
            else if (l.Contains("verified installer hash") || line.Contains("已成功验证安装程序哈希")) SetMessage(job, "下载完成，校验通过");
            else if (l.Contains("starting package install") || line.Contains("正在启动程序包安装") || line.Contains("开始安装程序包"))
            {
                SetPhase(job, "install", "正在安装…（如弹出权限确认，请点“是”）");
            }
            else if (l.Contains("starting package uninstall") || line.Contains("正在启动程序包卸载")) SetMessage(job, "正在卸载…");
            else if (l.Contains("no available upgrade") || l.Contains("no applicable upgrade")) SetMessage(job, "已是最新版本");
        }

        private static double ToBytes(string num, string unit)
        {
            double v;
            if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return 0;
            switch (unit.ToUpperInvariant())
            {
                case "KB": return v * 1024;
                case "MB": return v * 1024 * 1024;
                case "GB": return v * 1024 * 1024 * 1024;
                default: return v;
            }
        }

        private void UpdateProgressFrom(Job job, string text)
        {
            var m = SizeProgress.Match(text);
            if (m.Success)
            {
                var done = ToBytes(m.Groups[1].Value, m.Groups[2].Value);
                var total = ToBytes(m.Groups[3].Value, m.Groups[4].Value);
                if (total > 0)
                {
                    lock (_jobs) job.Progress = Math.Min(100, done * 100 / total);
                    return;
                }
            }
            var p = PercentProgress.Match(text);
            if (p.Success)
            {
                double v;
                if (double.TryParse(p.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) && v <= 100)
                    lock (_jobs) job.Progress = v;
            }
        }

        // ---------- 离线安装包 ----------

        private void RunOfflineInstaller(Job job, AppDef app, OfflineFile file)
        {
            job.Via = "离线包";
            string exe = file.Path, args;
            bool silent = true;
            if (file.Path.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            {
                exe = "msiexec.exe";
                args = "/i \"" + file.Path + "\" /qn /norestart";
            }
            else if (app.InstallerType == "nullsoft") args = "/S";
            else if (app.InstallerType == "inno") args = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-";
            else { args = ""; silent = false; }

            SetMessage(job, silent ? "正在从离线包安装…" : "已打开安装程序，请按提示完成安装");
            int code;
            try
            {
                var psi = new ProcessStartInfo(exe, args) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(file.Path) };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit();
                    code = p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                Finish(job, "failed", "无法运行安装包：" + ex.Message);
                return;
            }
            job.ExitCode = code;
            _core.RefreshInstalled();
            InstallInfo info;
            lock (_core.Sync) _core.Installed.TryGetValue(app.Id, out info);
            if (info != null && info.Installed) Finish(job, "success", "安装完成（离线包）");
            else if (code == 0) Finish(job, "success", "安装程序已完成");
            else Finish(job, "failed", "安装程序返回错误码 " + code);
        }

        // ---------- 卸载 ----------

        private void RunUninstall(Job job)
        {
            var app = _core.FindApp(job.AppId);
            if (app == null) { Finish(job, "failed", "未知的软件"); return; }
            InstallInfo info;
            lock (_core.Sync) _core.Installed.TryGetValue(app.Id, out info);
            var reg = info == null ? null : info.Reg;

            // winget 卸载时只会运行注册表里的普通卸载命令，NSIS/Inno 软件会弹出完整的卸载向导。
            // 能确定静默参数时由我们直接运行静默卸载；商店应用和类型不明的软件交给 winget。
            var silentCmd = SilentUninstallCommand(app, reg);
            if (silentCmd == null)
            {
                if (app.UsesWinget && _core.WingetVersion != null)
                {
                    RunWingetAction(job, "uninstall");
                    if (job.Status == "success") WaitUntilRemoved(job, app);
                    return;
                }
                var cmdText = reg == null ? null : reg.UninstallString;
                if (string.IsNullOrEmpty(cmdText)) { Finish(job, "manual", "请在系统“设置 → 应用”中卸载"); return; }
                if (IsRunning(job, app)) return;
                RunUninstallCommand(job, app, cmdText, "已打开卸载程序，请按提示完成卸载");
                return;
            }
            if (IsRunning(job, app)) return;
            RunUninstallCommand(job, app, silentCmd, "正在静默卸载…");
        }

        private static string SilentUninstallCommand(AppDef app, RegEntry reg)
        {
            if (reg == null) return null;
            if (!string.IsNullOrEmpty(reg.QuietUninstallString)) return reg.QuietUninstallString;
            var u = reg.UninstallString;
            if (string.IsNullOrEmpty(u)) return null;
            if (u.StartsWith("MsiExec.exe", StringComparison.OrdinalIgnoreCase))
                return Regex.Replace(u, @"/I\{", "/X{", RegexOptions.IgnoreCase) + " /qn /norestart";
            if (app.InstallerType == "nullsoft") return u + " /S";
            if (app.InstallerType == "inno") return u + " /VERYSILENT /SUPPRESSMSGBOXES /NORESTART";
            return null;
        }

        private void RunUninstallCommand(Job job, AppDef app, string cmd, string message)
        {
            job.Via = "卸载程序";
            SetMessage(job, message);
            AddLine(job, "> " + cmd);
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", "/s /c \"" + cmd + "\"") { UseShellExecute = false, CreateNoWindow = true };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit();
                    job.ExitCode = p.ExitCode;
                }
            }
            catch (Exception ex) { Finish(job, "failed", "无法运行卸载程序：" + ex.Message); return; }
            if (job.ExitCode != 0) { Finish(job, "failed", "卸载程序返回错误码 " + job.ExitCode); return; }
            SetMessage(job, "等待卸载程序完成…");
            WaitUntilRemoved(job, app);
            if (!job.Finished) Finish(job, "success", "已卸载");
        }

        /// <summary>
        /// NSIS 等卸载程序会把自己复制到临时目录后在后台继续执行，winget 返回时软件可能还没删掉。
        /// 这里反复重新识别，直到软件真的消失。
        /// </summary>
        private void WaitUntilRemoved(Job job, AppDef app)
        {
            lock (_core.Sync) _core.Installed.Remove(app.Id); // 去掉“winget 刚装好”的临时标记
            for (int i = 0; i < 40; i++)
            {
                _core.RefreshInstalled();
                InstallInfo info;
                lock (_core.Sync) _core.Installed.TryGetValue(app.Id, out info);
                if (info == null || !info.Installed)
                {
                    if (i > 0 || !job.Finished) Finish(job, "success", "已卸载");
                    return;
                }
                if (i == 0)
                {
                    lock (_jobs) { job.Status = "running"; job.Message = "等待卸载程序完成…"; }
                }
                Thread.Sleep(3000);
            }
            Finish(job, "manual", "卸载程序仍在运行，可能在等待你确认；完成后点击「刷新」");
        }

        /// <summary>
        /// 软件运行时（很多软件关掉窗口后仍在托盘/后台运行）卸载程序会卡在“请先关闭”弹窗上。
        /// job.Arg == "close" 表示用户已同意：先正常关闭，关不掉再强制结束；否则提示用户并提供“关闭并重试”。
        /// </summary>
        private bool IsRunning(Job job, AppDef app)
        {
            var procs = ProcessesOf(app);
            if (procs.Count == 0) return false;
            var names = new List<string>();
            foreach (var p in procs) if (!names.Contains(p.ProcessName)) names.Add(p.ProcessName);
            AddLine(job, "正在运行的进程：" + string.Join(", ", names.ToArray()));

            if (job.Arg != "close")
            {
                foreach (var p in procs) p.Dispose();
                lock (_jobs) job.Hint = "running";
                Finish(job, "failed", app.Name + " 正在运行（可能在托盘或后台），需要先关闭");
                return true;
            }

            SetMessage(job, "正在关闭 " + app.Name + "…");
            foreach (var p in procs)
            {
                try { if (p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow(); } catch (Exception) { }
            }
            for (int i = 0; i < 10 && ProcessesOf(app).Count > 0; i++) Thread.Sleep(500);
            foreach (var p in ProcessesOf(app))
            {
                try { p.Kill(); AddLine(job, "已结束进程 " + p.ProcessName + "（" + p.Id + "）"); }
                catch (Exception ex) { AddLine(job, "无法结束 " + p.ProcessName + "：" + ex.Message); }
            }
            foreach (var p in procs) p.Dispose();
            for (int i = 0; i < 10 && ProcessesOf(app).Count > 0; i++) Thread.Sleep(500);
            var left = ProcessesOf(app);
            if (left.Count == 0)
            {
                AddLine(job, app.Name + " 已关闭");
                return false;
            }
            foreach (var p in left) p.Dispose();
            Finish(job, "failed", "无法自动关闭 " + app.Name + "，请重启电脑后再试");
            return true;
        }

        /// <summary>安装目录下正在运行的全部进程（包括托盘、后台服务等子进程）。</summary>
        private List<Process> ProcessesOf(AppDef app)
        {
            var result = new List<Process>();
            InstallInfo info;
            lock (_core.Sync) _core.Installed.TryGetValue(app.Id, out info);
            if (info == null) return result;
            string dir = null;
            if (!string.IsNullOrEmpty(info.ExePath)) dir = Path.GetDirectoryName(info.ExePath);
            else if (info.Reg != null && !string.IsNullOrEmpty(info.Reg.InstallLocation)) dir = Detector.CleanPath(info.Reg.InstallLocation);
            if (string.IsNullOrEmpty(dir) || dir.Length < 8) return result;
            dir = dir.TrimEnd('\\') + "\\";
            var self = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcesses())
            {
                bool keep = false;
                try
                {
                    keep = p.Id != self && p.MainModule.FileName.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception) { } // 无权限访问的系统进程
                if (keep) result.Add(p); else p.Dispose();
            }
            return result;
        }

        // ---------- 模型 ----------

        private string OllamaExe()
        {
            var candidates = new List<string>();
            candidates.Add(Path.Combine(Path.Combine(Path.Combine(Paths.LocalAppData, "Programs"), "Ollama"), "ollama.exe"));
            InstallInfo info;
            lock (_core.Sync) _core.Installed.TryGetValue("ollama", out info);
            if (info != null && info.ExePath != null)
                candidates.Add(Path.Combine(Path.GetDirectoryName(info.ExePath), "ollama.exe"));
            var custom = Environment.GetEnvironmentVariable("AISTATION_OLLAMA");
            if (!string.IsNullOrEmpty(custom)) candidates.Insert(0, custom);
            foreach (var c in candidates) if (File.Exists(c)) return c;
            return null;
        }

        private static bool OllamaServerUp()
        {
            try
            {
                using (var c = new TcpClient())
                {
                    var ar = c.BeginConnect("127.0.0.1", 11434, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(800)) return false;
                    c.EndConnect(ar);
                    return true;
                }
            }
            catch (Exception) { return false; }
        }

        private bool EnsureOllamaServer(Job job, string exe)
        {
            if (OllamaServerUp()) return true;
            SetMessage(job, "正在启动 Ollama 服务…");
            try
            {
                Process.Start(new ProcessStartInfo(exe, "serve") { UseShellExecute = false, CreateNoWindow = true });
            }
            catch (Exception ex) { Log.Write("启动 ollama serve 失败: {0}", ex.Message); return false; }
            for (int i = 0; i < 30; i++)
            {
                Thread.Sleep(500);
                if (OllamaServerUp()) return true;
            }
            return false;
        }

        private void RunOllamaPull(Job job)
        {
            var model = job.Arg ?? "";
            if (!Regex.IsMatch(model, @"^[a-z0-9._-]+(:[a-z0-9._-]+)?$")) { Finish(job, "failed", "模型名称不合法"); return; }
            var exe = OllamaExe();
            if (exe == null) { Finish(job, "failed", "没有找到 Ollama，请先安装 Ollama"); return; }
            if (!EnsureOllamaServer(job, exe)) { Finish(job, "failed", "Ollama 服务没有启动，请先打开 Ollama 再试"); return; }
            job.Via = "Ollama";
            SetMessage(job, "正在下载模型 " + model + "…");
            var code = RunProcess(job, exe, "pull " + model, null);
            job.ExitCode = code;
            if (code == 0) Finish(job, "success", "模型 " + model + " 已下载，可在 Ollama 中使用");
            else Finish(job, "failed", "下载失败（错误码 " + code + "），请检查网络后重试");
        }

        private void RunImportModel(Job job)
        {
            OfflineFile file = null;
            foreach (var f in _core.OfflineModels()) if (f.Name == job.Arg) file = f;
            if (file == null) { Finish(job, "failed", "离线包中找不到 " + job.Arg); return; }
            var stem = Path.GetFileNameWithoutExtension(file.Name);
            job.Via = "离线包";

            if (job.Target == "ollama")
            {
                var exe = OllamaExe();
                if (exe == null) { Finish(job, "failed", "没有找到 Ollama，请先安装 Ollama"); return; }
                if (!EnsureOllamaServer(job, exe)) { Finish(job, "failed", "Ollama 服务没有启动"); return; }
                var name = Regex.Replace(stem.ToLowerInvariant(), @"[^a-z0-9._-]+", "-").Trim('-');
                var modelfile = Path.Combine(Path.GetTempPath(), "aistation-" + name + ".Modelfile");
                File.WriteAllText(modelfile, "FROM " + file.Path + Environment.NewLine, new UTF8Encoding(false));
                SetMessage(job, "正在导入到 Ollama…");
                var code = RunProcess(job, exe, "create " + name + " -f \"" + modelfile + "\"", null);
                try { File.Delete(modelfile); } catch (Exception) { }
                if (code == 0) Finish(job, "success", "已导入 Ollama，模型名：" + name);
                else Finish(job, "failed", "导入失败（错误码 " + code + "）");
                return;
            }

            string destDir;
            if (job.Target == "lmstudio")
            {
                var root = Path.Combine(Path.Combine(Paths.UserProfile, ".lmstudio"), "models");
                var legacy = Path.Combine(Path.Combine(Path.Combine(Paths.UserProfile, ".cache"), "lm-studio"), "models");
                if (!Directory.Exists(Path.Combine(Paths.UserProfile, ".lmstudio")) && Directory.Exists(legacy)) root = legacy;
                destDir = Path.Combine(Path.Combine(root, "local"), stem);
            }
            else if (job.Target == "gpt4all")
            {
                destDir = Path.Combine(Path.Combine(Paths.LocalAppData, "nomic.ai"), "GPT4All");
            }
            else { Finish(job, "failed", "未知的导入目标"); return; }

            var dest = Path.Combine(destDir, file.Name);
            if (File.Exists(dest) && new FileInfo(dest).Length == file.Size)
            {
                Finish(job, "success", "模型已存在：" + dest);
                return;
            }
            Paths.EnsureDir(destDir);
            SetMessage(job, "正在复制模型文件…");
            var tmp = dest + ".partial";
            var buffer = new byte[4 * 1024 * 1024];
            long copied = 0;
            using (var src = File.OpenRead(file.Path))
            using (var dst = File.Create(tmp))
            {
                int n;
                while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
                {
                    dst.Write(buffer, 0, n);
                    copied += n;
                    lock (_jobs) job.Progress = file.Size > 0 ? copied * 100.0 / file.Size : -1;
                }
            }
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(tmp, dest);
            Finish(job, "success", "已复制到 " + dest);
        }

        // ---------- 进程输出 ----------

        private int RunProcess(Job job, string exe, string args, Action<Job, string> onLine)
        {
            Log.Write("运行：{0} {1}", exe, args);
            AddLine(job, "> " + Path.GetFileName(exe) + " " + args);
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            Process p;
            try { p = Process.Start(psi); }
            catch (Exception ex)
            {
                AddLine(job, "无法启动：" + ex.Message);
                return -1;
            }
            using (p)
            {
                var errThread = new Thread(() => Pump(job, p.StandardError, onLine)) { IsBackground = true };
                errThread.Start();
                Pump(job, p.StandardOutput, onLine);
                errThread.Join(5000);
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        private void Pump(Job job, StreamReader reader, Action<Job, string> onLine)
        {
            var sb = new StringBuilder();
            var buf = new char[1024];
            int n;
            try
            {
                while ((n = reader.Read(buf, 0, buf.Length)) > 0)
                {
                    for (int i = 0; i < n; i++)
                    {
                        var c = buf[i];
                        if (c == '\r' || c == '\n')
                        {
                            var line = Clean(sb.ToString());
                            sb.Length = 0;
                            if (line.Length == 0) continue;
                            UpdateProgressFrom(job, line);
                            if (c == '\n') AddLine(job, line);
                            else lock (_jobs) job.CurrentLine = line; // \r：进度刷新行
                            if (onLine != null) onLine(job, line);
                        }
                        else sb.Append(c);
                    }
                    if (sb.Length > 0)
                    {
                        var partial = Clean(sb.ToString());
                        if (partial.Length > 0)
                        {
                            UpdateProgressFrom(job, partial);
                            lock (_jobs) job.CurrentLine = partial;
                        }
                    }
                }
                var rest = Clean(sb.ToString());
                if (rest.Length > 0) AddLine(job, rest);
            }
            catch (Exception ex) { Log.Write("读取输出失败: {0}", ex.Message); }
        }

        private static readonly Regex Ansi = new Regex(@"\x1B\[[0-9;?]*[A-Za-z]", RegexOptions.CultureInvariant);

        private static string Clean(string s)
        {
            s = Ansi.Replace(s, "");
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (c == '█' || c == '▒' || c == '░' || c == '▓' || c == '▏' || c == '▕') continue; // 进度条方块
                if (c < ' ' && c != '\t') continue;
                sb.Append(c);
            }
            var t = sb.ToString().Trim();
            // winget 的旋转指示符
            if (t == "-" || t == "\\" || t == "|" || t == "/") return "";
            return t;
        }

        private void AddLine(Job job, string line)
        {
            lock (_jobs)
            {
                job.CurrentLine = null;
                if (job.Lines.Count > 0 && job.Lines[job.Lines.Count - 1] == line) return;
                job.Lines.Add(line);
                if (job.Lines.Count > 400) job.Lines.RemoveRange(0, 100);
            }
        }
    }
}
