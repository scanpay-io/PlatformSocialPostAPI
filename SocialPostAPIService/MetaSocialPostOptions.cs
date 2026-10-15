using Newtonsoft.Json;

namespace ScanPay.SocialPostService
{
    public class MetaSocialPostOptions
    {
        [JsonProperty("platform")]
        public string Platform { get; set; } = "";
        [JsonProperty("connection_id")]
        public string ConnectionID { get; set; } = "";
        [JsonProperty("facebook_page_id")]
        public string FacebookPageID { get; set; } = "";
        [JsonProperty("image_url")]
        public string ImageUrl { get; set; } = "";
    }
}
