using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SpotifyWPF.Service
{
    public static class WebPlaybackHost
    {
        private static HttpListener? _listener;
        private static int _port = 0;
        private static CancellationTokenSource? _cts;

        public static int Port => _port;

        public static string? Url => _port > 0 ? $"http://127.0.0.1:{_port}/player.html" : null;

        // Start the listener on a free port (tries a few fallback ports)
        public static void Start()
        {
            if (_listener != null) return; // already started

            var portsToTry = new int[] { 50723, 50724, 50725, 50800, 50801 };
            foreach (var p in portsToTry)
            {
                if (!IsPortAvailable(p)) continue;
                try
                {
                    _listener = new HttpListener();
                    _listener.Prefixes.Add($"http://127.0.0.1:{p}/");
                    _listener.Start();
                    _port = p;
                    break;
                }
                catch
                {
                    _listener = null;
                }
            }

            if (_listener == null)
            {
                // Let OS pick a free port using HttpListener on default prefix; try random high port
                int attempts = 0;
                while (_listener == null && attempts++ < 10)
                {
                    var p = new Random().Next(20000, 60000);
                    try
                    {
                        _listener = new HttpListener();
                        _listener.Prefixes.Add($"http://127.0.0.1:{p}/");
                        _listener.Start();
                        _port = p;
                        break;
                    }
                    catch { _listener = null; }
                }
            }

            if (_listener == null)
            {
                // cannot start host
                return;
            }

            _cts = new CancellationTokenSource();
            Task.Run(() => RunLoop(_cts.Token));
        }

        public static void Stop()
        {
            try
            {
                _cts?.Cancel();
            }
            catch { }
            try
            {
                _listener?.Stop();
                _listener = null;
            }
            catch { }
            _port = 0;
            _cts = null;
        }

        private static bool IsPortAvailable(int port)
        {
            try
            {
                var tcp = new System.Net.Sockets.TcpListener(IPAddress.Loopback, port);
                tcp.Start();
                tcp.Stop();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async Task RunLoop(CancellationToken token)
        {
            try
            {
                while (_listener != null && _listener.IsListening && !token.IsCancellationRequested)
                {
                    var ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                    _ = Task.Run(() => HandleRequest(ctx), token);
                }
            }
            catch (Exception) { /* ignore */ }
        }

        private static void HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                var req = ctx.Request;
                var path = req.Url?.AbsolutePath ?? "/";
                if (path.EndsWith("/")) path += "player.html";

                var root = AppDomain.CurrentDomain.BaseDirectory;
                // Assets folder is at project path 'Assets' relative to executables
                var assetsDir = Path.Combine(root, "Assets");
                var filePath = Path.Combine(assetsDir, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(filePath))
                {
                    ctx.Response.StatusCode = 404;
                    var notFound = Encoding.UTF8.GetBytes("Not Found");
                    ctx.Response.OutputStream.Write(notFound, 0, notFound.Length);
                    ctx.Response.Close();
                    return;
                }

                var content = File.ReadAllBytes(filePath);
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentLength64 = content.Length;
                ctx.Response.ContentType = GetContentType(filePath);
                ctx.Response.OutputStream.Write(content, 0, content.Length);
                ctx.Response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                try
                {
                    ctx.Response.StatusCode = 500;
                    var err = Encoding.UTF8.GetBytes("Internal Server Error: " + ex.Message);
                    ctx.Response.OutputStream.Write(err, 0, err.Length);
                    ctx.Response.Close();
                }
                catch { }
            }
        }

        private static string GetContentType(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".html" => "text/html",
                ".js" => "application/javascript",
                ".css" => "text/css",
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                _ => "application/octet-stream",
            };
        }
    }
}