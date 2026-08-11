using Newtonsoft.Json;

namespace ScanPay.SocialPostService
{
    public class SocialPostAnalyticsSummary
    {
        [JsonProperty("queued")]
        public int Queued { get; set; }

        [JsonProperty("publishing")]
        public int Publishing { get; set; }

        [JsonProperty("published")]
        public int Published { get; set; }

        [JsonProperty("failed")]
        public int Failed { get; set; }

        [JsonProperty("cancelled")]
        public int Cancelled { get; set; }

        [JsonProperty("total_deliveries")]
        public int TotalDeliveries { get; set; }
    }
}
