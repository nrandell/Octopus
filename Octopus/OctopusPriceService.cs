using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NodaTime;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Octopus
{
    public class OctopusPriceService : BackgroundService
    {
        public ILogger Logger { get; }
        public OctopusStoreService OctopusStoreService { get; }
        public string Bucket { get; }

        public OctopusPriceService(ILogger<OctopusPriceService> logger, OctopusStoreService octopusStoreService, IOptions<InfluxDbService.Config> influxConfigOptions)
        {
            Logger = logger;
            OctopusStoreService = octopusStoreService;
            Bucket = influxConfigOptions.Value.Bucket;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var latest = await OctopusStoreService.ReadLastPriceEntryAsync();
                    await ProcessNewerValues(latest, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    Logger.LogWarning(ex, "Error in price service: {Exception}", ex.Message);
                }
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }

        private async Task ProcessNewerValues(OctopusPriceEntry? latest, CancellationToken ct)
        {
            Instant requestTime;
            List<OctopusConsumptionEntry> consumptionEntries;

            if (latest == null)
            {
                var flux = $@"
from(bucket: ""{Bucket}"")
|> range(start: -12mo)
|> filter(fn: (r) => r._measurement == ""Consumption"")
|> pivot(
    rowKey:[""_time""],
    columnKey: [""_field""],
    valueColumn: ""_value""
)";
                consumptionEntries = await OctopusStoreService.QueryAsync<OctopusConsumptionEntry>(flux);
                if (consumptionEntries.Count == 0)
                {
                    requestTime = Instant.FromUtc(2020, 1, 1, 0, 0);
                }
                else
                {
                    requestTime = consumptionEntries[0].Time.Plus(Duration.FromMinutes(30));
                }
            }
            else
            {
                requestTime = latest.Time.Plus(Duration.FromMinutes(30));
                var flux = $@"
from(bucket: ""{Bucket}"")
|> range(start: {requestTime})
|> filter(fn: (r) => r._measurement == ""Consumption"")
|> pivot(
    rowKey:[""_time""],
    columnKey: [""_field""],
    valueColumn: ""_value""
)";
                consumptionEntries = await OctopusStoreService.QueryAsync<OctopusConsumptionEntry>(flux);
            }

            if (consumptionEntries.Count > 0)
            {
                var tariffEntries = await QueryTarrifEntries(requestTime);
                Logger.LogInformation("Got {Count} tariff entries from {First} to {Last} - requested = {Request}", tariffEntries.Count, tariffEntries[0], tariffEntries[^1], requestTime);
                Logger.LogInformation("Got {Count} consumption entries from {First} to {Last}", consumptionEntries.Count, consumptionEntries[0], consumptionEntries[^1]);

                var joined = JoinConsumptionAndTariff(consumptionEntries, tariffEntries);

                Logger.LogInformation("Got {Count} price entries from {First} to {Last}", joined.Count, joined[0], joined[^1]);

                await OctopusStoreService.WriteEntriesAsync(joined, ct);
            }
        }

        private static List<OctopusPriceEntry> JoinConsumptionAndTariff(List<OctopusConsumptionEntry> consumptionEntries, List<OctopusTariffEntry> tariffEntries)
        {
            return consumptionEntries.Join(tariffEntries, oce => oce.Time, ope => ope.Time,
                (oce, ope) =>
                {
                    var consumption = Math.Round(oce.Consumption, 2);
                    return new OctopusPriceEntry
                    {
                        Time = ope.Time,
                        CostExcVat = ope.ValueExcVat * consumption,
                        CostIncVat = ope.ValueIncVat * consumption,
                    };
                })
                .OrderBy(p => p.Time)
                .ToList();
        }

        private async Task<List<OctopusTariffEntry>> QueryTarrifEntries(Instant requestTime)
        {
            var tariffFlux = $@"
from(bucket:""{Bucket}"") 
|> range(start: {requestTime})
|> filter(fn: (r) => (r._measurement == ""Tariff""))
|> pivot(
    rowKey:[""_time""],
    columnKey: [""_field""],
    valueColumn: ""_value""
)";
            var tariffEntries = await OctopusStoreService.QueryAsync<OctopusTariffEntry>(tariffFlux);
            return tariffEntries;
        }
    }
}
