﻿using InfluxDB.Client.Core;

using NodaTime;

using System.Text.Json.Serialization;

namespace Nick.Energy.Models
{
    [Measurement("Tariff")]
    public class OctopusTariffEntry
    {
        [Column(nameof(ValueExcVat))]
        [JsonPropertyName("value_exc_vat")]
        public double ValueExcVat { get; set; }

        [Column(nameof(ValueIncVat))]
        [JsonPropertyName("value_inc_vat")]
        public double ValueIncVat { get; set; }

        [JsonPropertyName("valid_from")]
        public Instant ValidFrom { get; set; }

        [JsonPropertyName("valid_to")]
        public Instant ValidTo { get; set; }

        [Column("time", IsTimestamp = true)]
        public Instant Time { get => ValidFrom; set => ValidFrom = value; }

        public override string ToString() => $"{Time} = {ValueIncVat}p/kWh ({ValueExcVat}p/kWh)";
    }
}
