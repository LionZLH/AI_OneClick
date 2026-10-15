// 硬件检测：全部来自 Windows API / 注册表的真实读数，读不到就返回 null，不做假设。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AIStation
{
    internal static class Hardware
    {
        [StructLayout(LayoutKind.Sequential)]
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

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

        [DllImport("kernel32.dll")]
        private static extern bool IsProcessorFeaturePresent(uint feature);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        private const uint PF_AVX2_INSTRUCTIONS_AVAILABLE = 40;
        private const double GB = 1024.0 * 1024.0 * 1024.0;

        private static bool IsWindows
        {
            get { return Environment.OSVersion.Platform == PlatformID.Win32NT; }
        }

        public static Dictionary<string, object> Detect()
        {
            var d = new Dictionary<string, object>();
            var os = OsInfo();
            d["os"] = os;
            d["cpu"] = CpuName();
            d["cores"] = Environment.ProcessorCount;
            d["arch"] = NativeArch();
            d["ramGB"] = RamGB();
            d["avx2"] = Avx2((int)os["build"]);
            d["gpus"] = Gpus();
            d["disk"] = Disk();
            d["clr"] = Environment.Version.ToString();
            return d;
        }

        private static object RamGB()
        {
            if (IsWindows)
            {
                try
                {
                    var m = new MEMORYSTATUSEX();
                    m.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                    if (GlobalMemoryStatusEx(ref m)) return Math.Round(m.ullTotalPhys / GB, 1);
                }
                catch (Exception ex) { Log.Write("RAM 检测失败: {0}", ex.Message); }
                return null;
            }
            // 非 Windows（仅开发调试用）
            try
            {
                foreach (var line in File.ReadAllLines("/proc/meminfo"))
                {
                    if (!line.StartsWith("MemTotal:")) continue;
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    return Math.Round(double.Parse(parts[1], CultureInfo.InvariantCulture) * 1024 / GB, 1);
                }
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>true / false / null（系统太旧，无法通过 API 确认）。</summary>
        private static object Avx2(int build)
        {
            if (IsWindows)
            {
                // PF_AVX2_INSTRUCTIONS_AVAILABLE 从 Windows 10 2004 (19041) 起才可靠
                if (build < 19041) return null;
                try { return IsProcessorFeaturePresent(PF_AVX2_INSTRUCTIONS_AVAILABLE); }
                catch (Exception) { return null; }
            }
            try
            {
                var text = File.ReadAllText("/proc/cpuinfo");
                return text.Contains(" avx2");
            }
            catch (Exception) { return null; }
        }

        private static string CpuName()
        {
            if (IsWindows)
            {
                try
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                    {
                        if (key != null)
                        {
                            var v = key.GetValue("ProcessorNameString") as string;
                            if (!string.IsNullOrEmpty(v)) return v.Trim();
                        }
                    }
                }
                catch (Exception) { }
                return null;
            }
            try
            {
                foreach (var line in File.ReadAllLines("/proc/cpuinfo"))
                    if (line.StartsWith("model name")) return line.Substring(line.IndexOf(':') + 1).Trim();
            }
            catch (Exception) { }
            return null;
        }

        private static string NativeArch()
        {
            if (IsWindows)
            {
                try
                {
                    ushort proc, native;
                    if (IsWow64Process2(GetCurrentProcess(), out proc, out native))
                    {
                        if (native == 0xAA64) return "arm64";
                        if (native == 0x8664) return "x64";
                        if (native == 0x014C) return "x86";
                    }
                }
                catch (Exception) { } // Windows 10 1709 之前没有这个 API
                var a = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") ??
                        Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "";
                if (a.Equals("ARM64", StringComparison.OrdinalIgnoreCase)) return "arm64";
                if (a.Equals("AMD64", StringComparison.OrdinalIgnoreCase)) return "x64";
                if (a.Equals("x86", StringComparison.OrdinalIgnoreCase)) return "x86";
            }
            return Environment.Is64BitOperatingSystem ? "x64" : "x86";
        }

        private static Dictionary<string, object> OsInfo()
        {
            var d = new Dictionary<string, object>();
            d["name"] = Environment.OSVersion.VersionString;
            d["version"] = null;
            d["build"] = Environment.OSVersion.Version.Build;
            d["windows"] = IsWindows;
            if (!IsWindows) return d;
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key == null) return d;
                    var product = key.GetValue("ProductName") as string ?? "Windows";
                    int build;
                    if (!int.TryParse(key.GetValue("CurrentBuildNumber") as string, out build))
                        build = Environment.OSVersion.Version.Build;
                    // Windows 11 的 ProductName 仍写着 "Windows 10"
                    if (build >= 22000) product = product.Replace("Windows 10", "Windows 11");
                    var displayVersion = key.GetValue("DisplayVersion") as string ?? key.GetValue("ReleaseId") as string;
                    var ubr = key.GetValue("UBR");
                    d["name"] = product;
                    d["version"] = displayVersion;
                    d["build"] = build;
                    d["buildFull"] = ubr == null ? build.ToString(CultureInfo.InvariantCulture) : build + "." + ubr;
                }
            }
            catch (Exception ex) { Log.Write("系统版本检测失败: {0}", ex.Message); }
            return d;
        }

        private static List<object> Gpus()
        {
            var list = new List<object>();
            if (!IsWindows) return list;
            var seen = new HashSet<string>();
            try
            {
                using (var cls = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (cls == null) return list;
                    foreach (var sub in cls.GetSubKeyNames())
                    {
                        if (sub.Length != 4) continue; // 0000、0001 …
                        try
                        {
                            using (var k = cls.OpenSubKey(sub))
                            {
                                if (k == null) continue;
                                var name = k.GetValue("DriverDesc") as string;
                                if (string.IsNullOrEmpty(name)) continue;
                                if (name.IndexOf("Basic Display", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                                if (name.IndexOf("Basic Render", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                                if (name.IndexOf("Remote", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                                if (name.IndexOf("Hyper-V", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                                if (!seen.Add(name)) continue;
                                var g = new Dictionary<string, object>();
                                g["name"] = name.Trim();
                                g["vramGB"] = VramGB(k);
                                list.Add(g);
                            }
                        }
                        catch (Exception) { } // 部分子键无权限
                    }
                }
            }
            catch (Exception ex) { Log.Write("显卡检测失败: {0}", ex.Message); }
            return list;
        }

        private static object VramGB(RegistryKey k)
        {
            long bytes = 0;
            var q = k.GetValue("HardwareInformation.qwMemorySize");
            if (q is long) bytes = (long)q;
            else if (q is byte[] && ((byte[])q).Length >= 8) bytes = BitConverter.ToInt64((byte[])q, 0);
            if (bytes <= 0)
            {
                var m = k.GetValue("HardwareInformation.MemorySize");
                if (m is int) bytes = (uint)(int)m;
                else if (m is long) bytes = (long)m;
                else if (m is byte[] && ((byte[])m).Length >= 4) bytes = BitConverter.ToUInt32((byte[])m, 0);
            }
            if (bytes <= 0) return null;
            return Math.Round(bytes / GB, 1);
        }

        private static Dictionary<string, object> Disk()
        {
            var d = new Dictionary<string, object>();
            try
            {
                var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System));
                if (string.IsNullOrEmpty(root)) root = Path.GetPathRoot(Paths.LocalAppData) ?? "/";
                var di = new DriveInfo(root);
                d["drive"] = root.TrimEnd('\\');
                d["freeGB"] = Math.Round(di.AvailableFreeSpace / GB, 1);
                d["totalGB"] = Math.Round(di.TotalSize / GB, 1);
            }
            catch (Exception ex) { Log.Write("磁盘检测失败: {0}", ex.Message); }
            return d;
        }
    }
}
