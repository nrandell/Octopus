
using InfluxDB.Client.Core;

using NodaTime;

namespace Octopus
{
    [Measurement("Price")]
    public class OctopusPriceEntry
    {
        [Column("_time", IsTimestamp = true)]
        public Instant Time { get; set; }

        [Column(nameof(CostIncVat))]
        public double CostIncVat { get; set; }

        [Column(nameof(CostExcVat))]
        public double CostExcVat { get; set; }

        public override string ToString() => $"{Time}: {CostExcVat} ({CostIncVat})";
    }
}
