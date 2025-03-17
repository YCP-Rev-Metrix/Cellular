﻿using Cellular.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

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

            // Register services with dependency injection (DI)
            builder.Services.AddSingleton<CellularDatabase>(); // Register CellularDatabase as a Singleton
            builder.Services.AddSingleton<UserRepository>();

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
