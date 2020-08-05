using System.Text.Json.Serialization;

using InfluxDB.Client.Core;

using NodaTime;

namespace Octopus
{
    [Measurement("Consumption")]
    public class OctopusConsumptionEntry
    {
        [Column("Consumption")]
        public double Consumption { get; set; }

        [JsonPropertyName("interval_start")]
        public OffsetDateTime IntervalStart
        {
            get => Time.WithOffset(Offset.Zero);
            set => Time = value.ToInstant();
        }

        [JsonPropertyName("interval_end")]
        public OffsetDateTime IntervalEnd { get; set; }

        [Column("_time", IsTimestamp = true)]
        public Instant Time { get; set; }

        public override string ToString() => $"{Time} = {Consumption}kWh";
    }
}
