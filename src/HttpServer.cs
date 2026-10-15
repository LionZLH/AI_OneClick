// 极简 HTTP/1.1 服务器，只监听 127.0.0.1。用 TcpListener 实现，无需管理员权限或 URL ACL。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace AIStation
{
    internal sealed class HttpRequest
    {
        public string Method;
        public string Path;
        public Dictionary<string, string> Query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public byte[] Body = new byte[0];

        public string Header(string name)
        {
            string v;
            return Headers.TryGetValue(name, out v) ? v : null;
        }

        public string Q(string name)
        {
            string v;
            return Query.TryGetValue(name, out v) ? v : null;
        }

        public Dictionary<string, object> JsonBody()
        {
            return Json.ParseObject(Encoding.UTF8.GetString(Body));
        }
    }

    internal sealed class HttpResponse
    {
        public int Status = 200;
        public string ContentType = "application/json; charset=utf-8";
        public byte[] Body = new byte[0];
        public string CacheControl = "no-store";

        public static HttpResponse JsonOf(object o)
        {
            return new HttpResponse { Body = Encoding.UTF8.GetBytes(Json.Serialize(o)) };
        }

        public static HttpResponse Error(int status, string message)
        {
            var d = new Dictionary<string, object>();
            d["error"] = message;
            var r = JsonOf(d);
            r.Status = status;
            return r;
        }
    }

    internal sealed class HttpServer
    {
        private readonly TcpListener _listener;
        private readonly Func<HttpRequest, HttpResponse> _handler;
        public int Port { get; private set; }

        public HttpServer(int port, Func<HttpRequest, HttpResponse> handler)
        {
            _handler = handler;
            _listener = new TcpListener(IPAddress.Loopback, port);
        }

        public void Start()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            var t = new Thread(AcceptLoop) { IsBackground = true, Name = "http" };
            t.Start();
            Log.Write("本地服务已启动：http://127.0.0.1:{0}/", Port);
        }

        private void AcceptLoop()
        {
            while (true)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); }
                catch (Exception ex)
                {
                    Log.Write("accept 失败: {0}", ex.Message);
                    Thread.Sleep(200);
                    continue;
                }
                ThreadPool.QueueUserWorkItem(_ => Handle(client));
            }
        }

        private void Handle(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 15000;
                    client.SendTimeout = 15000;
                    var stream = client.GetStream();
                    var req = Read(stream);
                    if (req == null) return;
                    HttpResponse res;
                    try { res = _handler(req); }
                    catch (Exception ex)
                    {
                        Log.Write("处理 {0} {1} 出错: {2}", req.Method, req.Path, ex);
                        res = HttpResponse.Error(500, ex.Message);
                    }
                    Write(stream, res ?? HttpResponse.Error(404, "not found"));
                }
                catch (IOException) { }
                catch (Exception ex) { Log.Write("连接处理失败: {0}", ex.Message); }
            }
        }

        private static HttpRequest Read(NetworkStream stream)
        {
            var header = new MemoryStream();
            int matched = 0;
            while (true)
            {
                int b = stream.ReadByte();
                if (b < 0) return null;
                header.WriteByte((byte)b);
                // 查找 \r\n\r\n
                if ((matched % 2 == 0 && b == '\r') || (matched % 2 == 1 && b == '\n')) matched++;
                else matched = b == '\r' ? 1 : 0;
                if (matched == 4) break;
                if (header.Length > 64 * 1024) return null;
            }
            var text = Encoding.UTF8.GetString(header.ToArray());
            var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var first = lines[0].Split(' ');
            if (first.Length < 2) return null;
            var req = new HttpRequest { Method = first[0].ToUpperInvariant() };
            var target = first[1];
            var qi = target.IndexOf('?');
            req.Path = Uri.UnescapeDataString(qi >= 0 ? target.Substring(0, qi) : target);
            if (qi >= 0)
            {
                foreach (var pair in target.Substring(qi + 1).Split('&'))
                {
                    if (pair.Length == 0) continue;
                    var eq = pair.IndexOf('=');
                    var k = Uri.UnescapeDataString((eq >= 0 ? pair.Substring(0, eq) : pair).Replace('+', ' '));
                    var v = eq >= 0 ? Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' ')) : "";
                    req.Query[k] = v;
                }
            }
            for (int i = 1; i < lines.Length; i++)
            {
                var c = lines[i].IndexOf(':');
                if (c <= 0) continue;
                req.Headers[lines[i].Substring(0, c).Trim()] = lines[i].Substring(c + 1).Trim();
            }
            int len;
            if (int.TryParse(req.Header("Content-Length"), NumberStyles.Integer, CultureInfo.InvariantCulture, out len) && len > 0)
            {
                if (len > 4 * 1024 * 1024) return null;
                var body = new byte[len];
                int read = 0;
                while (read < len)
                {
                    int n = stream.Read(body, read, len - read);
                    if (n <= 0) return null;
                    read += n;
                }
                req.Body = body;
            }
            return req;
        }

        private static void Write(NetworkStream stream, HttpResponse res)
        {
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(res.Status).Append(' ').Append(Reason(res.Status)).Append("\r\n");
            sb.Append("Content-Type: ").Append(res.ContentType).Append("\r\n");
            sb.Append("Content-Length: ").Append(res.Body.Length).Append("\r\n");
            sb.Append("Cache-Control: ").Append(res.CacheControl).Append("\r\n");
            sb.Append("X-Content-Type-Options: nosniff\r\n");
            sb.Append("Referrer-Policy: no-referrer\r\n");
            sb.Append("Connection: close\r\n\r\n");
            var head = Encoding.ASCII.GetBytes(sb.ToString());
            stream.Write(head, 0, head.Length);
            if (res.Body.Length > 0) stream.Write(res.Body, 0, res.Body.Length);
            stream.Flush();
        }

        private static string Reason(int status)
        {
            switch (status)
            {
                case 200: return "OK";
                case 400: return "Bad Request";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                default: return "Error";
            }
        }
    }
}
