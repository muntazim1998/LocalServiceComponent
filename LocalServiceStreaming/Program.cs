using LocalServiceStreaming;

var builder = Host.CreateApplicationBuilder(args);
Host.CreateDefaultBuilder(args)
    .UseWindowsService()
    .ConfigureServices((hostContext, services) =>
    {
        services.AddHostedService<Worker>(); 
        services.AddSingleton<List<CameraStream>>(new List<CameraStream>());
    })
    .Build()
    .Run();

