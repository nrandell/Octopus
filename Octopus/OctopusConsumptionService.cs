using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using NodaTime;

namespace Octopus
{
    public class OctopusConsumptionService : BackgroundService
    {
        public ILogger Logger { get; }
        public OctopusService OctopusService { get; }
        public OctopusStoreService OctopusStoreService { get; }

        public OctopusConsumptionService(ILogger<OctopusConsumptionService> logger, OctopusService octopusService, OctopusStoreService octopusStoreService)
        {
            Logger = logger;
            OctopusService = octopusService;
            OctopusStoreService = octopusStoreService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var latest = await OctopusStoreService.ReadLastConsumptionEntryAsync();
                    await ProcessNewerValues(latest, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    Logger.LogWarning(ex, "Error in consumption service: {Exception}", ex.Message);
                }
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }

        private async Task ProcessNewerValues(OctopusConsumptionEntry? latest, CancellationToken ct)
        {
            Instant requestTime;
            if (latest == null)
            {
                requestTime = Instant.FromDateTimeOffset(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
            }
            else
            {
                requestTime = latest.Time.Plus(Duration.FromMinutes(30));
            }

            var entries = new List<OctopusConsumptionEntry>();
            await foreach (var consumption in OctopusService.ReadConsumption(requestTime, ct))
            {
                entries.Add(consumption);
            }

            if (entries.Count > 0)
            {
                var ordered = entries.OrderBy(e => e.Time).ToList();
                Logger.LogInformation("Got {Count} new consumption entries from {Start} to {End}", ordered.Count, ordered[0].Time, ordered[^1].Time);

                await OctopusStoreService.WriteEntriesAsync(ordered, ct);
            }
        }
    }
}
