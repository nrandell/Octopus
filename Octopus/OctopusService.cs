using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;

using Microsoft.Extensions.Options;

using NodaTime;
using NodaTime.Text;

namespace Octopus
{
    public class OctopusService
    {
        public HttpClient Client { get; }
        public JsonSerializerOptions SerializerOptions { get; }

        public OctopusService(HttpClient client, IOptions<OctopusConfiguration> options, IOptions<JsonSerializerOptions> serializerOptions)
        {
            var configuration = options.Value;
            var authString = Convert.ToBase64String(Encoding.UTF8.GetBytes(configuration.ApiKey + ":"));
            client.BaseAddress = new Uri(configuration.BaseUrl);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authString);
            Client = client;
            SerializerOptions = serializerOptions.Value;
        }

        public IAsyncEnumerable<OctopusTariffEntry> ReadTariff(Instant from, CancellationToken ct)
        {
            var requestParameter = InstantPattern.General.Format(from);
            var url = "v1/products/AGILE-18-02-21/electricity-tariffs/E-1R-AGILE-18-02-21-H/standard-unit-rates?period_from=" + requestParameter;
            return Query<OctopusTariffEntry>(url, ct);
        }

        public IAsyncEnumerable<OctopusConsumptionEntry> ReadConsumption(Instant from, CancellationToken ct)
        {
            var requestParameter = InstantPattern.General.Format(from);
            var url = "v1/electricity-meter-points/2000017637833/meters/19L3667759/consumption/?period_from=" + requestParameter;
            return Query<OctopusConsumptionEntry>(url, ct);
        }

        private async IAsyncEnumerable<T> Query<T>(string url, [EnumeratorCancellation] CancellationToken ct)
        {
            string? urlToUse = url;
            while (!ct.IsCancellationRequested && (urlToUse != null))
            {
                var response = await Client.GetAsync(urlToUse, ct);
                response.EnsureSuccessStatusCode();

                using var responseStream = await response.Content.ReadAsStreamAsync(ct);
                var octopusResponse = await JsonSerializer.DeserializeAsync<OctopusResponse<T>>(responseStream, SerializerOptions, ct);
                if (octopusResponse != null)
                {
                    foreach (var result in octopusResponse.Results)
                    {
                        yield return result;
                    }

                    if (string.IsNullOrWhiteSpace(octopusResponse.Next))
                    {
                        urlToUse = null;
                    }
                    else
                    {
                        var uri = new Uri(octopusResponse.Next);
                        urlToUse = uri.PathAndQuery;
                    }
                }
                else
                {
                    urlToUse = null;
                }
            }
        }
    }
}
