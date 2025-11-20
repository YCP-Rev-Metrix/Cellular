using Cellular.Data;
using Cellular.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Camera.MAUI;
using CommunityToolkit.Maui;
using Cellular.Services;

namespace Cellular
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder.UseMauiApp<App>().UseMauiCameraView().UseMauiCommunityToolkit().ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });
            // Register services with dependency injection (DI)
            builder.Services.AddSingleton<CellularDatabase>(); // Register CellularDatabase as a Singleton
            builder.Services.AddSingleton<UserRepository>();
            builder.Services.AddSingleton<IMetaWearService, MetaWearBleService>();
            
#if DEBUG
            builder.Logging.AddDebug();
#endif
            builder.Logging.AddDebug();
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