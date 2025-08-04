using Seacore.Resources.Usercontrols.Servers;
using System.Windows;
using Serilog;

namespace Seacore
{
    public partial class App : Application
    {
        public App()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File("logs\\application.log", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            Log.Information("Application Started");
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ServerStateManager.Instance.Initialize();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("Application Exited");
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}