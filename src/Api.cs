// 接口路由。所有 /api/ 请求都必须带启动时随机生成的令牌，并校验 Host，防止网页跨站调用本机接口。
using System;
using System.Collections.Generic;
using System.Text;

namespace AIStation
{
    internal sealed class Api
    {
        private readonly Core _core;
        public Action ShowWindowRequested;

        public Api(Core core)
        {
            _core = core;
        }

        public HttpResponse Handle(HttpRequest req)
        {
            var host = req.Header("Host") ?? "";
            if (host != "127.0.0.1:" + _core.Port && host != "localhost:" + _core.Port)
                return HttpResponse.Error(403, "bad host");

            if (!req.Path.StartsWith("/api/")) return Static(req);

            var token = req.Header("X-AIStation-Token") ?? req.Q("t");
            if (token != _core.Token) return HttpResponse.Error(403, "bad token");
            if (req.Method == "POST" && req.Header("X-AIStation-Token") == null)
                return HttpResponse.Error(403, "token header required");

            var path = req.Path.Substring(5);
            if (req.Method == "GET")
            {
                switch (path)
                {
                    case "state": return HttpResponse.JsonOf(_core.State());
                    case "jobs": return HttpResponse.JsonOf(JobsSnapshot());
                    case "icon": return Icon(req.Q("id"));
                    case "show":
                        if (ShowWindowRequested != null) ShowWindowRequested();
                        return Ok();
                }
                return HttpResponse.Error(404, "not found");
            }
            if (req.Method != "POST") return HttpResponse.Error(405, "method not allowed");

            var body = req.JsonBody();
            switch (path)
            {
                case "ping":
                    _core.LastPing = Time.NowMs();
                    return Ok();
                case "refresh":
                    _core.RefreshInstalledAsync();
                    return Ok();
                case "network":
                    _core.ProbeNetwork();
                    lock (_core.Sync) return HttpResponse.JsonOf(_core.NetworkInfo);
                case "jobs": return CreateJobs(body);
                case "jobs/cancel":
                    _core.Jobs.Cancel((int)Json.Num(body, "id", -1));
                    return HttpResponse.JsonOf(JobsSnapshot());
                case "jobs/clear":
                    _core.Jobs.ClearFinished();
                    return HttpResponse.JsonOf(JobsSnapshot());
                case "launch": return Result(_core.Launch(Json.Str(body, "id")));
                case "open": return Open(body);
                case "profile":
                    _core.PatchProfile(body);
                    lock (_core.Sync) return HttpResponse.JsonOf(_core.Profile);
                case "web-shortcut":
                {
                    var app = _core.FindApp(Json.Str(body, "id"));
                    if (app == null || app.Kind != "web") return HttpResponse.Error(400, "不是网页应用");
                    _core.SetWebShortcut(app.Id, Json.Bool(body, "add"));
                    lock (_core.Sync) return HttpResponse.JsonOf(_core.Profile);
                }
                case "autostart":
                    return Result(Autostart.Set(Json.Bool(body, "enabled")));
                case "open-data-dir":
                    return Result(OpenFolder(Paths.DataDir));
            }
            return HttpResponse.Error(404, "not found");
        }

        private Dictionary<string, object> JobsSnapshot()
        {
            var d = new Dictionary<string, object>();
            d["jobs"] = _core.Jobs.ToJson();
            d["installed"] = _core.InstalledJson();
            lock (_core.Sync) d["scanning"] = _core.Scanning;
            return d;
        }

        private static readonly HashSet<string> Actions = new HashSet<string>
        {
            "install", "upgrade", "uninstall", "pull-model", "import-model"
        };

        private HttpResponse CreateJobs(Dictionary<string, object> body)
        {
            var created = new List<object>();
            foreach (var o in Json.Arr(body, "jobs"))
            {
                var j = o as Dictionary<string, object>;
                if (j == null) continue;
                var action = Json.Str(j, "action");
                var appId = Json.Str(j, "appId");
                if (!Actions.Contains(action)) return HttpResponse.Error(400, "unknown action");
                if (action != "pull-model" && action != "import-model" && _core.FindApp(appId) == null)
                    return HttpResponse.Error(400, "unknown app");
                var job = _core.Jobs.Enqueue(appId, action, Json.Str(j, "arg"), Json.Str(j, "target"));
                created.Add(job.Id);
            }
            var d = JobsSnapshot();
            d["created"] = created;
            return HttpResponse.JsonOf(d);
        }

        private HttpResponse Open(Dictionary<string, object> body)
        {
            var target = Json.Str(body, "target");
            if (target == "winget-store") return Result(Core.OpenUrl("ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1"));
            var app = _core.FindApp(Json.Str(body, "id"));
            if (app == null) return HttpResponse.Error(400, "unknown app");
            string url = app.Homepage;
            if (target == "web") url = app.WebUrl ?? app.Homepage;
            return Result(Core.OpenUrl(url));
        }

        private static string OpenFolder(string dir)
        {
            try
            {
                Paths.EnsureDir(dir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        private HttpResponse Icon(string id)
        {
            var png = id == null ? null : _core.IconPng(id);
            if (png == null) return HttpResponse.Error(404, "no icon");
            return new HttpResponse { ContentType = "image/png", Body = png, CacheControl = "max-age=3600" };
        }

        private static HttpResponse Ok()
        {
            var d = new Dictionary<string, object>();
            d["ok"] = true;
            return HttpResponse.JsonOf(d);
        }

        private static HttpResponse Result(string error)
        {
            if (error == null) return Ok();
            var d = new Dictionary<string, object>();
            d["ok"] = false;
            d["error"] = error;
            return HttpResponse.JsonOf(d);
        }

        private static readonly Dictionary<string, string> Mime = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".html", "text/html; charset=utf-8" },
            { ".js", "text/javascript; charset=utf-8" },
            { ".css", "text/css; charset=utf-8" },
            { ".json", "application/json; charset=utf-8" },
            { ".svg", "image/svg+xml" },
            { ".png", "image/png" },
            { ".ico", "image/x-icon" }
        };

        private HttpResponse Static(HttpRequest req)
        {
            if (req.Method != "GET") return HttpResponse.Error(405, "method not allowed");
            var name = req.Path == "/" ? "index.html" : req.Path.TrimStart('/');
            var bytes = _core.ReadUiFile(name);
            if (bytes == null) return HttpResponse.Error(404, "not found");
            string type;
            if (!Mime.TryGetValue(System.IO.Path.GetExtension(name), out type)) type = "application/octet-stream";
            return new HttpResponse { ContentType = type, Body = bytes };
        }
    }
}
