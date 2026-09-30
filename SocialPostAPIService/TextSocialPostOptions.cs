using Newtonsoft.Json;

namespace ScanPay.SocialPostService
{
    public class TextSocialPostOptions
    {
        [JsonProperty("connection_id")]
        public string ConnectionID { get; set; } = "";

        [JsonProperty("linkedin_page_urn")]
        public string LinkedInPageUrn { get; set; } = "";

        [JsonProperty("consent")]
        public bool Consent { get; set; }
    }
}
