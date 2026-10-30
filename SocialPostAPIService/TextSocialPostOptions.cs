using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace ScanPay.SocialPostService
{
    public class TextSocialPostOptions
    {
        // Preserve unsupported input so attachments cannot be silently dropped.
        [JsonExtensionData]
        public IDictionary<string, JToken>? AdditionalOptions { get; set; }

        [JsonProperty("connection_id")]
        public string ConnectionID { get; set; } = "";

        [JsonProperty("linkedin_page_urn")]
        public string LinkedInPageUrn { get; set; } = "";

        [JsonProperty("consent")]
        public bool Consent { get; set; }
    }
}
