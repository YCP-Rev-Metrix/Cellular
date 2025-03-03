using Cellular.Data;
using Microsoft.Extensions.Logging;

namespace Cellular
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            builder.Services.AddSingleton<CellularDatabase>();

#if DEBUG
            builder.Logging.AddDebug();
#endif

            var app = builder.Build();

            using var scope = app.Services.CreateScope();
            var databaseService = scope.ServiceProvider.GetRequiredService<CellularDatabase>();
            Task.Run(async () => await databaseService.InitializeAsync()).Wait();

            return app;
        }
    }
}
