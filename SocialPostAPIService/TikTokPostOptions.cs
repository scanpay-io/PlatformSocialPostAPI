using Newtonsoft.Json;

namespace ScanPay.SocialPostService
{
    public class TikTokPostOptions
    {
        [JsonProperty("connection_id")] public string ConnectionID { get; set; } = "";
        [JsonProperty("video_url")] public string VideoUrl { get; set; } = "";
        [JsonProperty("privacy_level")] public string PrivacyLevel { get; set; } = "";
        [JsonProperty("duration_seconds")] public double DurationSeconds { get; set; }
        [JsonProperty("send_to_inbox")] public bool SendToInbox { get; set; }
        [JsonProperty("allow_comments")] public bool AllowComments { get; set; }
        [JsonProperty("allow_duet")] public bool AllowDuet { get; set; }
        [JsonProperty("allow_stitch")] public bool AllowStitch { get; set; }
        [JsonProperty("your_brand")] public bool YourBrand { get; set; }
        [JsonProperty("branded_content")] public bool BrandedContent { get; set; }
        [JsonProperty("is_aigc")] public bool IsAigc { get; set; }
        [JsonProperty("consent")] public bool Consent { get; set; }
    }
}
