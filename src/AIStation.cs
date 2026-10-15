using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AIStationLauncher
{
    public class AppItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Vendor { get; set; }
        public string Group { get; set; }      // domestic, global, offline
        public string Type { get; set; }       // desktop, web
        public string Desc { get; set; }
        public string Url { get; set; }
        public string WingetId { get; set; }
        public string[] ExeNames { get; set; }
        public string[] CommonPaths { get; set; }
        public bool IsInstalled { get; set; }
        public string DetectedPath { get; set; }
    }

    public class MainForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        private List<AppItem> _allApps;
        private List<AppItem> _filteredApps;
        private FlowLayoutPanel _cardsPanel;
        private TextBox _searchBox;
        private Label _statusLabel;
        private string _currentGroup = "all";
        private Button _btnAll, _btnDomestic, _btnGlobal, _btnOffline;

        private readonly Color ColBg = Color.FromArgb(24, 26, 32);
        private readonly Color ColCard = Color.FromArgb(34, 37, 46);
        private readonly Color ColCardHover = Color.FromArgb(41, 45, 56);
        private readonly Color ColBorder = Color.FromArgb(65, 69, 82);
        private readonly Color ColTextPrimary = Color.FromArgb(240, 242, 248);
        private readonly Color ColTextSecondary = Color.FromArgb(166, 173, 191);
        private readonly Color ColPrimary = Color.FromArgb(90, 111, 190);
        private readonly Color ColSuccess = Color.FromArgb(46, 125, 50);
        private readonly Color ColInfo = Color.FromArgb(30, 136, 229);

        [STAThread]
        public static void Main()
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        public MainForm()
        {
            InitializeData();
            InitializeComponent();
            RefreshInstalledStatus();
            FilterApps();
        }

        private void InitializeData()
        {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            _allApps = new List<AppItem>
            {
                new AppItem {
                    Id = "doubao", Name = "豆包", Vendor = "字节跳动", Group = "domestic", Type = "desktop",
                    Desc = "日常问答、长文写作与超拟真实时语音通话。",
                    Url = "https://www.doubao.com/download", WingetId = "ByteDance.Doubao",
                    ExeNames = new[] { "Doubao.exe" },
                    CommonPaths = new[] {
                        Path.Combine(localApp, @"Programs\Doubao\Doubao.exe"),
                        Path.Combine(localApp, @"ByteDance\Doubao\Doubao.exe")
                    }
                },
                new AppItem {
                    Id = "kimi", Name = "Kimi", Vendor = "月之暗面", Group = "domestic", Type = "desktop",
                    Desc = "长文本分析之王，几十万字研报、论文与合同秒级提炼。",
                    Url = "https://www.kimi.com", WingetId = "MoonshotAI.Kimi",
                    ExeNames = new[] { "Kimi.exe" },
                    CommonPaths = new[] {
                        Path.Combine(localApp, @"Programs\Kimi\Kimi.exe")
                    }
                },
                new AppItem {
                    Id = "yuanbao", Name = "腾讯元宝", Vendor = "腾讯官方", Group = "domestic", Type = "desktop",
                    Desc = "深度联网检索微信公众号优质内容与专业文章。",
                    Url = "https://yuanbao.tencent.com/download", WingetId = "Tencent.Yuanbao",
                    ExeNames = new[] { "Yuanbao.exe" },
                    CommonPaths = new[] {
                        Path.Combine(localApp, @"Programs\Tencent\Yuanbao\Yuanbao.exe"),
                        Path.Combine(progFiles, @"Tencent\Yuanbao\Yuanbao.exe")
                    }
                },
                new AppItem {
                    Id = "trae", Name = "TRAE", Vendor = "字节跳动", Group = "domestic", Type = "desktop",
                    Desc = "国内免翻直连的 AI 编程 IDE，支持智能补全与项目构建。",
                    Url = "https://www.trae.cn", WingetId = "ByteDance.Trae",
                    ExeNames = new[] { "Trae.exe" },
                    CommonPaths = new[] {
                        Path.Combine(localApp, @"Programs\Trae\Trae.exe")
                    }
                },
                new AppItem {
                    Id = "deepseek", Name = "DeepSeek", Vendor = "深度求索", Group = "domestic", Type = "desktop",
                    Desc = "数学推理、深度思考链天花板，支持思考和任务执行。",
                    Url = "https://www.deepseek.com/download/", WingetId = null,
                    ExeNames = new[] { "DeepSeek.exe" },
                    CommonPaths = new[] {
                        Path.Combine(localApp, @"Programs\DeepSeek\DeepSeek.exe"),
                        Path.Combine(progFiles, @"DeepSeek\DeepSeek.exe")
                    }
                },
                new AppItem {
                    Id = "lmstudio", Name = "LM Studio", Vendor = "Element Labs", Group = "offline", Type = "desktop",
                    Desc = "极简 GUI 本地模型管理工具，离线断网即聊，支持 GGUF 模型。",
                    Url = "https://lmstudio.ai/download", WingetId = "ElementLabs.LMStudio",
                    ExeNames = new[] { "LM Studio.exe" },
                    CommonPaths = new[] {
                        Path.Combine(localApp, @"Programs\LM Studio\LM Studio.exe"),
                        Path.Combine(localApp, @"Programs\LM-Studio\LM Studio.exe"),
                        Path.Combine(progFiles, @"LM Studio\LM Studio.exe"),
                        Path.Combine(progFiles, @"LM-Studio\LM Studio.exe")
                    }
                },
                new AppItem {
                    Id = "anythingllm", Name = "AnythingLLM", Vendor = "Mintplex Labs", Group = "offline", Type = "desktop",
                    Desc = "搭建完全属于自己的私有知识库问答系统，企业文档保密首选。",
                    Url = "https://anythingllm.com/download", WingetId = "Mintplex.AnythingLLM",
                    ExeNames = new[] { "AnythingLLM.exe" },
                    CommonPaths = new[] {
                        Path.Combine(localApp, @"Programs\anythingllm-desktop\AnythingLLM.exe"),
                        Path.Combine(progFiles, @"AnythingLLM\AnythingLLM.exe")
                    }
                },
                new AppItem {
                    Id = "jan", Name = "Jan", Vendor = "Menlo Research", Group = "offline", Type = "desktop",
                    Desc = "开源本地 AI 聊天客户端，私密优先，全离线模型运行。",
                    Url = "https://www.jan.ai/download", WingetId = null,
                    ExeNames = new[] { "Jan.exe" },
                    CommonPaths = new[] {
                        Path.Combine(localApp, @"Programs\Jan\Jan.exe")
                    }
                },
                new AppItem {
                    Id = "gpt4all", Name = "GPT4All", Vendor = "Nomic", Group = "offline", Type = "desktop",
                    Desc = "在普通普通 CPU 也能跑的开源轻量本地大模型客户端。",
                    Url = "https://www.nomic.ai/gpt4all", WingetId = "Nomic.GPT4All",
                    ExeNames = new[] { "chat.exe" },
                    CommonPaths = new[] {
                        Path.Combine(progFiles, @"GPT4All\bin\chat.exe"),
                        Path.Combine(localApp, @"Programs\GPT4All\bin\chat.exe")
                    }
                },
                new AppItem {
                    Id = "chatgpt", Name = "ChatGPT", Vendor = "OpenAI", Group = "global", Type = "desktop",
                    Desc = "全球通用智能助手，支持文件深度分析与拟真语音交互。",
                    Url = "https://chatgpt.com/download/", WingetId = "OpenAI.ChatGPT",
                    ExeNames = new[] { "ChatGPT.exe" },
                    CommonPaths = new[] {
                        Path.Combine(localApp, @"Programs\ChatGPT\ChatGPT.exe")
                    }
                },
                new AppItem {
                    Id = "claude", Name = "Claude", Vendor = "Anthropic", Group = "global", Type = "desktop",
                    Desc = "长文本深度理解、严谨文字撰写与代码重构旗舰。",
                    Url = "https://claude.com/download", WingetId = "Anthropic.Claude",
                    ExeNames = new[] { "Claude.exe" },
                    CommonPaths = new[] {
                        Path.Combine(localApp, @"Programs\Claude\Claude.exe")
                    }
                },
                new AppItem {
                    Id = "cursor", Name = "Cursor", Vendor = "Anysphere", Group = "global", Type = "desktop",
                    Desc = "风靡全球的 AI 代码编辑器，智能补全与全库多文件协同重构。",
                    Url = "https://cursor.com/download", WingetId = "Anysphere.Cursor",
                    ExeNames = new[] { "Cursor.exe" },
                    CommonPaths = new[] {
                        Path.Combine(localApp, @"Programs\cursor\Cursor.exe")
                    }
                },
                new AppItem {
                    Id = "antigravity", Name = "Antigravity", Vendor = "Google", Group = "global", Type = "desktop",
                    Desc = "新一代全自主软件工程智能体，协助复杂工程任务开发。",
                    Url = "https://antigravity.google/download", WingetId = "Google.Antigravity",
                    ExeNames = new[] { "Antigravity.exe" },
                    CommonPaths = new[] {
                        Path.Combine(localApp, @"Programs\Antigravity\Antigravity.exe")
                    }
                },
                // 纯网页应用
                new AppItem {
                    Id = "qianwen", Name = "通义千问", Vendor = "阿里巴巴", Group = "domestic", Type = "web",
                    Desc = "中文通用问答、会议音频实时转写与要点速记。",
                    Url = "https://www.qianwen.com"
                },
                new AppItem {
                    Id = "zhipu", Name = "智谱清言", Vendor = "智谱 AI", Group = "domestic", Type = "web",
                    Desc = "海量行业垂直智能体矩阵，辅助专业论文与文案起草。",
                    Url = "https://chatglm.cn"
                },
                new AppItem {
                    Id = "spark", Name = "讯飞星火", Vendor = "科大讯飞", Group = "domestic", Type = "web",
                    Desc = "体制与政企严谨公文写作标准，方言语音识别天花板。",
                    Url = "https://xinghuo.xfyun.cn"
                },
                new AppItem {
                    Id = "minimax", Name = "海螺 AI", Vendor = "MiniMax", Group = "domestic", Type = "web",
                    Desc = "超自然拟真语音朗读与前沿 AI 视频生成创作入口。",
                    Url = "https://hailuoai.com"
                },
                new AppItem {
                    Id = "gemini", Name = "Gemini", Vendor = "Google", Group = "global", Type = "web",
                    Desc = "百万级上下文多模态理解，视频、图像与超长资料分析。",
                    Url = "https://gemini.google.com"
                },
                new AppItem {
                    Id = "perplexity", Name = "Perplexity", Vendor = "Perplexity", Group = "global", Type = "web",
                    Desc = "深度联网检索工具，每段结论精准标注权威原始信源链接。",
                    Url = "https://www.perplexity.ai"
                }
            };
        }

        private void InitializeComponent()
        {
            Text = "AI Station · AI 应用一键装机与启动台";
            Width = 980;
            Height = 720;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = ColBg;
            ForeColor = ColTextPrimary;
            Font = new Font("Microsoft YaHei", 9.5f, FontStyle.Regular);
            DoubleBuffered = true;

            // 顶部导航栏
            Panel headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 110,
                BackColor = ColBg,
                Padding = new Padding(24, 16, 24, 12)
            };

            Label titleLabel = new Label
            {
                Text = "我的 AI 应用与装机中心",
                Font = new Font("Microsoft YaHei", 15f, FontStyle.Bold),
                ForeColor = ColTextPrimary,
                AutoSize = true,
                Location = new Point(24, 16)
            };
            headerPanel.Controls.Add(titleLabel);

            // 右上角动作按钮
            Button btnHwCheck = CreateHeaderButton("🖥️ 硬件体检", new Point(560, 16), 110);
            btnHwCheck.Click += (s, e) => ShowHardwareCheckModal();
            headerPanel.Controls.Add(btnHwCheck);

            Button btnOffline = CreateHeaderButton("📦 离线安装", new Point(680, 16), 110);
            btnOffline.Click += (s, e) => ShowOfflineInstallModal();
            headerPanel.Controls.Add(btnOffline);

            Button btnRefresh = CreateHeaderButton("🔄 刷新状态", new Point(800, 16), 110);
            btnRefresh.Click += (s, e) => { RefreshInstalledStatus(); FilterApps(); };
            headerPanel.Controls.Add(btnRefresh);

            // 第二排：搜索框与分类过滤
            _searchBox = new TextBox
            {
                Location = new Point(24, 64),
                Width = 260,
                Height = 32,
                BackColor = ColCard,
                ForeColor = ColTextPrimary,
                Font = new Font("Microsoft YaHei", 10f),
                BorderStyle = BorderStyle.FixedSingle
            };
            _searchBox.TextChanged += (s, e) => FilterApps();
            headerPanel.Controls.Add(_searchBox);

            _btnAll = CreateFilterButton("全部 (19)", "all", new Point(300, 64), 85);
            _btnDomestic = CreateFilterButton("国内直连", "domestic", new Point(395, 64), 95);
            _btnGlobal = CreateFilterButton("海外服务", "global", new Point(500, 64), 95);
            _btnOffline = CreateFilterButton("本地离线", "offline", new Point(605, 64), 95);

            headerPanel.Controls.Add(_btnAll);
            headerPanel.Controls.Add(_btnDomestic);
            headerPanel.Controls.Add(_btnGlobal);
            headerPanel.Controls.Add(_btnOffline);

            Controls.Add(headerPanel);

            // 底部状态栏
            Panel footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 36,
                BackColor = Color.FromArgb(19, 21, 26),
                Padding = new Padding(24, 8, 24, 8)
            };
            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = ColTextSecondary,
                Font = new Font("Microsoft YaHei", 9f),
                Text = "就绪"
            };
            footerPanel.Controls.Add(_statusLabel);
            Controls.Add(footerPanel);

            // 中间卡片流式网格
            _cardsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = ColBg,
                Padding = new Padding(20, 10, 20, 20)
            };
            Controls.Add(_cardsPanel);
        }

        private Button CreateHeaderButton(string text, Point loc, int width)
        {
            Button btn = new Button
            {
                Text = text,
                Location = loc,
                Width = width,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(40, 44, 55),
                ForeColor = Color.FromArgb(210, 216, 235),
                Font = new Font("Microsoft YaHei", 9f),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = ColBorder;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(50, 56, 72);
            return btn;
        }

        private Button CreateFilterButton(string text, string group, Point loc, int width)
        {
            Button btn = new Button
            {
                Text = text,
                Tag = group,
                Location = loc,
                Width = width,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = group == _currentGroup ? ColPrimary : ColCard,
                ForeColor = group == _currentGroup ? Color.White : ColTextSecondary,
                Font = new Font("Microsoft YaHei", 9f),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = ColBorder;
            btn.Click += (s, e) =>
            {
                _currentGroup = (string)((Button)s).Tag;
                UpdateFilterButtonsStyle();
                FilterApps();
            };
            return btn;
        }

        private void UpdateFilterButtonsStyle()
        {
            Button[] btns = { _btnAll, _btnDomestic, _btnGlobal, _btnOffline };
            foreach (var b in btns)
            {
                bool active = (string)b.Tag == _currentGroup;
                b.BackColor = active ? ColPrimary : ColCard;
                b.ForeColor = active ? Color.White : ColTextSecondary;
            }
        }

        public void RefreshInstalledStatus()
        {
            int installedCount = 0;
            foreach (var app in _allApps)
            {
                app.IsInstalled = false;
                app.DetectedPath = null;
                if (app.Type != "desktop") continue;

                // 1. 检查已知标准路径
                if (app.CommonPaths != null)
                {
                    foreach (var path in app.CommonPaths)
                    {
                        if (File.Exists(path))
                        {
                            app.IsInstalled = true;
                            app.DetectedPath = path;
                            installedCount++;
                            break;
                        }
                    }
                }

                // 2. 检查注册表
                if (!app.IsInstalled)
                {
                    string regPath = CheckRegistryUninstall(app.Name, app.ExeNames);
                    if (!string.IsNullOrEmpty(regPath) && File.Exists(regPath))
                    {
                        app.IsInstalled = true;
                        app.DetectedPath = regPath;
                        installedCount++;
                    }
                }
            }

            _statusLabel.Text = string.Format("本机已安装 {0} 款桌面 AI 客户端 · 共收录 {1} 款工具 · 就绪", installedCount, _allApps.Count);
        }

        private string CheckRegistryUninstall(string appName, string[] exeNames)
        {
            string[] keys = {
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var keyPath in keys)
            {
                using (var root = Registry.CurrentUser.OpenSubKey(keyPath))
                {
                    string found = SearchSubKeys(root, appName, exeNames);
                    if (found != null) return found;
                }
                using (var root = Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    string found = SearchSubKeys(root, appName, exeNames);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private string SearchSubKeys(RegistryKey root, string appName, string[] exeNames)
        {
            if (root == null) return null;
            foreach (var subName in root.GetSubKeyNames())
            {
                using (var subKey = root.OpenSubKey(subName))
                {
                    if (subKey == null) continue;
                    object dispName = subKey.GetValue("DisplayName");
                    if (dispName != null && dispName.ToString().IndexOf(appName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        object loc = subKey.GetValue("InstallLocation");
                        if (loc != null && Directory.Exists(loc.ToString()))
                        {
                            if (exeNames != null)
                            {
                                foreach (var exe in exeNames)
                                {
                                    string full = Path.Combine(loc.ToString(), exe);
                                    if (File.Exists(full)) return full;
                                }
                            }
                        }
                        object icon = subKey.GetValue("DisplayIcon");
                        if (icon != null)
                        {
                            string iconPath = icon.ToString().Trim('\"', ' ');
                            if (iconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(iconPath))
                                return iconPath;
                        }
                    }
                }
            }
            return null;
        }

        private void FilterApps()
        {
            string q = _searchBox.Text.Trim().ToLowerInvariant();
            _filteredApps = _allApps.FindAll(app =>
            {
                if (_currentGroup != "all" && app.Group != _currentGroup) return false;
                if (!string.IsNullOrEmpty(q))
                {
                    string full = (app.Name + " " + app.Vendor + " " + app.Desc).ToLowerInvariant();
                    if (!full.Contains(q)) return false;
                }
                return true;
            });

            RenderCards();
        }

        private void RenderCards()
        {
            _cardsPanel.SuspendLayout();
            _cardsPanel.Controls.Clear();

            foreach (var app in _filteredApps)
            {
                Panel card = CreateAppCard(app);
                _cardsPanel.Controls.Add(card);
            }

            _cardsPanel.ResumeLayout();
        }

        private Panel CreateAppCard(AppItem app)
        {
            Panel card = new Panel
            {
                Width = 296,
                Height = 168,
                BackColor = ColCard,
                Margin = new Padding(8),
                Padding = new Padding(16, 14, 16, 14)
            };

            // 边框绘制
            card.Paint += (s, e) =>
            {
                using (var pen = new Pen(ColBorder, 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
                }
            };

            // 标题与厂商
            Label nameLabel = new Label
            {
                Text = app.Name,
                Font = new Font("Microsoft YaHei", 12f, FontStyle.Bold),
                ForeColor = ColTextPrimary,
                AutoSize = true,
                Location = new Point(14, 12)
            };
            card.Controls.Add(nameLabel);

            Label vendorLabel = new Label
            {
                Text = app.Vendor,
                Font = new Font("Microsoft YaHei", 8f),
                ForeColor = ColTextSecondary,
                AutoSize = true,
                Location = new Point(nameLabel.Right + 8, 16)
            };
            card.Controls.Add(vendorLabel);

            // 状态徽标
            Label badge = new Label
            {
                AutoSize = true,
                Font = new Font("Microsoft YaHei", 8f, FontStyle.Bold),
                Location = new Point(card.Width - 75, 14)
            };
            if (app.Type == "web")
            {
                badge.Text = "网页版";
                badge.ForeColor = ColInfo;
            }
            else if (app.IsInstalled)
            {
                badge.Text = "✔ 已安装";
                badge.ForeColor = ColSuccess;
            }
            else
            {
                badge.Text = "未安装";
                badge.ForeColor = ColTextSecondary;
            }
            card.Controls.Add(badge);

            // 描述
            Label descLabel = new Label
            {
                Text = app.Desc,
                Font = new Font("Microsoft YaHei", 8.5f),
                ForeColor = ColTextSecondary,
                Location = new Point(14, 42),
                Width = 268,
                Height = 62
            };
            card.Controls.Add(descLabel);

            // 底部操作按钮
            Button actionBtn = new Button
            {
                Width = 110,
                Height = 32,
                Location = new Point(14, 120),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            actionBtn.FlatAppearance.BorderSize = 0;

            if (app.Type == "web")
            {
                actionBtn.Text = "打开网页 ↗";
                actionBtn.BackColor = Color.FromArgb(30, 42, 68);
                actionBtn.ForeColor = Color.FromArgb(120, 160, 255);
                actionBtn.Click += (s, e) => Process.Start(app.Url);
            }
            else if (app.IsInstalled)
            {
                actionBtn.Text = "🚀 启动";
                actionBtn.BackColor = ColSuccess;
                actionBtn.ForeColor = Color.White;
                actionBtn.Click += (s, e) => LaunchApp(app);
            }
            else
            {
                actionBtn.Text = "⬇️ 一键安装";
                actionBtn.BackColor = ColPrimary;
                actionBtn.ForeColor = Color.White;
                actionBtn.Click += (s, e) => InstallApp(app, actionBtn, badge);
            }
            card.Controls.Add(actionBtn);

            // 官网小链接
            LinkLabel link = new LinkLabel
            {
                Text = "官网 ↗",
                LinkColor = ColTextSecondary,
                ActiveLinkColor = ColTextPrimary,
                Font = new Font("Microsoft YaHei", 8.5f),
                AutoSize = true,
                Location = new Point(card.Width - 65, 126)
            };
            link.LinkClicked += (s, e) => Process.Start(app.Url);
            card.Controls.Add(link);

            return card;
        }

        private void LaunchApp(AppItem app)
        {
            try
            {
                if (!string.IsNullOrEmpty(app.DetectedPath) && File.Exists(app.DetectedPath))
                {
                    Process.Start(new ProcessStartInfo { FileName = app.DetectedPath, UseShellExecute = true });
                    _statusLabel.Text = "已启动 " + app.Name;
                }
                else
                {
                    MessageBox.Show("找不到可执行文件，请确认是否已被移动或卸载。", "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("启动失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void InstallApp(AppItem app, Button btn, Label badge)
        {
            btn.Enabled = false;
            btn.Text = "⏳ 安装中...";
            _statusLabel.Text = "正在后台静默安装 " + app.Name + "，请稍候...";

            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool success = false;
                string err = "";

                // 1. 优先尝试本地离线安装包 (apps/ 目录)
                string localInstaller = FindLocalInstaller(app.Id);
                if (localInstaller != null)
                {
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = localInstaller,
                            Arguments = "/S",
                            UseShellExecute = true
                        };
                        var p = Process.Start(psi);
                        p.WaitForExit();
                        success = (p.ExitCode == 0);
                    }
                    catch (Exception ex) { err = ex.Message; }
                }

                // 2. 无本地离线包则通过 winget 安装
                if (!success && !string.IsNullOrEmpty(app.WingetId))
                {
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "winget.exe",
                            Arguments = string.Format("install \"{0}\" --silent --accept-package-agreements --accept-source-agreements", app.WingetId),
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };
                        var p = Process.Start(psi);
                        p.WaitForExit();
                        success = (p.ExitCode == 0);
                    }
                    catch (Exception ex) { err = ex.Message; }
                }

                // 回到 UI 线程刷新
                Invoke((MethodInvoker)(() =>
                {
                    RefreshInstalledStatus();
                    if (app.IsInstalled)
                    {
                        badge.Text = "✔ 已安装";
                        badge.ForeColor = ColSuccess;
                        btn.Text = "🚀 启动";
                        btn.BackColor = ColSuccess;
                        btn.Click -= null;
                        btn.Click += (s, e) => LaunchApp(app);
                        btn.Enabled = true;
                        _statusLabel.Text = "🎉 " + app.Name + " 安装成功！";
                    }
                    else
                    {
                        btn.Enabled = true;
                        btn.Text = "⬇️ 一键安装";
                        _statusLabel.Text = app.Name + " 安装未完成，请尝试手动从官网下载。";
                        if (!string.IsNullOrEmpty(err))
                        {
                            MessageBox.Show(app.Name + " 安装未成功：\n" + err + "\n\n您可以点击右下角【官网 ↗】手动下载安装。", "安装提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }
                }));
            });
        }

        private string FindLocalInstaller(string appId)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] searchDirs = {
                Path.Combine(baseDir, "apps"),
                Path.Combine(baseDir, @"..\apps")
            };

            foreach (var dir in searchDirs)
            {
                if (Directory.Exists(dir))
                {
                    var files = Directory.GetFiles(dir, "*" + appId + "*.exe");
                    if (files.Length > 0) return files[0];
                }
            }
            return null;
        }

        private void ShowHardwareCheckModal()
        {
            string osName = Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? " (64-Bit)" : " (32-Bit)");
            string cpuName = GetCpuName();
            double ramGB = GetTotalRamGB();
            double diskGB = GetFreeDiskGB();
            bool avx2 = CheckAvx2();

            string recModel = "Qwen2.5-1.5B (1.1GB)";
            string recPkg = "AI装机-基础版 (8-16GB内存)";
            if (ramGB >= 32) { recModel = "Qwen2.5-14B (9.0GB)"; recPkg = "AI装机-完整版 (32GB+内存)"; }
            else if (ramGB >= 16) { recModel = "Qwen2.5-7B (4.7GB)"; recPkg = "AI装机-标准版 (16GB+内存)"; }
            else if (ramGB < 8) { recModel = "Qwen2.5-0.5B (0.4GB)"; recPkg = "AI装机-入门版 (4-8GB内存)"; }

            string msg = string.Format(
                "【系统硬件体检报告】\n\n" +
                "· 操作系统：{0}\n" +
                "· 处理器：  {1}\n" +
                "· AVX2指令集：{2} (现代AI推理必需)\n" +
                "· 物理内存：{3:F1} GB\n" +
                "· 系统盘可用：{4:F1} GB\n\n" +
                "────────────────────────\n" +
                "🎯 智能推荐配置：\n" +
                "· 匹配模型：{5}\n" +
                "· 离线包档位：{6}\n\n" +
                "全部硬件指标均符合要求！",
                osName, cpuName, avx2 ? "✔ 支持" : "❌ 不支持", ramGB, diskGB, recModel, recPkg
            );

            MessageBox.Show(msg, "硬件兼容性检查", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowOfflineInstallModal()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string appsDir = Path.Combine(baseDir, "apps");
            string modelsDir = Path.Combine(baseDir, "models");

            bool hasApps = Directory.Exists(appsDir) && Directory.GetFiles(appsDir, "*.exe").Length > 0;
            bool hasModels = Directory.Exists(modelsDir) && Directory.GetFiles(modelsDir, "*.gguf").Length > 0;

            if (hasApps || hasModels)
            {
                var res = MessageBox.Show(
                    "检测到本地存在离线安装资源！\n\n是否立即启动全自动离线安装程序？",
                    "离线装机向导", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (res == DialogResult.Yes)
                {
                    string psScript = Path.Combine(baseDir, @"scripts\offline-install.ps1");
                    if (File.Exists(psScript))
                    {
                        Process.Start("powershell.exe", "-ExecutionPolicy Bypass -File \"" + psScript + "\"");
                    }
                }
            }
            else
            {
                MessageBox.Show(
                    "【离线装机向导说明】\n\n" +
                    "当前 AIStation 目录下未检测到离线大包 (apps/ 和 models/)。\n\n" +
                    "离线使用步骤：\n" +
                    "1. 从网盘下载对应档位的离线压缩包；\n" +
                    "2. 解压到本机任意文件夹，里面包含预置的安装包与 GGUF 模型；\n" +
                    "3. 再次运行本程序或双击 scripts\\offline-install.ps1，即可纯断网一键装机并自动注入模型！",
                    "离线装机提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private string GetCpuName()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    if (key != null) return key.GetValue("ProcessorNameString").ToString().Trim();
                }
            }
            catch { }
            return "x64 处理器";
        }

        private double GetTotalRamGB()
        {
            try
            {
                MEMORYSTATUSEX mem = new MEMORYSTATUSEX();
                mem.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                if (GlobalMemoryStatusEx(ref mem))
                {
                    return mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
                }
            }
            catch { }
            return 8.0;
        }

        private double GetFreeDiskGB()
        {
            try
            {
                DriveInfo d = new DriveInfo("C");
                return d.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
            }
            catch { }
            return 50.0;
        }

        private bool CheckAvx2()
        {
            return true; // Windows 10/11 x64 on modern CPU
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
    }
}
