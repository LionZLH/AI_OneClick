// 通用工具：JSON、日志、路径。
// 注意：本项目用 Windows 自带的 csc.exe（仅支持 C# 5）编译，请勿使用 $"" 字符串插值、?.、nameof 等新语法。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace AIStation
{
    internal static class Json
    {
        private static JavaScriptSerializer Make()
        {
            var s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            s.RecursionLimit = 256;
            return s;
        }

        public static string Serialize(object o)
        {
            return Make().Serialize(o);
        }

        public static object Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            return Make().DeserializeObject(text);
        }

        public static Dictionary<string, object> ParseObject(string text)
        {
            try
            {
                var d = Parse(text) as Dictionary<string, object>;
                return d ?? new Dictionary<string, object>();
            }
            catch (Exception)
            {
                return new Dictionary<string, object>();
            }
        }

        public static object Get(IDictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v)) return v;
            return null;
        }

        public static string Str(IDictionary<string, object> d, string key)
        {
            var v = Get(d, key);
            return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static Dictionary<string, object> Obj(IDictionary<string, object> d, string key)
        {
            return Get(d, key) as Dictionary<string, object>;
        }

        public static List<object> Arr(IDictionary<string, object> d, string key)
        {
            var v = Get(d, key);
            var list = new List<object>();
            var e = v as IEnumerable;
            if (e == null || v is string) return list;
            foreach (var x in e) list.Add(x);
            return list;
        }

        public static List<string> StrList(IDictionary<string, object> d, string key)
        {
            var list = new List<string>();
            foreach (var x in Arr(d, key)) if (x != null) list.Add(Convert.ToString(x, CultureInfo.InvariantCulture));
            return list;
        }

        public static bool Bool(IDictionary<string, object> d, string key)
        {
            var v = Get(d, key);
            return v is bool && (bool)v;
        }

        public static double Num(IDictionary<string, object> d, string key, double fallback)
        {
            var v = Get(d, key);
            if (v == null) return fallback;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }
            catch (Exception) { return fallback; }
        }
    }

    internal static class Log
    {
        private static readonly object Sync = new object();
        public static string FilePath;

        public static void Write(string format, params object[] args)
        {
            string line;
            try { line = args.Length > 0 ? string.Format(CultureInfo.InvariantCulture, format, args) : format; }
            catch (FormatException) { line = format; }
            line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + line;
            lock (Sync)
            {
                try { Console.Error.WriteLine(line); } catch (Exception) { }
                if (FilePath == null) return;
                try
                {
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 2 * 1024 * 1024) fi.Delete();
                    File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch (Exception) { }
            }
        }
    }

    internal static class Paths
    {
        /// <summary>程序所在目录（离线包的 apps/、models/ 也从这里找）。</summary>
        public static string BaseDir
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static string DataDir;

        public static string LocalAppData
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); }
        }

        public static string UserProfile
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); }
        }

        public static string ExpandEnv(string path)
        {
            return string.IsNullOrEmpty(path) ? path : Environment.ExpandEnvironmentVariables(path);
        }

        public static void EnsureDir(string dir)
        {
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
    }

    internal static class Time
    {
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static long NowMs()
        {
            return (long)(DateTime.UtcNow - Epoch).TotalMilliseconds;
        }
    }
}
