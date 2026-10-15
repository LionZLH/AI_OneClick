// 把 AI Station 安装到本机：复制到 %LOCALAPPDATA%\Programs\AIStation，创建桌面和开始菜单快捷方式，
// 在系统“设置 → 应用”中登记（可从那里卸载）。只写当前用户目录和 HKCU，不需要管理员权限。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AIStation
{
    internal static class SelfInstall
    {
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AIStation";
        private const string ShortcutName = "AI Station.lnk";

        public static string InstallDir
        {
            get { return Path.Combine(Path.Combine(Paths.LocalAppData, "Programs"), "AIStation"); }
        }

        public static string InstalledExe
        {
            get { return Path.Combine(InstallDir, "AIStation.exe"); }
        }

        public static string CurrentExe
        {
            get { return Path.GetFullPath(Application.ExecutablePath); }
        }

        public static bool RunningFromInstallDir
        {
            get { return string.Equals(CurrentExe, Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase); }
        }

        public static bool IsInstalled
        {
            get
            {
                if (!File.Exists(InstalledExe)) return false;
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey)) return k != null;
                }
                catch (Exception) { return false; }
            }
        }

        private static IEnumerable<string> ShortcutPaths()
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName);
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName);
        }

        /// <summary>安装或覆盖本机副本。返回 null 表示成功，否则为错误说明。</summary>
        public static string Install(string version)
        {
            var problems = new List<string>();
            try
            {
                Paths.EnsureDir(InstallDir);
                if (!RunningFromInstallDir) File.Copy(CurrentExe, InstalledExe, true);
            }
            catch (Exception ex)
            {
                Log.Write("复制程序失败: {0}", ex);
                return "无法复制程序文件：" + ex.Message;
            }

            foreach (var lnk in ShortcutPaths())
            {
                var err = CreateShortcut(lnk, InstalledExe);
                if (err != null) problems.Add(err);
            }

            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
                {
                    k.SetValue("DisplayName", "AI Station");
                    k.SetValue("DisplayVersion", version);
                    k.SetValue("DisplayIcon", InstalledExe + ",0");
                    k.SetValue("Publisher", "AI Station");
                    k.SetValue("InstallLocation", InstallDir);
                    k.SetValue("UninstallString", "\"" + InstalledExe + "\" --uninstall-self");
                    k.SetValue("QuietUninstallString", "\"" + InstalledExe + "\" --uninstall-self --quiet");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    k.SetValue("EstimatedSize", (int)Math.Max(1, new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { problems.Add("登记到系统应用列表失败：" + ex.Message); }

            // 开机自启指向新位置
            if (Autostart.IsEnabled()) Autostart.Set(true, InstalledExe);

            Log.Write("已安装到 {0}{1}", InstallDir, problems.Count > 0 ? "（" + string.Join("；", problems.ToArray()) + "）" : "");
            return problems.Count == 0 ? null : string.Join("；", problems.ToArray());
        }

        /// <summary>通过 WScript.Shell 创建 .lnk 快捷方式（Windows 自带的 COM 组件）。</summary>
        private static string CreateShortcut(string lnkPath, string target)
        {
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return "无法创建快捷方式（缺少 WScript.Shell）";
                var shell = Activator.CreateInstance(t);
                var lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                var lt = lnk.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(target) });
                lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { target + ",0" });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "AI 软件启动器与管理工具" });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
                return null;
            }
            catch (Exception ex)
            {
                Log.Write("创建快捷方式 {0} 失败: {1}", lnkPath, ex.Message);
                return "无法创建快捷方式：" + Path.GetFileName(Path.GetDirectoryName(lnkPath));
            }
        }

        /// <summary>从系统“设置 → 应用”卸载 AI Station 本身（已安装的 AI 软件不受影响）。</summary>
        public static int Uninstall(bool quiet)
        {
            if (!quiet)
            {
                var r = MessageBox.Show("确定要卸载 AI Station 吗？\n\n通过它安装的 AI 软件不会被删除，仍可在系统“应用”中单独管理。",
                    "卸载 AI Station", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (r != DialogResult.OK) return 1;
            }
            Program.StopRunningInstance();

            foreach (var lnk in ShortcutPaths())
            {
                try { if (File.Exists(lnk)) File.Delete(lnk); } catch (Exception) { }
            }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch (Exception) { }
            try { Autostart.Set(false); } catch (Exception) { }

            // 正在运行的就是安装目录里的程序，自己删不掉自己：交给延迟执行的 cmd 清理
            var dirs = new List<string> { InstallDir, Paths.DataDir };
            var cmd = "ping -n 3 127.0.0.1 >nul";
            foreach (var d in dirs) cmd += " & rd /s /q \"" + d + "\"";
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", "/c " + cmd) { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
            }
            catch (Exception ex) { Log.Write("清理程序目录失败: {0}", ex.Message); }

            if (!quiet) MessageBox.Show("AI Station 已卸载。", "卸载 AI Station", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        /// <summary>安装后从新位置重新启动，并让当前进程退出。</summary>
        public static void RelaunchInstalled()
        {
            Process.Start(new ProcessStartInfo(InstalledExe, "--wait-pid " + Process.GetCurrentProcess().Id)
            {
                UseShellExecute = false,
                WorkingDirectory = InstallDir
            });
        }
    }
}
