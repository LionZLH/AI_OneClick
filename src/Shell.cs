// 与桌面环境打交道：打开界面窗口（Edge 应用模式）、托盘图标、开机自启。
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AIStation
{
    internal static class Browser
    {
        public static string FindEdge()
        {
            return FindApp("msedge.exe", @"Microsoft\Edge\Application\msedge.exe");
        }

        public static string FindChrome()
        {
            return FindApp("chrome.exe", @"Google\Chrome\Application\chrome.exe");
        }

        private static string FindApp(string exe, string relative)
        {
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                try
                {
                    using (var k = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe))
                    {
                        var v = k == null ? null : k.GetValue(null) as string;
                        if (!string.IsNullOrEmpty(v) && File.Exists(v.Trim('"'))) return v.Trim('"');
                    }
                }
                catch (Exception) { }
            }
            foreach (var env in new[] { "ProgramFiles(x86)", "ProgramFiles", "LOCALAPPDATA" })
            {
                var root = Environment.GetEnvironmentVariable(env);
                if (string.IsNullOrEmpty(root)) continue;
                var p = Path.Combine(root, relative);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        /// <summary>用独立配置目录的 Edge/Chrome 应用窗口打开界面；都没有时用默认浏览器。</summary>
        public static void Open(string url)
        {
            var browser = FindEdge() ?? FindChrome();
            if (browser != null)
            {
                try
                {
                    var profile = Path.Combine(Paths.DataDir, "WebView");
                    var args = "--app=\"" + url + "\" --user-data-dir=\"" + profile + "\" --window-size=1200,820 " +
                               "--no-first-run --no-default-browser-check --disable-sync --disable-extensions " +
                               "--disable-features=Translate,msEdgeSidebarV2,msHubApps,msImplicitSignin";
                    Process.Start(new ProcessStartInfo(browser, args) { UseShellExecute = false });
                    Log.Write("已打开窗口：{0}", Path.GetFileName(browser));
                    return;
                }
                catch (Exception ex) { Log.Write("应用窗口打开失败，改用默认浏览器: {0}", ex.Message); }
            }
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                Log.Write("无法打开浏览器: {0}", ex.Message);
                MessageBox.Show("无法打开界面，请手动在浏览器中访问：\n" + url, "AI Station", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    internal static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name = "AIStation";

        public static bool IsEnabled()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(Name) != null;
            }
            catch (Exception) { return false; }
        }

        public static string Set(bool enabled)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (enabled) k.SetValue(Name, "\"" + Application.ExecutablePath + "\" --minimized");
                    else if (k.GetValue(Name) != null) k.DeleteValue(Name);
                }
                return null;
            }
            catch (Exception ex) { return "设置开机自启失败：" + ex.Message; }
        }
    }

    /// <summary>托盘图标：快速启动已安装的 AI 软件、打开主界面、退出。</summary>
    internal sealed class TrayContext : ApplicationContext
    {
        private readonly Core _core;
        private readonly Func<string> _url;
        private readonly NotifyIcon _icon;
        private readonly ContextMenuStrip _menu;
        private readonly Timer _timer;
        private bool _windowWasAlive;

        public TrayContext(Core core, Func<string> url, bool showWindow)
        {
            _core = core;
            _url = url;
            _menu = new ContextMenuStrip();
            _menu.Opening += (s, e) => BuildMenu();
            _icon = new NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "AI Station",
                Visible = true,
                ContextMenuStrip = _menu
            };
            _icon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
            BuildMenu();

            // 窗口关闭后程序留在托盘，首次时提示一次
            _timer = new Timer { Interval = 5000 };
            _timer.Tick += (s, e) => CheckWindow();
            _timer.Start();

            if (showWindow) ShowWindow();
        }

        private static Icon LoadIcon()
        {
            try
            {
                var ic = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (ic != null) return ic;
            }
            catch (Exception) { }
            return SystemIcons.Application;
        }

        public void ShowWindow()
        {
            Browser.Open(_url());
        }

        private void CheckWindow()
        {
            var alive = Time.NowMs() - _core.LastPing < 15000;
            if (_windowWasAlive && !alive)
            {
                bool shown;
                lock (_core.Sync) shown = Json.Bool(_core.Profile, "trayHintShown");
                if (!shown)
                {
                    _icon.ShowBalloonTip(5000, "AI Station 仍在后台运行", "点击托盘图标可以重新打开，右键可快速启动 AI 软件。", ToolTipIcon.Info);
                    lock (_core.Sync)
                    {
                        _core.Profile["trayHintShown"] = true;
                        _core.SaveProfileLocked();
                    }
                }
            }
            _windowWasAlive = alive;
        }

        private void BuildMenu()
        {
            _menu.Items.Clear();
            var open = new ToolStripMenuItem("打开 AI Station");
            open.Font = new Font(open.Font, FontStyle.Bold);
            open.Click += (s, e) => ShowWindow();
            _menu.Items.Add(open);
            _menu.Items.Add(new ToolStripSeparator());

            var launch = new ToolStripMenuItem("快速启动");
            foreach (var app in _core.Apps)
            {
                InstallInfo info;
                bool ok;
                lock (_core.Sync) ok = _core.Installed.TryGetValue(app.Id, out info) && info.Installed;
                if (app.Kind == "web") ok = _core.WebShortcuts().Contains(app.Id);
                if (!ok) continue;
                var id = app.Id;
                var item = new ToolStripMenuItem(app.Name);
                item.Click += (s, e) =>
                {
                    var err = _core.Launch(id);
                    if (err != null) _icon.ShowBalloonTip(4000, "无法启动", err, ToolTipIcon.Warning);
                };
                launch.DropDownItems.Add(item);
            }
            if (launch.DropDownItems.Count == 0)
            {
                launch.DropDownItems.Add(new ToolStripMenuItem("（还没有安装 AI 软件）") { Enabled = false });
            }
            _menu.Items.Add(launch);

            var auto = new ToolStripMenuItem("开机时自动启动") { Checked = Autostart.IsEnabled() };
            auto.Click += (s, e) => Autostart.Set(!Autostart.IsEnabled());
            _menu.Items.Add(auto);
            _menu.Items.Add(new ToolStripSeparator());

            var exit = new ToolStripMenuItem("退出");
            exit.Click += (s, e) => ExitThread();
            _menu.Items.Add(exit);
        }

        protected override void ExitThreadCore()
        {
            if (_core.Jobs.Busy)
            {
                var r = MessageBox.Show("还有任务正在进行，退出后它们会中断。确定退出吗？", "AI Station",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (r != DialogResult.OK) return;
            }
            _timer.Stop();
            _icon.Visible = false;
            _icon.Dispose();
            base.ExitThreadCore();
        }
    }
}
