using Amazon.Lambda.Core;
using Newtonsoft.Json.Linq;
using ScanPay.Utility.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using static ScanPay.Utility.Model.ResponseStatusException;

namespace ScanPay.SocialPostService
{
    public class TikTokPublishingService
    {
        private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };

        private static async Task<SocialConnectionTokenDb> TokenAsync(SocialConnectionDb connection, ILambdaContext context)
        {
            if (connection.Platform != SocialPlatform.TikTok || connection.Status != SocialConnectionStatus.Connected)
                throw ResponseStatusFactory.BadRequest("Connect your TikTok account first.");
            var token = await DbCRUD<SocialConnectionTokenDb>.ReadConsistentAsync(connection.TokenSecretID, context);
            if (token == null || token.OrganizationID != connection.OrganizationID ||
                token.SocialConnectionID != connection.SocialConnectionID || token.Platform != SocialPlatform.TikTok)
                throw ResponseStatusFactory.BadRequest("TikTok credentials are unavailable. Please reconnect.");
            // Expired tokens fail closed; reconnection avoids racing refresh-token rotation.
            if (token.TokenExpiresDateUtc == null || token.TokenExpiresDateUtc <= DateTime.UtcNow.AddMinutes(1))
                throw ResponseStatusFactory.BadRequest("Your TikTok connection has expired. Please reconnect before posting.");
            return token;
        }

        private static async Task<JObject> PostAsync(string path, string accessToken, JObject payload)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://open.tiktokapis.com/v2/" + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
            using var response = await Client.SendAsync(request);
            var result = JObject.Parse(await response.Content.ReadAsStringAsync());
            if (!response.IsSuccessStatusCode || result["error"]?.Value<string>("code") != "ok")
                throw ResponseStatusFactory.BadRequest("TikTok rejected the request. Check account permissions, posting limits, and reconnect if needed.");
            return result["data"] as JObject ?? throw ResponseStatusFactory.BadRequest("TikTok returned an incomplete response.");
        }

        public async Task<JObject> CreatorAsync(SocialConnectionDb connection, ILambdaContext context)
        {
            var token = await TokenAsync(connection, context);
            return await PostAsync("post/publish/creator_info/query/", token.AccessToken, new JObject());
        }

        public static void Validate(SocialPostDb post, JObject creator)
        {
            var options = post.TikTok ?? throw ResponseStatusFactory.BadRequest("TikTok post settings are required.");
            if (post.Platforms.Count != 1 || post.Platforms[0] != SocialPlatform.TikTok ||
                string.IsNullOrWhiteSpace(options.ConnectionID) || !options.Consent)
                throw ResponseStatusFactory.BadRequest("Select a TikTok account and explicitly consent to sending this video.");
            if (!Uri.TryCreate(options.VideoUrl, UriKind.Absolute, out var video) ||
                video.Scheme != "https" || !string.IsNullOrEmpty(video.UserInfo) || !string.IsNullOrEmpty(video.Fragment) ||
                !(video.Host == "gogiveanywhere.com" || video.Host.EndsWith(".gogiveanywhere.com", StringComparison.OrdinalIgnoreCase)))
                throw ResponseStatusFactory.BadRequest("Use an HTTPS video URL hosted on the verified gogiveanywhere.com domain.");
            if (!double.IsFinite(options.DurationSeconds) || options.DurationSeconds <= 0 ||
                options.DurationSeconds > creator.Value<int>("max_video_post_duration_sec"))
                throw ResponseStatusFactory.BadRequest("The video duration exceeds this TikTok account's limit or could not be read.");
            if (post.Content.Length > 2200)
                throw ResponseStatusFactory.BadRequest("TikTok captions must be at most 2200 characters.");
            if (options.SendToInbox) return;
            var privacy = creator["privacy_level_options"]?.Values<string>().ToArray() ?? Array.Empty<string>();
            if (!privacy.Contains(options.PrivacyLevel))
                throw ResponseStatusFactory.BadRequest("Choose an available TikTok privacy setting.");
            if (options.BrandedContent && options.PrivacyLevel == "SELF_ONLY")
                throw ResponseStatusFactory.BadRequest("Branded content cannot use Only me privacy.");
            if ((options.AllowComments && creator.Value<bool>("comment_disabled")) ||
                (options.AllowDuet && creator.Value<bool>("duet_disabled")) ||
                (options.AllowStitch && creator.Value<bool>("stitch_disabled")))
                throw ResponseStatusFactory.BadRequest("TikTok account permissions changed. Reload the publishing form.");
        }

        public async Task<SocialPostDb> PublishAsync(SocialPostDb post, ILambdaContext context)
        {
            var options = post.TikTok ?? throw ResponseStatusFactory.BadRequest("Use the TikTok video composer to publish.");
            var connection = await new SocialConnectionService().ReadAsync(post.OrganizationID, options.ConnectionID, context);
            var token = await TokenAsync(connection, context);
            string scope = options.SendToInbox ? "video.upload" : "video.publish";
            if (!connection.GrantedScopes.Split(',', ' ').Contains(scope))
                throw ResponseStatusFactory.BadRequest("Reconnect TikTok and grant the required publishing permission.");
            Validate(post, await CreatorAsync(connection, context));

            // A deterministic, conditional delivery record prevents duplicate sends, including after timeouts.
            var delivery = new SocialPostDeliveryDb
            {
                DeliveryID = "tiktok-" + post.SocialPostID,
                OrganizationID = post.OrganizationID,
                SocialPostID = post.SocialPostID,
                SocialConnectionID = connection.SocialConnectionID,
                Platform = SocialPlatform.TikTok,
                Status = "initializing"
            };
            if (!await DbCRUD<SocialPostDeliveryDb>.TryCreateAsync(delivery, context))
                throw ResponseStatusFactory.BadRequest("This TikTok post was already submitted. Check its status before creating another.");

            try
            {
                var body = new JObject { ["source_info"] = new JObject { ["source"] = "PULL_FROM_URL", ["video_url"] = options.VideoUrl } };
                if (!options.SendToInbox)
                    body["post_info"] = new JObject
                    {
                        ["title"] = post.Content, ["privacy_level"] = options.PrivacyLevel,
                        ["disable_comment"] = !options.AllowComments, ["disable_duet"] = !options.AllowDuet,
                        ["disable_stitch"] = !options.AllowStitch, ["brand_organic_toggle"] = options.YourBrand,
                        ["brand_content_toggle"] = options.BrandedContent, ["is_aigc"] = options.IsAigc
                    };
                var result = await PostAsync(options.SendToInbox ? "post/publish/inbox/video/init/" : "post/publish/video/init/", token.AccessToken, body);
                delivery.ExternalPostID = result.Value<string>("publish_id") ?? throw new InvalidOperationException("Missing publish ID.");
                delivery.Status = "processing";
                await DbCRUD<SocialPostDeliveryDb>.SaveAsync(delivery, context);
                post.Status = SocialPostStatus.Queued;
                await DbCRUD<SocialPostDb>.SaveAsync(post, context);
                return post;
            }
            catch
            {
                // Never retry a possibly accepted initialization automatically.
                delivery.Status = "submission_unknown";
                delivery.ErrorMessage = "TikTok submission could not be confirmed. Check TikTok before trying another post.";
                await DbCRUD<SocialPostDeliveryDb>.SaveAsync(delivery, context);
                post.Status = "submission_unknown";
                await DbCRUD<SocialPostDb>.SaveAsync(post, context);
                throw ResponseStatusFactory.BadRequest(delivery.ErrorMessage);
            }
        }

        public async Task RefreshStatusAsync(SocialPostDb post, ILambdaContext context)
        {
            var delivery = await DbCRUD<SocialPostDeliveryDb>.ReadConsistentAsync("tiktok-" + post.SocialPostID, context);
            if (delivery == null || delivery.OrganizationID != post.OrganizationID ||
                delivery.Status != "processing" || string.IsNullOrWhiteSpace(delivery.ExternalPostID)) return;
            var connection = await new SocialConnectionService().ReadAsync(post.OrganizationID, delivery.SocialConnectionID, context);
            var token = await TokenAsync(connection, context);
            var result = await PostAsync("post/publish/status/fetch/", token.AccessToken,
                new JObject { ["publish_id"] = delivery.ExternalPostID });
            string status = result.Value<string>("status") ?? "";
            if (status == "PUBLISH_COMPLETE")
            {
                delivery.Status = "published";
                delivery.PublishedDateUtc = DateTime.UtcNow;
                post.Status = SocialPostStatus.Published;
                post.PublishedDateUtc = delivery.PublishedDateUtc;
            }
            else if (status == "SEND_TO_USER_INBOX")
            {
                delivery.Status = "sent_to_inbox";
                post.Status = "sent_to_inbox";
            }
            else if (status == "FAILED")
            {
                delivery.Status = "failed";
                delivery.ErrorCode = result.Value<string>("fail_reason") ?? "tiktok_failed";
                post.Status = SocialPostStatus.Failed;
            }
            else return;
            await DbCRUD<SocialPostDeliveryDb>.SaveAsync(delivery, context);
            await DbCRUD<SocialPostDb>.SaveAsync(post, context);
        }
    }
}
