// 核心状态：软件目录、硬件、已安装识别、用户配置、离线包、网络探测。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace AIStation
{
    internal sealed class AppDef
    {
        public string Id, Name, Kind, Region, Homepage, WebUrl, Source, PackageId, DetectName, Pfn, InstallerType;
        public List<string> ExeNames = new List<string>();
        public List<string> ExePaths = new List<string>();
        public List<string> OfflineMatch = new List<string>();

        public bool UsesWinget
        {
            get { return Source == "winget" || Source == "msstore"; }
        }

        public static AppDef From(Dictionary<string, object> d)
        {
            var a = new AppDef();
            a.Id = Json.Str(d, "id");
            a.Name = Json.Str(d, "name");
            a.Kind = Json.Str(d, "kind");
            a.Region = Json.Str(d, "region");
            a.Homepage = Json.Str(d, "homepage");
            a.WebUrl = Json.Str(d, "webUrl");
            var install = Json.Obj(d, "install");
            a.Source = Json.Str(install, "source");
            a.PackageId = Json.Str(install, "id");
            var detect = Json.Obj(d, "detect");
            a.DetectName = Json.Str(detect, "displayName");
            a.Pfn = Json.Str(detect, "pfn");
            a.ExeNames = Json.StrList(detect, "exe");
            a.ExePaths = Json.StrList(detect, "paths");
            var offline = Json.Obj(d, "offline");
            a.OfflineMatch = Json.StrList(offline, "match");
            a.InstallerType = Json.Str(offline, "installerType");
            return a;
        }
    }

    internal sealed class OfflineFile
    {
        public string Path;
        public string Name;
        public long Size;
        public string AppId;
    }

    internal sealed class Core
    {
        public readonly object Sync = new object();
        public readonly string Version;
        public string Token;
        public int Port;
        public string UiDir; // 开发模式：直接读取磁盘上的 ui/，不用嵌入资源

        public List<AppDef> Apps = new List<AppDef>();
        public Dictionary<string, AppDef> AppsById = new Dictionary<string, AppDef>();
        public Dictionary<string, object> CatalogData;

        public Dictionary<string, object> HardwareInfo;
        public Dictionary<string, object> NetworkInfo;
        public Dictionary<string, InstallInfo> Installed = new Dictionary<string, InstallInfo>();
        public bool Scanning;
        private bool _rescanRequested;
        public long ScannedAt;

        public bool WingetChecked;
        public string WingetVersion; // null = 不可用
        public string WingetPath;

        public Dictionary<string, object> Profile;
        public JobRunner Jobs;
        public long LastPing;

        private readonly Dictionary<string, byte[]> _iconCache = new Dictionary<string, byte[]>();
        private string ProfilePath { get { return Path.Combine(Paths.DataDir, "profile.json"); } }

        public event Action InstalledChanged;

        public Core(string version)
        {
            Version = version;
            Token = Guid.NewGuid().ToString("N");
            var custom = Environment.GetEnvironmentVariable("AISTATION_WINGET");
            WingetPath = string.IsNullOrEmpty(custom) ? "winget.exe" : custom;
            Jobs = new JobRunner(this);
        }

        // ---------- 目录 ----------

        public void LoadCatalog()
        {
            var bytes = ReadUiFile("catalog.json");
            if (bytes == null) throw new InvalidOperationException("缺少 catalog.json");
            CatalogData = Json.ParseObject(Encoding.UTF8.GetString(bytes));
            foreach (var o in Json.Arr(CatalogData, "apps"))
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                var app = AppDef.From(d);
                Apps.Add(app);
                AppsById[app.Id] = app;
            }
            Log.Write("目录已加载：{0} 款软件", Apps.Count);
        }

        public AppDef FindApp(string id)
        {
            AppDef a;
            return id != null && AppsById.TryGetValue(id, out a) ? a : null;
        }

        public byte[] ReadUiFile(string name)
        {
            if (name.Contains("..") || name.Contains("\\")) return null;
            if (UiDir != null)
            {
                var p = Path.Combine(UiDir, name);
                return File.Exists(p) ? File.ReadAllBytes(p) : null;
            }
            using (var s = typeof(Core).Assembly.GetManifestResourceStream("ui/" + name))
            {
                if (s == null) return null;
                var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        // ---------- 后台检测 ----------

        public void StartBackgroundWork()
        {
            Background("hardware", delegate
            {
                var hw = Hardware.Detect();
                lock (Sync) HardwareInfo = hw;
                Log.Write("硬件检测完成：{0}", Json.Serialize(hw));
            });
            Background("network", ProbeNetwork);
            Background("winget", CheckWinget);
            Background("detect", RefreshInstalled);
        }

        private static void Background(string name, Action work)
        {
            var t = new Thread(() =>
            {
                try { work(); }
                catch (Exception ex) { Log.Write("后台任务 {0} 失败: {1}", name, ex); }
            });
            t.IsBackground = true;
            t.Name = name;
            t.Start();
        }

        public void RefreshInstalledAsync()
        {
            Background("detect", RefreshInstalled);
        }

        public void RefreshInstalled()
        {
            lock (Sync)
            {
                if (Scanning) { _rescanRequested = true; return; }
                Scanning = true;
            }
            try
            {
                while (true)
                {
                    var result = Detector.Detect(Apps);
                    lock (Sync)
                    {
                        // 本次会话中 winget 已报告安装成功、但识别不到的，保留为已安装
                        foreach (var kv in Installed)
                        {
                            InstallInfo fresh;
                            if (kv.Value.Installed && kv.Value.Via == "winget" &&
                                result.TryGetValue(kv.Key, out fresh) && !fresh.Installed)
                                result[kv.Key] = kv.Value;
                        }
                        Installed = result;
                        ScannedAt = Time.NowMs();
                        if (!_rescanRequested) { Scanning = false; break; }
                        _rescanRequested = false;
                    }
                }
            }
            catch (Exception)
            {
                lock (Sync) Scanning = false;
                throw;
            }
            var h = InstalledChanged;
            if (h != null) h();
        }

        /// <summary>winget 报告成功后立即标记，避免等待下一次扫描。</summary>
        public void MarkInstalled(string appId)
        {
            lock (Sync)
            {
                InstallInfo info;
                if (Installed.TryGetValue(appId, out info) && info.Installed) return;
                Installed[appId] = new InstallInfo { Installed = true, Via = "winget", DisplayName = appId };
            }
        }

        public void ProbeNetwork()
        {
            bool? cn = null, global = null;
            var t1 = new Thread(() => { cn = Probe("https://www.baidu.com/"); });
            var t2 = new Thread(() => { global = Probe("https://www.google.com/generate_204"); });
            t1.IsBackground = t2.IsBackground = true;
            t1.Start(); t2.Start();
            t1.Join(); t2.Join();
            var d = new Dictionary<string, object>();
            d["cn"] = cn;
            d["global"] = global;
            d["checkedAt"] = Time.NowMs();
            lock (Sync) NetworkInfo = d;
            Log.Write("网络探测：国内={0} 海外={1}", cn, global);
        }

        private static bool Probe(string url)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "HEAD";
                req.Timeout = 5000;
                req.ReadWriteTimeout = 5000;
                req.UserAgent = "AIStation";
                using (req.GetResponse()) return true;
            }
            catch (WebException ex)
            {
                // 收到任何 HTTP 响应都说明网络可达
                if (ex.Response != null) { ex.Response.Close(); return true; }
                return false;
            }
            catch (Exception) { return false; }
        }

        public void CheckWinget()
        {
            string version = null;
            try
            {
                var psi = new ProcessStartInfo(WingetPath, "--version")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    var output = p.StandardOutput.ReadToEnd();
                    if (p.WaitForExit(20000) && p.ExitCode == 0) version = output.Trim();
                }
            }
            catch (Exception ex) { Log.Write("winget 不可用: {0}", ex.Message); }
            lock (Sync)
            {
                WingetVersion = string.IsNullOrEmpty(version) ? null : version;
                WingetChecked = true;
            }
            Log.Write("winget 版本：{0}", version ?? "(不可用)");
        }

        /// <summary>winget 1.4 起支持 --disable-interactivity。</summary>
        public bool WingetSupportsDisableInteractivity()
        {
            var v = WingetVersion;
            if (string.IsNullOrEmpty(v)) return false;
            v = v.TrimStart('v', 'V');
            var parts = v.Split('.');
            int major, minor;
            if (parts.Length < 2 || !int.TryParse(parts[0], out major) || !int.TryParse(parts[1], out minor)) return false;
            return major > 1 || (major == 1 && minor >= 4);
        }

        // ---------- 用户配置 ----------

        public void LoadProfile()
        {
            Dictionary<string, object> p = null;
            try { if (File.Exists(ProfilePath)) p = Json.ParseObject(File.ReadAllText(ProfilePath, Encoding.UTF8)); }
            catch (Exception ex) { Log.Write("读取配置失败: {0}", ex.Message); }
            Profile = p ?? new Dictionary<string, object>();
        }

        private static readonly string[] ProfileKeys =
        {
            "onboarded", "answers", "recommendation", "webShortcuts", "hiddenApps", "trayHintShown", "theme"
        };

        public void PatchProfile(Dictionary<string, object> patch)
        {
            lock (Sync)
            {
                foreach (var key in ProfileKeys)
                    if (patch.ContainsKey(key)) Profile[key] = patch[key];
                SaveProfileLocked();
            }
        }

        public void SaveProfileLocked()
        {
            try
            {
                Paths.EnsureDir(Paths.DataDir);
                var tmp = ProfilePath + ".tmp";
                File.WriteAllText(tmp, Json.Serialize(Profile), new UTF8Encoding(false));
                if (File.Exists(ProfilePath)) File.Delete(ProfilePath);
                File.Move(tmp, ProfilePath);
            }
            catch (Exception ex) { Log.Write("保存配置失败: {0}", ex.Message); }
        }

        public void RecordLaunch(string appId)
        {
            lock (Sync)
            {
                var launches = Json.Obj(Profile, "launches") ?? new Dictionary<string, object>();
                launches[appId] = Time.NowMs();
                Profile["launches"] = launches;
                SaveProfileLocked();
            }
        }

        public List<string> WebShortcuts()
        {
            lock (Sync) return Json.StrList(Profile, "webShortcuts");
        }

        public void SetWebShortcut(string appId, bool add)
        {
            lock (Sync)
            {
                var list = Json.StrList(Profile, "webShortcuts");
                list.Remove(appId);
                if (add) list.Add(appId);
                Profile["webShortcuts"] = list;
                SaveProfileLocked();
            }
        }

        // ---------- 离线包 ----------

        public string OfflineDir(string name)
        {
            foreach (var root in new[] { Paths.BaseDir, Path.Combine(Paths.BaseDir, "..") })
            {
                try
                {
                    var p = Path.GetFullPath(Path.Combine(root, name));
                    if (Directory.Exists(p)) return p;
                }
                catch (Exception) { }
            }
            return null;
        }

        public List<OfflineFile> OfflineInstallers()
        {
            var list = new List<OfflineFile>();
            var dir = OfflineDir("apps");
            if (dir == null) return list;
            try
            {
                foreach (var f in Directory.GetFiles(dir))
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".exe" && ext != ".msi") continue;
                    var fi = new FileInfo(f);
                    var item = new OfflineFile { Path = f, Name = fi.Name, Size = fi.Length };
                    var lower = fi.Name.ToLowerInvariant();
                    foreach (var app in Apps)
                    {
                        foreach (var m in app.OfflineMatch)
                        {
                            if (lower.Contains(m.ToLowerInvariant())) { item.AppId = app.Id; break; }
                        }
                        if (item.AppId != null) break;
                    }
                    list.Add(item);
                }
            }
            catch (Exception ex) { Log.Write("读取离线安装包失败: {0}", ex.Message); }
            return list;
        }

        public OfflineFile OfflineInstallerFor(string appId)
        {
            foreach (var f in OfflineInstallers()) if (f.AppId == appId) return f;
            return null;
        }

        public List<OfflineFile> OfflineModels()
        {
            var list = new List<OfflineFile>();
            var dir = OfflineDir("models");
            if (dir == null) return list;
            try
            {
                foreach (var f in Directory.GetFiles(dir, "*.gguf", SearchOption.AllDirectories))
                {
                    var fi = new FileInfo(f);
                    list.Add(new OfflineFile { Path = f, Name = fi.Name, Size = fi.Length });
                }
            }
            catch (Exception ex) { Log.Write("读取离线模型失败: {0}", ex.Message); }
            return list;
        }

        // ---------- 启动 / 打开 ----------

        public string Launch(string appId)
        {
            var app = FindApp(appId);
            if (app == null) return "未知的软件";
            if (app.Kind == "web")
            {
                var err = OpenUrl(app.WebUrl ?? app.Homepage);
                if (err == null) RecordLaunch(appId);
                return err;
            }
            InstallInfo info;
            lock (Sync) Installed.TryGetValue(appId, out info);
            if (info == null || !info.Installed) return app.Name + " 尚未安装";
            if (!info.CanLaunch)
            {
                // winget 刚装好、还没识别到启动方式时，先重新扫描一次
                RefreshInstalled();
                lock (Sync) Installed.TryGetValue(appId, out info);
                if (info == null || !info.CanLaunch) return "找不到 " + app.Name + " 的启动入口，请从开始菜单打开";
            }
            try
            {
                if (!string.IsNullOrEmpty(info.ExePath) && File.Exists(info.ExePath))
                {
                    Process.Start(new ProcessStartInfo(info.ExePath)
                    {
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(info.ExePath)
                    });
                }
                else
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + info.AppId) { UseShellExecute = true });
                }
                RecordLaunch(appId);
                Log.Write("启动 {0}", appId);
                return null;
            }
            catch (Exception ex)
            {
                Log.Write("启动 {0} 失败: {1}", appId, ex.Message);
                return "启动失败：" + ex.Message;
            }
        }

        public static string OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "没有可用的链接";
            if (!url.StartsWith("https://") && !url.StartsWith("http://") && !url.StartsWith("ms-windows-store://"))
                return "不支持的链接";
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return null;
            }
            catch (Exception ex) { return "无法打开链接：" + ex.Message; }
        }

        public byte[] IconPng(string appId)
        {
            lock (_iconCache)
            {
                byte[] cached;
                if (_iconCache.TryGetValue(appId, out cached)) return cached;
            }
            InstallInfo info;
            lock (Sync) Installed.TryGetValue(appId, out info);
            if (info == null || string.IsNullOrEmpty(info.ExePath) || !File.Exists(info.ExePath)) return null;
            byte[] png = null;
            try { png = ExtractLargeIcon(info.ExePath, 64); }
            catch (Exception ex) { Log.Write("提取 {0} 大图标失败: {1}", appId, ex.Message); }
            if (png == null)
            {
                try
                {
                    using (var icon = Icon.ExtractAssociatedIcon(info.ExePath))
                    using (var bmp = icon.ToBitmap())
                    using (var ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Png);
                        png = ms.ToArray();
                    }
                }
                catch (Exception ex) { Log.Write("提取 {0} 图标失败: {1}", appId, ex.Message); }
            }
            lock (_iconCache) _iconCache[appId] = png;
            return png;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, int[] ids, uint count, uint flags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icon);

        /// <summary>按指定尺寸提取 exe 的第一个图标（ExtractAssociatedIcon 只有 32px）。</summary>
        private static byte[] ExtractLargeIcon(string exe, int size)
        {
            var handles = new IntPtr[1];
            var ids = new int[1];
            if (PrivateExtractIcons(exe, 0, size, size, handles, ids, 1, 0) == 0 || handles[0] == IntPtr.Zero) return null;
            try
            {
                using (var icon = Icon.FromHandle(handles[0]))
                using (var bmp = icon.ToBitmap())
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    return ms.ToArray();
                }
            }
            finally { DestroyIcon(handles[0]); }
        }

        // ---------- 状态快照 ----------

        public Dictionary<string, object> InstalledJson()
        {
            var d = new Dictionary<string, object>();
            lock (Sync)
            {
                foreach (var kv in Installed) d[kv.Key] = kv.Value.ToJson();
            }
            return d;
        }

        public Dictionary<string, object> State()
        {
            var d = new Dictionary<string, object>();
            d["version"] = Version;
            d["catalog"] = CatalogData;
            d["installed"] = InstalledJson();
            d["jobs"] = Jobs.ToJson();
            lock (Sync)
            {
                d["hardware"] = HardwareInfo;
                d["network"] = NetworkInfo;
                d["scanning"] = Scanning;
                d["scannedAt"] = ScannedAt;
                d["profile"] = Profile;
                var w = new Dictionary<string, object>();
                w["checked"] = WingetChecked;
                w["available"] = WingetVersion != null;
                w["version"] = WingetVersion;
                d["winget"] = w;
            }
            var offline = new Dictionary<string, object>();
            var installers = new List<object>();
            foreach (var f in OfflineInstallers())
            {
                var x = new Dictionary<string, object>();
                x["name"] = f.Name;
                x["size"] = f.Size;
                x["appId"] = f.AppId;
                installers.Add(x);
            }
            var models = new List<object>();
            foreach (var f in OfflineModels())
            {
                var x = new Dictionary<string, object>();
                x["name"] = f.Name;
                x["size"] = f.Size;
                models.Add(x);
            }
            offline["appsDir"] = OfflineDir("apps");
            offline["modelsDir"] = OfflineDir("models");
            offline["installers"] = installers;
            offline["models"] = models;
            d["offline"] = offline;
            var sys = new Dictionary<string, object>();
            sys["autostart"] = Autostart.IsEnabled();
            sys["dataDir"] = Paths.DataDir;
            sys["exe"] = System.Reflection.Assembly.GetExecutingAssembly().Location;
            d["system"] = sys;
            return d;
        }

        public static string Fmt(double v)
        {
            return v.ToString("0.#", CultureInfo.InvariantCulture);
        }
    }
}
