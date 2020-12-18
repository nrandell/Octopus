using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Core.Exceptions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Polly;
using Polly.Contrib.WaitAndRetry;
using Polly.Retry;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Octopus
{
    public class InfluxDbService : IDisposable
    {
        public class Config
        {
            public string Token { get; init; } = default!;
            public string Bucket { get; init; } = default!;
            public string Organisation { get; init; } = default!;
            public string Url { get; init; } = default!;
        }

        public ILogger Logger { get; }
        public AsyncRetryPolicy RetryPolicy { get; }

        private readonly InfluxDBClient _client;

        public InfluxDbService(ILogger<InfluxDbService> logger, IOptions<Config> configOptions)
        {
            Logger = logger;
            var config = configOptions.Value;
            var options = InfluxDBClientOptions.Builder.CreateNew()
                .Url(config.Url)
                .Bucket(config.Bucket)
                .Org(config.Organisation)
                .AuthenticateToken(config.Token)
                .Build();

            _client = InfluxDBClientFactory.Create(options);

            var delay = Backoff.DecorrelatedJitterBackoffV2(TimeSpan.FromSeconds(1), retryCount: 100);
            RetryPolicy = Policy.Handle<HttpException>()
                .WaitAndRetryAsync(delay,
                    (ex, ts) => Logger.LogWarning(ex, "Waiting {TimeSpan} due to {Exception}", ts, ex.Message));
        }

        public void Dispose()
        {
            _client.Dispose();
            GC.SuppressFinalize(this);
        }

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

        public async Task<IReadOnlyList<T>> QueryAsync<T>(string query)
        {
            var queryApi = _client.GetQueryApi();
            return await queryApi.QueryAsync<T>(query);
        }
    }
}
