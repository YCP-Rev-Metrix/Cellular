using Cellular.Data;
using Camera.MAUI;
using Cellular.Services;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cellular
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>().UseMauiCameraView()
                .UseMauiCommunityToolkitCore()
                .UseMauiCommunityToolkit()
                .UseMauiCommunityToolkitCamera()
                .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });
            // Register services with dependency injection (DI)
            builder.Services.AddSingleton<CellularDatabase>(); // Register CellularDatabase as a Singleton
            builder.Services.AddSingleton<UserRepository>();
            builder.Services.AddSingleton<IMetaWearService, MetaWearBleService>();
            builder.Services.AddSingleton<IWatchBleService, WatchBleService>();
            
#if DEBUG
            builder.Logging.AddDebug();
#endif

            var app = builder.Build();
            // Initialize the database asynchronously
            using (var scope = app.Services.CreateScope())
            {
                var databaseService = scope.ServiceProvider.GetRequiredService<CellularDatabase>();
                Task.Run(async () => await databaseService.InitializeAsync()).Wait();
            }

            return app;
        }
    }
}