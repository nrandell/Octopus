namespace Octopus
{
    public class OctopusConfiguration
    {
        public string ApiKey { get; set; } = default!;
        public string BaseUrl { get; set; } = "https://api.octopus.energy";
    }
}