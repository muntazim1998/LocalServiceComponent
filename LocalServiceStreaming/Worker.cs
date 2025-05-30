using LocalServiceStreaming.Models;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using WebSocketSharp;
using WebSocketSharp.Server;
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


    #region System Monitoring Socket
    public class MonitoringSocket : WebSocketBehavior
    {
        private Timer _timer;
        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        private static readonly PerformanceCounter CpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        private static readonly PerformanceCounter RamCounter = new PerformanceCounter("Memory", "Available MBytes");
        private static readonly PerformanceCounter DiskCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total");
        private static string TotalMemory = GetTotalMemory();
        protected override void OnOpen()
        {
            _logger.Info("Client connected to system monitoring");
            _timer = new Timer(SendSystemInfo, null, 0, 5000); // every 5 seconds
        }

        private void SendSystemInfo(object state)
        {
            try
            {
                if (State == WebSocketState.Open)
                {
                    var info = GetSystemInfo();
                    var json = JsonConvert.SerializeObject(info);
                    _logger.Info($"Sending system info: {json}");
                    Send(json);
                }
            }
            catch (Exception ex)
            {
            }
        }

        public object GetSystemInfo()
        {
            var cpuUsage = GetCpuUsage();
            var totalRam = GetMemoryUsage();
            var ffmpegMemoryusage = GetProcessMemoryUsage("ffmpeg");
            var ffmpegcpu_usage = GetProcessCpuUsage("ffmpeg");
            return new { totalcpu = cpuUsage, totalram = totalRam , ffmpegMemoryUsage= ffmpegMemoryusage, ffmpegcpuUsage = ffmpegcpu_usage };
        }
        private static string GetMemoryUsage()
        {
            try
            {
                if (string.IsNullOrEmpty(TotalMemory))
                    TotalMemory = GetTotalMemory();

                var total = float.Parse(TotalMemory);
                var available = GetAvailableMemory();
                var usage = ((total - available) / total) * 100;

                return $"{(int)usage}%";
            }
            catch
            {
                return "20%";
            }
        }

        private static string GetCpuUsage()
        {
            float cpuUsage = 0;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                CpuCounter.NextValue(); // warm-up
                Thread.Sleep(500);
                cpuUsage = CpuCounter.NextValue();
            }
            else
            {
                try
                {
                    var lines = File.ReadAllLines("/proc/stat");
                    var cpuLine = lines.First(l => l.StartsWith("cpu "));
                    var values = cpuLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Skip(1)
                                        .Select(ulong.Parse)
                                        .ToArray();

                    var idle = values[3];
                    var total = values.Aggregate((a, b) => a + b);

                    cpuUsage = 100.0f - (idle * 100.0f / total);
                }
                catch
                {
                    return "10%";
                }
            }
            return $"{(int)cpuUsage}%";
        }

        public static string GetProcessCpuUsage(string processName, int intervalMs = 1000)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                if (processes.Length == 0)
                    return "0%";
                var totalCpuTimeStart = processes.Sum(p => p.TotalProcessorTime.TotalMilliseconds);
                var stopwatch = Stopwatch.StartNew();
                Thread.Sleep(intervalMs); // Wait a bit to measure CPU usage
                stopwatch.Stop();

                // Refresh processes
                processes = Process.GetProcessesByName(processName);
                var totalCpuTimeEnd = processes.Sum(p => p.TotalProcessorTime.TotalMilliseconds);

                var cpuUsedMs = totalCpuTimeEnd - totalCpuTimeStart;
                var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * stopwatch.ElapsedMilliseconds) * 100;

                return $"{(int)cpuUsageTotal}%";

            }
            catch { return "10%"; }
           
        }
        public static string GetProcessMemoryUsage(string processName)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                if (processes.Length == 0)
                    return "0%";

                // Total memory used by all matching processes (in bytes)
                long totalMemoryUsedBytes = processes.Sum(p => p.WorkingSet64);
                double totalMemoryUsedMB = totalMemoryUsedBytes / (1024.0 * 1024.0);

                return $"{(int)totalMemoryUsedMB}MB";
            }
            catch
            {
                return "10%";
            }
        }

        private static float GetAvailableMemory()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return RamCounter.NextValue();
            }
            else
            {
                // Linux implementation using /proc/meminfo
                try
                {
                    var lines = File.ReadAllLines("/proc/meminfo");
                    var memAvailable = lines.First(l => l.StartsWith("MemAvailable:"));
                    var kb = long.Parse(memAvailable.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[1]);
                    return kb / 1024.0f; // Convert to MB
                }
                catch
                {
                    return 0;
                }
            }
        }
        private static string GetTotalMemory()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                    {
                        foreach (var obj in searcher.Get())
                        {
                            var totalMemoryBytes = Convert.ToInt64(obj["TotalPhysicalMemory"]);
                            return $"{(totalMemoryBytes / (1024 * 1024)):F2}";
                        }
                    }
                }
                else
                {
                    // Linux implementation
                    var lines = File.ReadAllLines("/proc/meminfo");
                    var memTotal = lines.First(l => l.StartsWith("MemTotal:"));
                    var kb = long.Parse(memTotal.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[1]);
                    return $"{(kb / 1024.0f / 1024.0f):F2}"; // Convert to GB
                }
            }
            catch
            {
                return "1024";
            }
            return "1024";
        }

        protected override void OnClose(CloseEventArgs e)
        {
            Console.WriteLine("Client disconnected from system monitoring");
            _timer?.Dispose();
        }
    }
    #endregion


    public class StreamSocket : WebSocketBehavior
    {
        private CameraStream _camera;
        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        private static SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(ConstantVariable.BoundCapacity, ConstantVariable.BoundCapacity);
        public void Initialize(CameraStream camera)
        {
            _camera = camera;
        }
        protected override void OnMessage(MessageEventArgs e)
        {
            _logger.Info($"Received message from client: {e.Data}");
            if (e.Data == "stop")
            {
                _logger.Info($"Stopping stream for {_camera.Name} as requested by client.");

                try
                {
                    _camera.FfmpegProcess?.Kill(true);
                    Worker._cams?.RemoveAll(c => c.Name == _camera.Name);
                    // Close the WebSocket session
                    Worker._webSocketServer.RemoveWebSocketService(_camera.Route);
                    Context.WebSocket.Close();
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to stop FFmpeg for {_camera.Name}: {ex.Message}");
                }
            }
            else
            {
                try
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        _semaphoreSlim.Wait();
                        var jsonObject =JsonConvert.DeserializeObject<StreamModel>(e.Data);
                        if (jsonObject != null)
                        {
                            var password = AesEncryption.Decrypt(jsonObject.Password);
                            var obj = new CameraStream
                            {
                                Name = jsonObject.RtspChannel,
                                Route = $"/{jsonObject.RtspChannel}",
                                Url = $"rtsp://{jsonObject.Username}:{password}@{jsonObject.IP}:{jsonObject.Port}/Streaming/Channels/{jsonObject.RtspChannel}/",
                                //Url = $"rtsp://admin:{jsonObject.Password}@{jsonObject.IP}:{jsonObject.Port}/Streaming/Channels/{jsonObject.RtspChannel}/",
                            };
                            _logger.Info($"Received RTSP URL for {obj.Name}: {obj.Url}");
                            if (!Worker._cams.Any(c => c.Route == obj.Route))
                            {
                                Worker.StartWebSocketServer(obj,jsonObject.Resolution, CancellationToken.None);
                                _semaphoreSlim.Release();
                            }
                            else
                            {
                                _logger.Info($"Stream for {obj.Name} already running.");
                                _semaphoreSlim.Release();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                }
            }
        }

        protected override void OnOpen()
        {
            if(_camera == null) return;
            _logger.Info($"Client connected to {_camera.Name}");
            Task.Run(() => PipeFfmpegToWebSocket());
        }

        private async Task PipeFfmpegToWebSocket()
        {
            var buffer = new byte[8192];
            int bytesRead;

            try
            {
                if(_camera !=null && _camera.FfmpegProcess != null)
                    while ((bytesRead = await _camera.FfmpegProcess.StandardOutput.BaseStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        if (State == WebSocketSharp.WebSocketState.Open)
                        {
                            Send(buffer.Take(bytesRead).ToArray());
                        }
                        else
                        {
                            _logger.Error($"WebSocket connection closed for {_camera.Name}");
                            break;
                        }
                    }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error piping {_camera.Name} stream: {ex.Message}");
            }
        }

        protected override void OnClose(CloseEventArgs e)
        {
            if(_camera == null) return;
            _logger.Info($"Client disconnected from {_camera?.Name}  error: {e.Reason}");

            //if (!string.IsNullOrEmpty(e.Reason))
            //{
            //    try
            //    {
            //        Worker._webSocketServer.RemoveWebSocketService(_camera.Route);
            //        Context.WebSocket.Close();
            //        Thread.Sleep(2000);
            //        Worker._webSocketServer.AddWebSocketService<StreamSocket>(_camera.Route, socket =>
            //        {
            //            socket.OriginValidator = origin =>
            //            {
            //                return true;
            //            };
            //            socket.Initialize(_camera);
            //        });
            //        Worker._webSocketServer.Start();
            //    }
            //    catch (Exception ex)
            //    {
            //        _logger.Error($"Failed to stop FFmpeg for {_camera.Name}: {ex.Message}");
            //    }
            //}
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
        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        public static WebSocketServer _webSocketServer;
        public static readonly List<CameraStream> _cams = new List<CameraStream>();
        private SemaphoreSlim SemaphoreSlim = new SemaphoreSlim(ConstantVariable.BoundCapacity, ConstantVariable.BoundCapacity);
        public Worker()
        {

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
                //var service = new MonitoringSocket();
                //var info = service.GetSystemInfo();
                //var json = JsonConvert.SerializeObject(info);
                //_logger.Info($"Sending system info: {json}");


                await InstallFFMpeg();
                var cams = new[]
                {
                    new CameraStream {
                        Name = "cam1",
                        Url = "rtsp://admin:tech@9900@106.51.129.154:554/Streaming/Channels/202/",
                        Route = "/101"
                    },
                    new CameraStream {
                        Name = "cam2",
                        Url = "rtsp://admin:tech@9900@106.51.129.154:554/Streaming/Channels/101/",
                        Route = "/102"
                    },
                    new CameraStream {
                        Name = "cam3",
                        Url = "rtsp://admin:tech@9900@106.51.129.154:554/Streaming/Channels/302/",
                        Route = "/201"
                    },
                     new CameraStream {
                        Name = "cam4",
                        Url = "rtsp://admin:tech@9900@106.51.129.154:554/Streaming/Channels/202/",
                        Route = "/202"
                    },
                    new CameraStream {
                        Name = "cam5",
                        Url = "rtsp://admin:tech@9900@106.51.129.154:554/Streaming/Channels/101/",
                        Route = "/301"
                    }
                };

                //_cams.AddRange(cams);

                // Start HTTP server
                HttpRequestHandler.StartHttpServer(ConstantVariable.LocalPort);
                _logger.Info("HTTP Server started on port 8080");

                // Start WebSocket server
                _webSocketServer = new WebSocketServer(ConstantVariable.websocketPort);


                #region starting the WebSocket server for streaming

                _webSocketServer.AddWebSocketService<StreamSocket>("/streaming", socket =>
                {
                    socket.OriginValidator = origin =>
                    {
                        return true;
                    };
                });
                _logger.Info($"Started on ws://localhost:{ConstantVariable.websocketPort}/streaming  to start the streaming");

                #endregion

                #region Starting the WebSocket server for system monitoring

                _webSocketServer.AddWebSocketService<MonitoringSocket>("/monitoring", socket =>
                {
                    socket.OriginValidator = origin =>
                    {
                        return true;
                    };
                });

                #endregion


                #region starting the WebSocket server for playback

                //var playBackUri = $"rtsp://admin:\"tech@9900\"@106.51.129.154:554/Streaming/tracks/101?starttime=20250522T100000Z&endtime=20250523T110000Z";
                //    var route = $"/playback?camera=101&time=20250522T100000Z";
                //    var cam1 = new CameraStream
                //    {
                //        Name = "Playback",
                //        Url = playBackUri,
                //        Route = "/playback"
                //    };
                //    StartFFmpegStream(cam1);
                //    _webSocketServer.AddWebSocketService<StreamSocket>(cam1.Route, socket =>
                //    {
                //        socket.OriginValidator = origin =>
                //        {
                //            return true;
                //        };
                //        socket.Initialize(cam1);
                //    });

                //    _cams.Add(cam1);

                #endregion
                _webSocketServer.Start();
                _logger.Info($"WebSocket Server started on port {ConstantVariable.websocketPort}");


                #region Hardcoded just for testing
                //try
                //{
                //    var websoc = new WebSocketSharp.WebSocket("ws://localhost:9898/streaming/");
                //    websoc.Connect();
                //    var data = new List<string>
                //    {
                //        $"{{\"ip\":\"106.51.129.154\",\"port\":554,\"username\":\"admin\",\"password\":\"tech@9900\",\"RtspChannel\":\"101\",\"ChannelId\":101,\"resolution\":\"1280x720\"}}",
                //        $"{{\"ip\":\"106.51.129.154\",\"port\":554,\"username\":\"admin\",\"password\":\"tech@9900\",\"RtspChannel\":\"201\",\"ChannelId\":201,\"resolution\":\"1280x720\"}}",
                //        $"{{\"ip\":\"106.51.129.154\",\"port\":554,\"username\":\"admin\",\"password\":\"tech@9900\",\"RtspChannel\":\"301\",\"ChannelId\":301,\"resolution\":\"1280x720\"}}",
                //    };
                //    foreach (var item in data)
                //    {
                //        websoc.Send(item);
                //    }
                //}
                //catch (Exception ex)
                //{
                //}
                #endregion
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(1000, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error starting servers");
                throw;
            }
        }

        internal static void StartWebSocketServer(CameraStream cam, string resolution, CancellationToken cancellationToken)
        {
            try
            {
                StartFFmpegStream(cam, resolution);

                _webSocketServer.AddWebSocketService<StreamSocket>(cam.Route, socket =>
                {
                    socket.OriginValidator = origin =>
                    {
                        return true;
                    };
                    socket.Initialize(cam);
                });
                _webSocketServer.Start();
                _cams.Add(cam);
                _logger.Info($"Started {cam.Name} on ws://localhost:{ConstantVariable.websocketPort}{cam.Route}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error starting WebSocket server");
            }
        }

        internal static void StartFFmpegStream(CameraStream cam, string resolution = "1280x720", bool isPlayback = false)
        {
            // URL-encode the password and use TCP transport
            var encodedUrl = cam.Url;
            var ffmpegArgs = string.Empty;
            if (!isPlayback)
            {
                ffmpegArgs = $"-rtsp_transport tcp -re -i \"{encodedUrl}\" " +
                        "-f mpegts -codec:v mpeg1video " +
                        "-q:v 5 -r 23.976 -bf 0 " +
                        $"-s {resolution} " +
                        "-loglevel warning -fflags nobuffer -err_detect ignore_err " +
                        "-";

                //ffmpegArgs = $"-rtsp_transport tcp -timeout 5000000 -re -i \"{encodedUrl}\" " +
                //            "-f mpegts -codec:v mpeg1video " +
                //            "-q:v 5 -r 23.976 -bf 0 " +
                //            "-s 1280x720 " +
                //            "-loglevel warning -fflags nobuffer -err_detect ignore_err -";


            }
            else
                ffmpegArgs = $"-i \"{encodedUrl}\" -f mpegts -codec:v mpeg1video -q:v 5 -r 24 -bf 0 -s {resolution} -";

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
            bool errorStream = false;
            cam.FfmpegProcess.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data) &&
                    !e.Data.Contains("deprecated pixel format") &&
                    !e.Data.Contains("Last message repeated"))
                    _logger.Error($"[FFmpeg] {cam.Name}: {e.Data}");

                if(e.Data !=null && e.Data.Contains("Unknown error"))
                    errorStream = true;
            };

            cam.FfmpegProcess.Exited += (sender, e) =>
            {
                _logger.Error($"[FFmpeg] {cam.Name} process exited with code {cam.FfmpegProcess.ExitCode}");
                if (cam.FfmpegProcess.ExitCode == 0 || (errorStream && cam.FfmpegProcess.ExitCode == -1))
                {
                    StartFFmpegStream(cam);
                    errorStream=false;
                }
            };

            try
            {
                cam.FfmpegProcess.Start();
                cam.FfmpegProcess.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                _logger.Error($"[Error] Failed to start {cam.Name}: {ex.Message}");
            }
        }
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Stopping servers...");

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
                    _logger.Info($"Stopped {cam.Name}");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error stopping {cam.Name}: {ex.Message}");
                }
            }
            await base.StopAsync(cancellationToken);
        }

        public static bool IsNvidiaGpuPresent()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("select * from Win32_VideoController");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var name = obj["Name"]?.ToString()?.ToLower();
                    if (!string.IsNullOrEmpty(name) && name.Contains("nvidia"))
                        return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"GPU check failed: {ex.Message}");
            }

            return false;
        }
        public static string GetFfmpegArgs(string encodedUrl, string pixelFormat = "1280x720")
        {
            bool useGpu = IsNvidiaGpuPresent();

            if (useGpu)
            {
                _logger.Info("NVIDIA GPU found — using GPU acceleration.");
                return $"-hwaccel cuda -rtsp_transport tcp -re -i \"{encodedUrl}\" " +
                       "-f mpegts -codec:v h264_nvenc -pix_fmt yuv420p -preset fast " +
                       $"-r 25 -bf 0 -s {pixelFormat} -loglevel warning -";
            }
            else
            {
                _logger.Info("No NVIDIA GPU — using software encoding.");
                return $"-rtsp_transport tcp -re -i \"{encodedUrl}\" " +
                       "-f mpegts -codec:v mpeg1video -q:v 5 -r 25 -bf 0 " +
                       $"-s {pixelFormat} -loglevel warning -";
            }
        }
    }
}
