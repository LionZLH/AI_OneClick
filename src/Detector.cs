// 已安装软件识别：注册表卸载项 + 开始菜单应用（含商店应用）+ 商店包目录 + 常见安装路径。
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace AIStation
{
    internal sealed class RegEntry
    {
        public string KeyName;
        public string DisplayName;
        public string DisplayVersion;
        public string DisplayIcon;
        public string InstallLocation;
        public string UninstallString;
        public string QuietUninstallString;
        public string Publisher;
    }

    internal sealed class StartApp
    {
        public string Name;
        public string AppId; // AUMID 或已知文件夹路径，可用 shell:AppsFolder\<AppId> 启动
    }

    internal sealed class InstallInfo
    {
        public bool Installed;
        public string Via;          // registry / startmenu / store / path
        public string DisplayName;
        public string Version;
        public string ExePath;
        public string AppId;        // 开始菜单 AUMID
        public RegEntry Reg;

        public bool CanLaunch
        {
            get { return !string.IsNullOrEmpty(ExePath) || !string.IsNullOrEmpty(AppId); }
        }

        public Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object>();
            d["installed"] = Installed;
            d["via"] = Via;
            d["displayName"] = DisplayName;
            d["version"] = Version;
            d["canLaunch"] = CanLaunch;
            d["exe"] = ExePath;
            return d;
        }
    }

    internal static class Detector
    {
        private static bool IsWindows
        {
            get { return Environment.OSVersion.Platform == PlatformID.Win32NT; }
        }

        public static List<RegEntry> ScanRegistry()
        {
            var list = new List<RegEntry>();
            if (!IsWindows) return list;
            const string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
            ScanRoot(list, RegistryHive.CurrentUser, RegistryView.Registry64, path);
            ScanRoot(list, RegistryHive.LocalMachine, RegistryView.Registry64, path);
            ScanRoot(list, RegistryHive.LocalMachine, RegistryView.Registry32, path);
            return list;
        }

        private static void ScanRoot(List<RegEntry> list, RegistryHive hive, RegistryView view, string path)
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (var root = baseKey.OpenSubKey(path))
                {
                    if (root == null) return;
                    foreach (var name in root.GetSubKeyNames())
                    {
                        try
                        {
                            using (var k = root.OpenSubKey(name))
                            {
                                if (k == null) continue;
                                var dn = k.GetValue("DisplayName") as string;
                                if (string.IsNullOrEmpty(dn)) continue;
                                list.Add(new RegEntry
                                {
                                    KeyName = name,
                                    DisplayName = dn.Trim(),
                                    DisplayVersion = k.GetValue("DisplayVersion") as string,
                                    DisplayIcon = k.GetValue("DisplayIcon") as string,
                                    InstallLocation = k.GetValue("InstallLocation") as string,
                                    UninstallString = k.GetValue("UninstallString") as string,
                                    QuietUninstallString = k.GetValue("QuietUninstallString") as string,
                                    Publisher = k.GetValue("Publisher") as string
                                });
                            }
                        }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception ex) { Log.Write("读取注册表 {0} 失败: {1}", hive, ex.Message); }
        }

        /// <summary>枚举“开始”菜单中的全部应用（与 PowerShell Get-StartApps 同源），包含商店应用。</summary>
        public static List<StartApp> ScanStartApps()
        {
            var list = new List<StartApp>();
            if (!IsWindows) return list;
            Exception error = null;
            var t = new Thread(() =>
            {
                try
                {
                    var shellType = Type.GetTypeFromProgID("Shell.Application");
                    if (shellType == null) return;
                    var shell = Activator.CreateInstance(shellType);
                    var folder = Invoke(shell, "NameSpace", BindingFlags.InvokeMethod, "shell:::{4234d49b-0245-4df3-b780-3893943456e1}");
                    if (folder == null) return;
                    var items = Invoke(folder, "Items", BindingFlags.InvokeMethod);
                    var count = Convert.ToInt32(Invoke(items, "Count", BindingFlags.GetProperty));
                    for (int i = 0; i < count; i++)
                    {
                        var item = Invoke(items, "Item", BindingFlags.InvokeMethod, i);
                        if (item == null) continue;
                        var name = Invoke(item, "Name", BindingFlags.GetProperty) as string;
                        var id = Invoke(item, "Path", BindingFlags.GetProperty) as string;
                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(id))
                            list.Add(new StartApp { Name = name.Trim(), AppId = id });
                        Marshal.ReleaseComObject(item);
                    }
                    Marshal.ReleaseComObject(items);
                    Marshal.ReleaseComObject(folder);
                    Marshal.ReleaseComObject(shell);
                }
                catch (Exception ex) { error = ex; }
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            if (!t.Join(20000)) Log.Write("枚举开始菜单超时");
            if (error != null) Log.Write("枚举开始菜单失败: {0}", error.Message);
            return list;
        }

        private static object Invoke(object target, string name, BindingFlags flags, params object[] args)
        {
            return target.GetType().InvokeMember(name, flags, null, target, args);
        }

        public static Dictionary<string, InstallInfo> Detect(List<AppDef> apps)
        {
            var reg = ScanRegistry();
            var start = ScanStartApps();
            var result = new Dictionary<string, InstallInfo>();
            foreach (var app in apps)
            {
                if (app.Kind == "web") continue;
                try { result[app.Id] = DetectOne(app, reg, start); }
                catch (Exception ex)
                {
                    Log.Write("识别 {0} 失败: {1}", app.Id, ex.Message);
                    result[app.Id] = new InstallInfo();
                }
            }
            Log.Write("软件识别完成：注册表 {0} 项，开始菜单 {1} 项", reg.Count, start.Count);
            return result;
        }

        private static InstallInfo DetectOne(AppDef app, List<RegEntry> reg, List<StartApp> start)
        {
            var info = new InstallInfo();
            Regex re = null;
            if (!string.IsNullOrEmpty(app.DetectName))
                re = new Regex(app.DetectName, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (re != null)
            {
                foreach (var e in reg)
                {
                    if (!re.IsMatch(e.DisplayName)) continue;
                    info.Installed = true;
                    info.Via = "registry";
                    info.Reg = e;
                    info.DisplayName = e.DisplayName;
                    info.Version = e.DisplayVersion;
                    break;
                }
                foreach (var s in start)
                {
                    if (!re.IsMatch(s.Name)) continue;
                    // 跳过开始菜单里的“卸载 xxx”等快捷方式
                    if (s.Name.IndexOf("卸载", StringComparison.Ordinal) >= 0 ||
                        s.Name.IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    info.AppId = s.AppId;
                    if (!info.Installed)
                    {
                        info.Installed = true;
                        info.Via = "startmenu";
                        info.DisplayName = s.Name;
                    }
                    break;
                }
            }

            if (!string.IsNullOrEmpty(app.Pfn) && IsWindows)
            {
                var pkgDir = Path.Combine(Path.Combine(Paths.LocalAppData, "Packages"), app.Pfn);
                if (Directory.Exists(pkgDir) && !info.Installed)
                {
                    info.Installed = true;
                    info.Via = "store";
                    info.DisplayName = app.Name;
                }
            }

            info.ExePath = ResolveExe(app, info.Reg);
            if (!info.Installed && info.ExePath != null)
            {
                info.Installed = true;
                info.Via = "path";
                info.DisplayName = app.Name;
            }
            return info;
        }

        private static string ResolveExe(AppDef app, RegEntry e)
        {
            foreach (var p in app.ExePaths)
            {
                var full = Paths.ExpandEnv(p);
                if (IsWindows && File.Exists(full)) return full;
            }
            if (app.ExeNames.Count == 0) return null;
            if (e != null)
            {
                var icon = CleanPath(e.DisplayIcon);
                if (icon != null && icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && IsAppExe(app, icon)) return icon;
                foreach (var dir in new[] { e.InstallLocation, DirOf(icon), DirOf(CleanPath(e.UninstallString)) })
                {
                    var hit = FindIn(dir, app.ExeNames, 1);
                    if (hit != null) return hit;
                }
            }
            if (!IsWindows) return null;
            var roots = new List<string>();
            roots.Add(Path.Combine(Paths.LocalAppData, "Programs"));
            roots.Add(Paths.LocalAppData);
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            var x86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            if (!string.IsNullOrEmpty(x86)) roots.Add(x86);
            foreach (var root in roots)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                string[] subs;
                try { subs = Directory.GetDirectories(root); }
                catch (Exception) { continue; }
                foreach (var sub in subs)
                {
                    if (!LooksRelated(app, sub)) continue;
                    var hit = FindIn(sub, app.ExeNames, 2);
                    if (hit != null) return hit;
                }
            }
            return null;
        }

        /// <summary>按目录名粗筛，避免把整个 Program Files 翻一遍。</summary>
        private static bool LooksRelated(AppDef app, string dir)
        {
            var name = Path.GetFileName(dir).ToLowerInvariant().Replace(" ", "").Replace("-", "");
            foreach (var exe in app.ExeNames)
            {
                var stem = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant().Replace(" ", "").Replace("-", "");
                if (stem.Length >= 5 && (name.Contains(stem) || (stem.Contains(name) && name.Length >= 4))) return true;
            }
            var id = app.Id.ToLowerInvariant();
            return name.Contains(id) || (app.Name.Length >= 3 && name.Contains(app.Name.ToLowerInvariant().Replace(" ", "")));
        }

        private static bool IsAppExe(AppDef app, string path)
        {
            if (!File.Exists(path)) return false;
            var file = Path.GetFileName(path);
            if (file.IndexOf("unins", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            foreach (var n in app.ExeNames)
                if (string.Equals(n, file, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string FindIn(string dir, List<string> names, int depth)
        {
            if (string.IsNullOrEmpty(dir)) return null;
            dir = CleanPath(dir);
            try
            {
                if (dir == null || !Directory.Exists(dir)) return null;
                foreach (var n in names)
                {
                    var p = Path.Combine(dir, n);
                    if (File.Exists(p)) return p;
                }
                if (depth <= 1) return null;
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    var hit = FindIn(sub, names, depth - 1);
                    if (hit != null) return hit;
                }
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>去掉引号、",0" 图标序号和命令行参数。</summary>
        public static string CleanPath(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            var s = raw.Trim();
            if (s.StartsWith("\""))
            {
                var end = s.IndexOf('"', 1);
                s = end > 0 ? s.Substring(1, end - 1) : s.Trim('"');
            }
            else
            {
                var comma = s.LastIndexOf(',');
                if (comma > 0 && s.Substring(comma + 1).Trim().TrimStart('-').Length > 0 &&
                    IsDigits(s.Substring(comma + 1).Trim().TrimStart('-'))) s = s.Substring(0, comma);
                var exe = s.IndexOf(".exe ", StringComparison.OrdinalIgnoreCase);
                if (exe > 0) s = s.Substring(0, exe + 4);
            }
            s = Paths.ExpandEnv(s);
            return s.Length == 0 ? null : s;
        }

        private static bool IsDigits(string s)
        {
            foreach (var c in s) if (c < '0' || c > '9') return false;
            return s.Length > 0;
        }

        private static string DirOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try { return Path.GetDirectoryName(path); }
            catch (Exception) { return null; }
        }
    }
}
