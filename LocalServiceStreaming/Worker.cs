using WebSocketSharp.Server;
using System.Net;
using System.Diagnostics;
using System.Text;
using WebSocketSharp;
namespace LocalServiceStreaming
{
    public class CameraStream
    {
        public string Name { get; set; }
        public string Url { get; set; }
        //public int Port { get; set; }
        public string Route { get; set; }
        public Process FfmpegProcess { get; set; }
    }

    public class StreamSocket : WebSocketBehavior
    {
        private CameraStream _camera;

        public void Initialize(CameraStream camera)
        {
            _camera = camera;
        }

        protected override void OnOpen()
        {
            Console.WriteLine($"Client connected to {_camera.Name}");
            Task.Run(() => PipeFfmpegToWebSocket());
        }

        private async Task PipeFfmpegToWebSocket()
        {
            var buffer = new byte[8192];
            int bytesRead;

            try
            {
                while ((bytesRead = await _camera.FfmpegProcess.StandardOutput.BaseStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    if (State == WebSocketState.Open)
                    {
                        Send(buffer.Take(bytesRead).ToArray());
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error piping {_camera.Name} stream: {ex.Message}");
            }
        }

        protected override void OnClose(CloseEventArgs e)
        {
            Console.WriteLine($"Client disconnected from {_camera.Name}");
        }
    }

    public class HttpRequestHandler : WebSocketBehavior
    {
        protected override void OnMessage(MessageEventArgs e)
        {
            // Not used for HTTP requests
        }
       
        // Add this to your Program.cs
        public static void StartHttpServer(int port)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://*:{port}/");
            listener.Start();

            Task.Run(() =>
            {
                while (true)
                {
                    var context = listener.GetContext();
                    ProcessHttpRequest(context);
                }
            });
        }

        private static void ProcessHttpRequest(HttpListenerContext context)
        {
            try
            {
                var path = context.Request.Url.AbsolutePath;

                if (path == "/" || path == "/index.html")
                {
                    var html = File.ReadAllText("stream-viewer.html");
                    context.Response.ContentType = "text/html";
                    context.Response.ContentEncoding = Encoding.UTF8;
                    context.Response.OutputStream.Write(Encoding.UTF8.GetBytes(html), 0, Encoding.UTF8.GetByteCount(html));
                }
                else if (path == "/jsmpeg.min.js")
                {
                    var js = File.ReadAllText("jsmpeg.min.js");
                    context.Response.ContentType = "application/javascript";
                    context.Response.OutputStream.Write(Encoding.UTF8.GetBytes(js), 0, Encoding.UTF8.GetByteCount(js));
                }
                else
                {
                    context.Response.StatusCode = 404;
                }
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                context.Response.OutputStream.Write(Encoding.UTF8.GetBytes(ex.Message), 0, Encoding.UTF8.GetByteCount(ex.Message));
            }
            finally
            {
                context.Response.Close();
            }
        }
    }

    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private WebSocketServer _webSocketServer;
        private readonly List<CameraStream> _cams;

        public Worker(ILogger<Worker> logger, List<CameraStream> cams)
        {
            _logger = logger;
            _cams = cams;
        }
        private async Task InstallFFMpeg()
        {
            
                try
                {
                    var file = ConstantVariable.GetFFMPegPath();
                    var exe = ConstantVariable.FFMPegPath = Path.Combine(file, "ffmpeg.exe");
                    if (File.Exists(exe))
                        return;
                    using (HttpClient client = new HttpClient())
                    using (var response = await client.GetAsync("https://www.dropbox.com/scl/fi/obk7dnwjsm903dy04secd/ffmpeg.exe?rlkey=fow2d3pgdm14oc8nyvz6hb33o&st=zmr46mwi&dl=1"))
                    using (var fs = new FileStream(exe, FileMode.Create))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                }
                catch (Exception ex)
                {
                }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
               await InstallFFMpeg();
                var cams = new[]
                {
                    new CameraStream {
                        Name = "cam1",
                        Url = "rtsp://admin:tech@9900@106.51.129.154:554/Streaming/Channels/202/",
                        Route = "/cam1"
                    },
                    new CameraStream {
                        Name = "cam2",
                        Url = "rtsp://admin:tech@9900@106.51.129.154:554/Streaming/Channels/101/",
                        Route = "/cam2"
                    },
                    new CameraStream {
                        Name = "cam3",
                        Url = "rtsp://admin:tech@9900@106.51.129.154:554/Streaming/Channels/302/",
                        Route = "/cam3"
                    },
                     new CameraStream {
                        Name = "cam4",
                        Url = "rtsp://admin:tech@9900@106.51.129.154:554/Streaming/Channels/202/",
                        Route = "/cam4"
                    },
                    new CameraStream {
                        Name = "cam5",
                        Url = "rtsp://admin:tech@9900@106.51.129.154:554/Streaming/Channels/101/",
                        Route = "/cam5"
                    },
                    new CameraStream {
                        Name = "cam6",
                        Url = "rtsp://admin:tech@9900@106.51.129.154:554/Streaming/Channels/302/",
                        Route = "/cam6"
                    },
                };

                _cams.AddRange(cams);
                // Start HTTP server
                HttpRequestHandler.StartHttpServer(8080);
                _logger.LogInformation("HTTP Server started on port 8080");

                // Start WebSocket server
                int websocketPort = 9898;
                _webSocketServer = new WebSocketServer(websocketPort);

                foreach (var cam in _cams)
                {
                    StartFFmpegStream(cam);

                    _webSocketServer.AddWebSocketService<StreamSocket>(cam.Route, socket =>
                    {
                        socket.OriginValidator = origin =>
                        {
                            // Allow all origins (⚠️ only do this in trusted environments)
                            return true;
                        };
                        socket.Initialize(cam);
                    });
                    _logger.LogInformation($"Started {cam.Name} on ws://localhost:{websocketPort}{cam.Route}");
                }

                _webSocketServer.Start();
                _logger.LogInformation($"WebSocket Server started on port {websocketPort}");

                // Keep the service running
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(1000, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting servers");
                throw;
            }
        }
        static void StartFFmpegStream(CameraStream cam)
        {
            // URL-encode the password and use TCP transport
            var encodedUrl = cam.Url;

            var ffmpegArgs = $"-rtsp_transport tcp -re -i \"{encodedUrl}\" " +
                              "-f mpegts -codec:v mpeg1video " +
                              "-q:v 5 -r 25 -bf 0 " +
                              "-s 1280x720 " +
                              "-loglevel warning " +
                              "-";

            //var ffmpegArgs = $"-rtsp_transport tcp -re -i \"{encodedUrl}\" " +
            //                 "-f mpegts -codec:v mpeg1video " +
            //                 "-q:v 1 -r 30 -bf 2 " +
            //                 "-g 60 -b:v 5000k -maxrate 5000k -bufsize 10000k " +
            //                 "-s 1920x1080 " +
            //                 "-preset veryfast " +
            //                 "-loglevel warning -";


            //var ffmpegArgs = $"-rtsp_transport tcp -re -i \"{encodedUrl}\" " +
            //     "-f mpegts -codec:v mpeg1video -q:v 6 -r 20 -bf 0 -s 1280x720 -threads 1 -loglevel warning -";

            //var ffmpegArgs = $"-rtsp_transport tcp -re -i \"{encodedUrl}\" " +
            //             "-f mpegts -codec:v mpeg1video -q:v 2 -r 25 -bf 0 -s 1280x720 -threads 2 -loglevel error -";

            //var ffmpegArgs = $"-rtsp_transport tcp -re -i \"{encodedUrl}\" " +
            //             "-f mpegts -codec:v mpeg1video -q:v 2 -r 25 -bf 0 -s 1920x1080 -loglevel error -";
            //var ffmpegArgs = $"-rtsp_transport tcp -i \"{encodedUrl}\" " +
            //        "-f mpegts -codec:v h264_nvenc -preset fast -b:v 2M " +
            //        "-r 15 -s 640x360 -loglevel warning -";

            cam.FfmpegProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ConstantVariable.FFMPegPath,
                    Arguments = ffmpegArgs,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            cam.FfmpegProcess.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data) &&
                    !e.Data.Contains("deprecated pixel format") &&
                    !e.Data.Contains("Last message repeated"))
                    Console.WriteLine($"[FFmpeg] {cam.Name}: {e.Data}");
            };

            cam.FfmpegProcess.Exited += (sender, e) =>
            {
                Console.WriteLine($"[FFmpeg] {cam.Name} process exited with code {cam.FfmpegProcess.ExitCode}");
                // Optional: Add restart logic here
            };

            try
            {
                cam.FfmpegProcess.Start();
                cam.FfmpegProcess.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] Failed to start {cam.Name}: {ex.Message}");
            }
        }
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping servers...");

            // Correctly stop the WebSocketServer
            if (_webSocketServer != null && _webSocketServer.IsListening)
            {
                _webSocketServer.Stop();
            }
            foreach (var cam in _cams)
            {
                try
                {
                    cam.FfmpegProcess?.Kill();
                    Console.WriteLine($"Stopped {cam.Name}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error stopping {cam.Name}: {ex.Message}");
                }
            }
            await base.StopAsync(cancellationToken);
        }
    }
}
