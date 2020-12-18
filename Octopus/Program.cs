
using System;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using NodaTime.Text;

using Serilog;
using Serilog.Events;

namespace Octopus
{
    internal static class Program
    {
        public static int Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .CreateLogger();

            try
            {
                Log.Information("Starting up");

                CreateHostBuilder(args)
                    .Build()
                    .Run();
                return 0;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Host terminated unexpectedly");
                return 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host
                .CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    services.Configure<OctopusConfiguration>(context.Configuration.GetSection("Octopus"));
                    services.Configure<InfluxDbService.Config>(context.Configuration.GetSection("Influx"));

                    services.Configure<JsonSerializerOptions>(options =>
                    {
                        options.WriteIndented = false;
                        options.Converters.Add(new NodaPatternConverter<LocalDateTime>(
                            LocalDateTimePattern.CreateWithInvariantCulture("yyyy-MM-dd HH:mm:ss")));
                        options.Converters.Add(new NodaPatternConverter<OffsetDateTime>(
                            OffsetDateTimePattern.GeneralIso));
                        options.Converters.Add(new NodaPatternConverter<Instant>(InstantPattern.General));
                        options.PropertyNameCaseInsensitive = true;
                    });

                    services.AddSingleton<InfluxDbService>();
                    services.AddHttpClient<OctopusService>();

                    services.AddSingleton<OctopusStoreService>();
                    services.AddHostedService<OctopusTariffService>();
                    services.AddHostedService<OctopusConsumptionService>();
                    services.AddHostedService<OctopusPriceService>();
                })
            .UseSerilog((hostingContext, loggerConfiguration) =>
                loggerConfiguration
                    .ReadFrom.Configuration(hostingContext.Configuration)
                    .Enrich.FromLogContext()
                    .WriteTo.Console()
            );
    }
}
