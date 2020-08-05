using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Core.Exceptions;

using Microsoft.Extensions.Logging;

using Polly;
using Polly.Contrib.WaitAndRetry;
using Polly.Retry;

namespace Octopus
{
    public class InfluxDbService : IDisposable
    {
        public ILogger Logger { get; }
        public AsyncRetryPolicy RetryPolicy { get; }

        private const string DatabaseName = "HomeMeasurements";
        private readonly InfluxDBClient _client;

        public InfluxDbService(ILogger<InfluxDbService> logger)
        {
            Logger = logger;
            var options = InfluxDBClientOptions.Builder.CreateNew()
                .Url("http://server.home:8086")
                .Bucket(DatabaseName + "/one_year")
                .Org("-")
                .Build();

            _client = InfluxDBClientFactory.Create(options);

            var delay = Backoff.DecorrelatedJitterBackoffV2(TimeSpan.FromSeconds(1), retryCount: 100);
            RetryPolicy = Policy.Handle<HttpException>()
                .WaitAndRetryAsync(delay,
                    (ex, ts) => Logger.LogWarning(ex, "Waiting {TimeSpan} due to {Exception}", ts, ex.Message));
        }

        public void Dispose() => _client.Dispose();

        private Task WriteAsync<T>(List<T> entries, CancellationToken ct) =>
            RetryPolicy.ExecuteAsync(_ => _client.GetWriteApiAsync().WriteMeasurementsAsync(WritePrecision.S, entries), ct);

        public async Task WriteAsync<T>(IEnumerable<T> entries, CancellationToken ct)
        {
            var buffer = new List<T>(100);
            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                buffer.Add(entry);
                if (buffer.Count == buffer.Capacity)
                {
                    await WriteAsync(buffer, ct);
                    buffer.Clear();
                }
            }
            if (buffer.Count > 0)
            {
                await WriteAsync(buffer, ct);
            }
        }

#pragma warning disable MA0016 // Prefer return collection abstraction instead of implementation
        public Task<List<T>> QueryAsync<T>(string query)
#pragma warning restore MA0016 // Prefer return collection abstraction instead of implementation
        {
            var queryApi = _client.GetQueryApi();
            return queryApi.QueryAsync<T>(query);
        }
    }
}
