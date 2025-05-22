using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace LocalServiceComponent
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
                    context.Response.OutputStream.Write(Encoding.UTF8.GetBytes(html),0,Encoding.UTF8.GetByteCount(html));
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
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static void Main()
        {

            ServiceBase[] ServicesToRun;
            ServicesToRun = new ServiceBase[]
            {
                new Service1()
            };
            ServiceBase.Run(ServicesToRun);

            InstallFFMpeg();

            var cams = new[]
       {
            new CameraStream {
                Name = "cam1",
                Url = "rtsp://admin:tech@9900@192.168.0.211:554/Streaming/Channels/201/",
                Route = "/cam1"
            },
            new CameraStream {
                Name = "cam2",
                Url = "rtsp://admin:tech@9900@192.168.0.211:554/Streaming/Channels/101/",
                Route = "/cam2"
            },
            new CameraStream {
                Name = "cam3",
                Url = "rtsp://admin:tech@9900@192.168.0.211:554/Streaming/Channels/301/",
                Route = "/cam3"
            },
             new CameraStream {
                Name = "cam4",
                Url = "rtsp://admin:tech@9900@192.168.0.211:554/Streaming/Channels/201/",
                Route = "/cam4"
            },
            new CameraStream {
                Name = "cam5",
                Url = "rtsp://admin:tech@9900@192.168.0.211:554/Streaming/Channels/101/",
                Route = "/cam5"
            },
        };

            HttpRequestHandler.StartHttpServer(8080);
            var wssv = new List<WebSocketServer>();
            int websocketPort = 9898;
            var ws = new WebSocketServer(websocketPort);
            foreach (var cam in cams)
            {
                StartFFmpegStream(cam);

                //var ws = new WebSocketServer(cam.Port);
                ws.AddWebSocketService<StreamSocket>(cam.Route, socket =>
                {
                    socket.Initialize(cam);
                });

                ws.Start();
                wssv.Add(ws);
            }

            
        }
        

        static void StartFFmpegStream(CameraStream cam)
        {
            // URL-encode the password and use TCP transport
            var encodedUrl = cam.Url;//.Replace("@", "%40");

            var ffmpegArgs = $"-rtsp_transport tcp -re -i \"{encodedUrl}\" " +
                             "-f mpegts -codec:v mpeg1video " +
                             "-q:v 5 -r 25 -bf 0 " +
                             "-s 1280x720 " +
                             "-loglevel warning " +
                             "-";

            cam.FfmpegProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = UtilityConstants.FFMPegPath,
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
              //  Console.WriteLine($"[FFmpeg] {cam.Name} process exited with code {cam.FfmpegProcess.ExitCode}");
                // Optional: Add restart logic here
            };

            try
            {
                cam.FfmpegProcess.Start();
                cam.FfmpegProcess.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
              //  Console.WriteLine($"[Error] Failed to start {cam.Name}: {ex.Message}");
            }
        }
        private static void InstallFFMpeg()
        {
            Task.Run(async () =>
            {
                try
                {
                    var file = UtilityConstants.GetFFMPegPath();
                    var exe = UtilityConstants.FFMPegPath = Path.Combine(file, "ffmpeg.exe");
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
            }).Wait();
        }
    }
}
