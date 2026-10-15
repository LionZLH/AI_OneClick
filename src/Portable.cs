// 绿色版：不安装、不写系统目录。数据放在 exe 旁边的 AIStation-data 文件夹，
// 整个文件夹拷走或删掉就是全部。exe 所在位置不可写时才退回到 %LOCALAPPDATA%\AIStation。
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace AIStation
{
    internal static class Portable
    {
        public const string DataFolderName = "AIStation-data";

        public static string ExePath
        {
            get { return Path.GetFullPath(Application.ExecutablePath); }
        }

        public static string ExeDir
        {
            get { return Path.GetDirectoryName(ExePath); }
        }

        public static string LegacyDataDir
        {
            get { return Path.Combine(Paths.LocalAppData, "AIStation"); }
        }

        /// <summary>true = 数据在 exe 旁边（绿色）；false = 因位置不可写而退回系统目录。</summary>
        public static bool DataNextToExe;

        public static string ResolveDataDir()
        {
            var local = Path.Combine(ExeDir, DataFolderName);
            if (IsWritable(local))
            {
                DataNextToExe = true;
                MigrateLegacy(local);
                return local;
            }
            DataNextToExe = false;
            return LegacyDataDir;
        }

        private static bool IsWritable(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                var probe = Path.Combine(dir, ".write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>旧版本把配置放在 %LOCALAPPDATA%\AIStation；第一次以绿色版运行时把配置带过来。</summary>
        private static void MigrateLegacy(string dataDir)
        {
            try
            {
                var target = Path.Combine(dataDir, "profile.json");
                var old = Path.Combine(LegacyDataDir, "profile.json");
                if (!File.Exists(target) && File.Exists(old)) File.Copy(old, target);
            }
            catch (Exception) { }
        }

        public static bool OnRemovableDrive
        {
            get
            {
                try { return new DriveInfo(Path.GetPathRoot(ExePath)).DriveType == DriveType.Removable; }
                catch (Exception) { return false; }
            }
        }

        public static string DesktopShortcutPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "AI Station.lnk"); }
        }

        /// <summary>桌面快捷方式是否存在且指向当前这个 exe。</summary>
        public static bool DesktopShortcutOk
        {
            get
            {
                try
                {
                    if (!File.Exists(DesktopShortcutPath)) return false;
                    var lnk = OpenShortcut(DesktopShortcutPath);
                    var target = lnk.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, lnk, null) as string;
                    return string.Equals(target, ExePath, StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception) { return false; }
            }
        }

        private static object OpenShortcut(string path)
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) throw new InvalidOperationException("缺少 WScript.Shell");
            var shell = Activator.CreateInstance(t);
            return t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
        }

        /// <summary>在桌面创建（或更新）指向当前 exe 的快捷方式。</summary>
        public static string CreateDesktopShortcut()
        {
            try
            {
                var lnk = OpenShortcut(DesktopShortcutPath);
                var lt = lnk.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { ExePath });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { ExeDir });
                lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { ExePath + ",0" });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "AI 软件启动器与管理工具" });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
                Log.Write("已创建桌面快捷方式 -> {0}", ExePath);
                return null;
            }
            catch (Exception ex)
            {
                Log.Write("创建桌面快捷方式失败: {0}", ex.Message);
                return "无法创建桌面快捷方式：" + ex.Message;
            }
        }

        /// <summary>关闭 AI Station 自己打开的 Edge/Chrome 窗口（按专用配置目录匹配，不影响用户自己的浏览器）。</summary>
        private static void CloseOwnBrowserWindow()
        {
            var profile = Path.Combine(Paths.DataDir, "WebView");
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='msedge.exe' OR Name='chrome.exe'"))
                {
                    foreach (System.Management.ManagementObject mo in searcher.Get())
                    {
                        var line = mo["CommandLine"] as string;
                        if (line == null || line.IndexOf(profile, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        try { Process.GetProcessById(Convert.ToInt32(mo["ProcessId"])).Kill(); } catch (Exception) { }
                    }
                }
            }
            catch (Exception ex) { Log.Write("关闭界面窗口失败: {0}", ex.Message); }
        }

        /// <summary>开机自启记录的是 exe 的位置；程序被移动后再次打开时自动更新到新位置。</summary>
        public static void FixAutostartPath()
        {
            try
            {
                var current = Autostart.RegisteredExe();
                if (current == null) return;
                if (string.Equals(current, ExePath, StringComparison.OrdinalIgnoreCase)) return;
                Autostart.Set(true);
                Log.Write("开机自启位置已更新：{0} -> {1}", current, ExePath);
            }
            catch (Exception) { }
        }

        /// <summary>清除数据：关闭开机自启、删除桌面快捷方式，退出后删除数据文件夹。</summary>
        public static void ClearDataAndExit()
        {
            try { Autostart.Set(false); } catch (Exception) { }
            try { if (DesktopShortcutOk) File.Delete(DesktopShortcutPath); } catch (Exception) { }
            CloseOwnBrowserWindow();
            // 文件可能还被占用：最多重试约 2 分钟，直到文件夹删掉
            var dir = Paths.DataDir;
            var cmd = "for /l %i in (1,1,40) do @(if exist \"" + dir + "\" (rd /s /q \"" + dir + "\" 2>nul & ping -n 4 127.0.0.1 >nul))";
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", "/c " + cmd)
                {
                    CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath()
                });
            }
            catch (Exception ex) { Log.Write("清除数据失败: {0}", ex.Message); }
            var exit = Program.ExitRequested;
            if (exit != null) exit(); else Environment.Exit(0);
        }
    }
}
