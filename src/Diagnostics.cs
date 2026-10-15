// 一键导出问题报告：把电脑信息、软件识别结果、任务记录和日志打包成 zip 放到桌面，方便发给技术支持。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace AIStation
{
    internal static class Diagnostics
    {
        public static string Export(Core core, out string path)
        {
            path = null;
            try
            {
                var dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Paths.DataDir;
                var name = "AIStation-问题报告-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".zip";
                path = Path.Combine(dir, name);

                var report = core.State();
                report.Remove("catalog"); // 目录是内置的，不需要
                report["generatedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                report["exePath"] = Portable.ExePath;
                report["dataDir"] = Paths.DataDir;
                report["registryEntries"] = Detector.ScanRegistry().Count;

                using (var fs = File.Create(path))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    AddText(zip, "说明.txt",
                        "这是 AI Station 的问题报告，用于排查安装、识别或启动问题。\r\n" +
                        "包含：电脑硬件和系统信息、AI 软件的识别结果、安装任务记录、AI Station 运行日志。\r\n" +
                        "不包含：任何账号、密码或聊天内容。\r\n" +
                        "文件路径中可能含有你的 Windows 用户名。\r\n");
                    AddText(zip, "report.json", Json.Serialize(report));
                    var log = ReadShared(Log.FilePath);
                    if (log != null) AddText(zip, "aistation.log", log);
                    var winget = WingetInfo(core);
                    if (winget != null) AddText(zip, "winget.txt", winget);
                }
                Log.Write("已导出问题报告：{0}", path);
                try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch (Exception) { }
                return null;
            }
            catch (Exception ex)
            {
                Log.Write("导出问题报告失败: {0}", ex);
                return "导出失败：" + ex.Message;
            }
        }

        private static void AddText(ZipArchive zip, string name, string text)
        {
            var entry = zip.CreateEntry(name);
            using (var s = entry.Open())
            {
                var bytes = new UTF8Encoding(true).GetBytes(text);
                s.Write(bytes, 0, bytes.Length);
            }
        }

        private static string ReadShared(string file)
        {
            try
            {
                if (string.IsNullOrEmpty(file) || !File.Exists(file)) return null;
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(fs, Encoding.UTF8))
                    return r.ReadToEnd();
            }
            catch (Exception) { return null; }
        }

        private static string WingetInfo(Core core)
        {
            if (core.WingetVersion == null) return "winget 不可用";
            try
            {
                var psi = new ProcessStartInfo(core.WingetPath, "--info")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using (var p = Process.Start(psi))
                {
                    var output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(20000);
                    return output;
                }
            }
            catch (Exception ex) { return "winget --info 失败：" + ex.Message; }
        }
    }
}
