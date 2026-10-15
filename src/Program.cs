// AI Station 入口。
// 编译：build.cmd（Windows 自带 csc）或 build.sh（Mono，仅用于开发调试）。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("AI Station")]
[assembly: AssemblyProduct("AI Station")]
[assembly: AssemblyDescription("AI 软件启动器与管理工具")]
[assembly: AssemblyVersion("2.2.0.0")]
[assembly: AssemblyFileVersion("2.2.0.0")]

namespace AIStation
{
    internal static class Program
    {
        public const string Version = "2.2.0";

        /// <summary>由托盘设置：请求整个程序退出。</summary>
        public static Action ExitRequested;

        [STAThread]
        private static int Main(string[] args)
        {
            var opts = Options.Parse(args);

            // 绿色版：数据默认放在 exe 旁边的 AIStation-data 文件夹
            Paths.DataDir = opts.DataDir ?? Portable.ResolveDataDir();
            Paths.EnsureDir(Paths.DataDir);
            Paths.EnsureDir(Path.Combine(Paths.DataDir, "logs"));
            Log.FilePath = Path.Combine(Path.Combine(Paths.DataDir, "logs"), "aistation.log");
            Log.Write("AI Station {0} 启动，参数：{1}", Version, string.Join(" ", args));

            // 未指定目标框架时 .NET 4.x 默认不启用 TLS 1.2，网络探测会失败
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls;

            if (opts.SelfTest != null) return SelfTest.Run(opts, opts.SelfTest);

            Mutex mutex = null;
            if (!opts.Headless)
            {
                bool created;
                mutex = new Mutex(true, "Local\\AIStation.SingleInstance", out created);
                if (!created)
                {
                    // 已经在运行：通知它打开窗口
                    if (!opts.Minimized) NotifyExistingInstance();
                    return 0;
                }
            }

            var core = new Core(Version) { UiDir = opts.UiDir };
            try
            {
                core.LoadCatalog();
            }
            catch (Exception ex)
            {
                Log.Write("加载目录失败: {0}", ex);
                if (!opts.Headless) MessageBox.Show("程序文件已损坏：" + ex.Message, "AI Station", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            core.LoadProfile();

            var api = new Api(core);
            var server = new HttpServer(opts.Port, api.Handle);
            try { server.Start(); }
            catch (Exception ex)
            {
                Log.Write("本地服务启动失败: {0}", ex);
                if (!opts.Headless) MessageBox.Show("无法启动本地服务：" + ex.Message, "AI Station", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            core.Port = server.Port;
            if (opts.Token != null) core.Token = opts.Token;
            Func<string> url = () => "http://127.0.0.1:" + core.Port + "/?t=" + core.Token;
            WriteInstanceFile(core);

            core.Jobs.Start();
            core.StartBackgroundWork();
            if (!opts.Headless) Portable.FixAutostartPath();

            if (opts.Headless)
            {
                Console.WriteLine(url());
                Console.Out.Flush();
                Thread.Sleep(Timeout.Infinite);
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var tray = new TrayContext(core, url, !opts.Minimized);
            api.ShowWindowRequested = tray.ShowWindow;
            Application.Run(tray);
            try { File.Delete(InstanceFile); } catch (Exception) { }
            GC.KeepAlive(mutex);
            return 0;
        }

        // 放在临时目录：不同位置的 exe 副本之间也能找到正在运行的那一个
        private static string InstanceFile
        {
            get { return Path.Combine(Path.GetTempPath(), "AIStation-instance.json"); }
        }

        private static void WriteInstanceFile(Core core)
        {
            try
            {
                var d = new Dictionary<string, object>();
                d["port"] = core.Port;
                d["token"] = core.Token;
                File.WriteAllText(InstanceFile, Json.Serialize(d), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Write("写入实例信息失败: {0}", ex.Message); }
        }

        private static void NotifyExistingInstance()
        {
            try
            {
                var d = Json.ParseObject(File.ReadAllText(InstanceFile));
                var port = (int)Json.Num(d, "port", 0);
                var token = Json.Str(d, "token");
                using (var wc = new WebClient())
                {
                    wc.Headers["X-AIStation-Token"] = token;
                    wc.DownloadString("http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + "/api/show");
                }
            }
            catch (Exception ex)
            {
                Log.Write("通知已运行的实例失败: {0}", ex.Message);
                MessageBox.Show("AI Station 已经在运行，请在任务栏右下角的托盘中打开它。", "AI Station",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }

    internal sealed class Options
    {
        public bool Headless;     // 不显示窗口和托盘，只运行本地服务（测试用）
        public bool Minimized;    // 开机自启：只显示托盘
        public int Port;          // 0 = 随机端口
        public string UiDir;      // 开发模式：从磁盘读取界面文件
        public string DataDir;
        public string Token;      // 测试用：固定令牌
        public string SelfTest;   // 自检并把结果写到该文件

        public static Options Parse(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                Func<string> next = () => i + 1 < args.Length ? args[++i] : null;
                switch (a)
                {
                    case "--headless": o.Headless = true; break;
                    case "--minimized": o.Minimized = true; break;
                    case "--port": int.TryParse(next(), out o.Port); break;
                    case "--ui-dir": o.UiDir = next(); break;
                    case "--data-dir": o.DataDir = next(); break;
                    case "--token": o.Token = next(); break;
                    case "--selftest": o.SelfTest = next() ?? "selftest.json"; break;

                }
            }
            return o;
        }
    }

    /// <summary>在真实 Windows 上检查各项能力，供 CI 和排障使用。</summary>
    internal static class SelfTest
    {
        public static int Run(Options opts, string outFile)
        {
            var report = new Dictionary<string, object>();
            var core = new Core(Program.Version) { UiDir = opts.UiDir };
            try
            {
                core.LoadCatalog();
                report["catalogApps"] = core.Apps.Count;
                foreach (var name in new[] { "index.html", "app.js", "styles.css", "recommend.js", "catalog.json" })
                    report["ui:" + name] = core.ReadUiFile(name) != null;
                report["hardware"] = Hardware.Detect();
                var reg = Detector.ScanRegistry();
                var start = Detector.ScanStartApps();
                report["registryEntries"] = reg.Count;
                report["startApps"] = start.Count;
                var sample = new List<object>();
                for (int i = 0; i < Math.Min(15, start.Count); i++) sample.Add(start[i].Name + " => " + start[i].AppId);
                report["startAppsSample"] = sample;
                core.RefreshInstalled();
                report["installed"] = core.InstalledJson();
                core.CheckWinget();
                report["winget"] = core.WingetVersion;
                core.ProbeNetwork();
                report["network"] = core.NetworkInfo;
                report["edge"] = Browser.FindEdge();
                report["chrome"] = Browser.FindChrome();
                report["ok"] = true;
            }
            catch (Exception ex)
            {
                report["ok"] = false;
                report["error"] = ex.ToString();
            }
            File.WriteAllText(outFile, Json.Serialize(report), new UTF8Encoding(false));
            return Json.Bool(report, "ok") ? 0 : 1;
        }
    }
}
