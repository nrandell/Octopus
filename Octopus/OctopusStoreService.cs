using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace Octopus
{
    public class OctopusStoreService
    {
        public ILogger Logger { get; }
        public InfluxDbService InfluxDb { get; }

        public OctopusStoreService(ILogger<OctopusStoreService> logger, InfluxDbService influxDbService)
        {
            Logger = logger;
            InfluxDb = influxDbService;
        }

#pragma warning disable MA0016 // Prefer return collection abstraction instead of implementation
        public Task<List<T>> QueryAsync<T>(string flux) => InfluxDb.QueryAsync<T>(flux);
#pragma warning restore MA0016 // Prefer return collection abstraction instead of implementation

        public async Task<OctopusPriceEntry?> ReadLastPriceEntryAsync()
        {
            const string flux = @"
from(bucket:""HomeMeasurements/one_year"") 
|> range(start: -12mo, stop: 1w)
|> filter(fn: (r) => (r._measurement == ""Price""))
|> last()
|> pivot(
    rowKey:[""_time""],
    columnKey: [""_field""],
    valueColumn: ""_value""
)";
            var results = await InfluxDb.QueryAsync<OctopusPriceEntry>(flux);
            return results.SingleOrDefault();
        }

        public async Task<OctopusTariffEntry?> ReadLastTariffEntryAsync()
        {
            const string flux = @"
from(bucket:""HomeMeasurements/one_year"") 
|> range(start: -12mo, stop: 1w)
|> filter(fn: (r) => (r._measurement == ""Tariff""))
|> last()
|> pivot(
    rowKey:[""_time""],
    columnKey: [""_field""],
    valueColumn: ""_value""
)";

            var results = await InfluxDb.QueryAsync<OctopusTariffEntry>(flux);
            return results.SingleOrDefault();
        }

        public Task WriteEntriesAsync<T>(IEnumerable<T> entries, CancellationToken ct)
        => InfluxDb.WriteAsync(entries, ct);

        public async Task<OctopusConsumptionEntry?> ReadLastConsumptionEntryAsync()
        {
            const string flux = @"
from(bucket:""HomeMeasurements/one_year"") 
|> range(start: -12mo, stop: 1w)
|> filter(fn: (r) => (r._measurement == ""Consumption""))
|> last()
|> pivot(
    rowKey:[""_time""],
    columnKey: [""_field""],
    valueColumn: ""_value""
)";
            var results = await InfluxDb.QueryAsync<OctopusConsumptionEntry>(flux);
            return results.SingleOrDefault();
        }
    }
}
