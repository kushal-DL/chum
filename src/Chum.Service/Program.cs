using Chum.Service;
using Serilog;
using Serilog.Events;

// ── Logging ───────────────────────────────────────────────────────────────
var logDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "Chum", "Logs");
Directory.CreateDirectory(logDir);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .WriteTo.File(Path.Combine(logDir, "service-.log"),
        rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
    .WriteTo.EventLog("ChumHostSvc", manageEventSource: false)
    .CreateLogger();

// ── Host ──────────────────────────────────────────────────────────────────
var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(o => o.ServiceName = "ChumHostSvc")
    .UseSerilog()
    .ConfigureServices(services =>
    {
        services.AddSingleton<AuditLogger>();
        services.AddHostedService<ChumWorker>();
    })
    .Build();

try
{
    await host.RunAsync();
}
finally
{
    Log.CloseAndFlush();
}
