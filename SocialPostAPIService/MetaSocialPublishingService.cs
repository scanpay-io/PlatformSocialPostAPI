using Amazon.Lambda.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static ScanPay.Utility.Model.ResponseStatusException;

namespace ScanPay.SocialPostService
{
    public class MetaSocialPublishingService
    {
        private readonly MetaSocialProviderClient client;
        private readonly IMetaPublishingStore? storeOverride;
        public MetaSocialPublishingService(MetaSocialProviderClient? client = null, IMetaPublishingStore? store = null)
        { this.client = client ?? new(); storeOverride = store; }

        public static bool Supports(string platform) => platform is SocialPlatform.Facebook or SocialPlatform.Instagram or SocialPlatform.Threads;

        public static string Validate(SocialPostDb post, string platform)
        {
            if (post.MetaSocial == null || post.MetaSocial.Any(o => o == null))
                throw ResponseStatusFactory.BadRequest("meta_social must be an array of platform settings.");
            if (!Supports(platform) || post.Platforms.Any(p => !Supports(p)))
                throw ResponseStatusFactory.BadRequest("X, LinkedIn and TikTok must each be published as separate posts.");
            if (post.MetaSocial.GroupBy(o => o.Platform).Any(g => g.Count() > 1) ||
                post.MetaSocial.Any(o => !post.Platforms.Contains(o.Platform)))
                throw ResponseStatusFactory.BadRequest("Provide at most one meta_social setting for each selected platform.");
            if (string.IsNullOrWhiteSpace(post.Content)) throw ResponseStatusFactory.BadRequest("Post text is required.");
            if (!string.IsNullOrWhiteSpace(post.DestinationUrl)) ValidateUrl(post.DestinationUrl, false);
            string text = post.Content.Trim() + (string.IsNullOrWhiteSpace(post.DestinationUrl) ? "" : "\n\n" + post.DestinationUrl);
            if (text.Length > (platform == SocialPlatform.Threads ? 500 : platform == SocialPlatform.Instagram ? 2200 : 63206))
                throw ResponseStatusFactory.BadRequest($"The post exceeds the {platform} text limit.");
            string image = Options(post, platform).ImageUrl;
            if (platform == SocialPlatform.Instagram && string.IsNullOrWhiteSpace(image))
                throw ResponseStatusFactory.BadRequest("Instagram requires a publicly accessible JPEG image URL in meta_social.image_url.");
            if (!string.IsNullOrWhiteSpace(image)) ValidateUrl(image, true);
            return text;
        }

        private static void ValidateUrl(string value, bool image)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "https" && (image || uri.Scheme != "http")) ||
                !string.IsNullOrEmpty(uri.UserInfo) || uri.IsLoopback || !string.IsNullOrEmpty(uri.Fragment) && image)
                throw ResponseStatusFactory.BadRequest(image ? "Use a public HTTPS image URL without credentials or a fragment." : "Use an HTTP or HTTPS destination URL without credentials.");
        }

        private static MetaSocialPostOptions Options(SocialPostDb post, string platform) =>
            post.MetaSocial.FirstOrDefault(o => o.Platform == platform) ?? new() { Platform = platform };

        private static async Task<SocialConnectionTokenDb> TokenAsync(IMetaPublishingStore store, SocialConnectionDb connection)
        {
            var token = await store.TokenAsync(connection.TokenSecretID);
            if (connection.Status != SocialConnectionStatus.Connected || token == null ||
                token.OrganizationID != connection.OrganizationID || token.SocialConnectionID != connection.SocialConnectionID ||
                token.Platform != connection.Platform || string.IsNullOrWhiteSpace(token.AccessToken) ||
                token.TokenExpiresDateUtc <= DateTime.UtcNow.AddMinutes(1))
                throw ResponseStatusFactory.BadRequest($"Reconnect the {connection.Platform} account before posting.");
            return token;
        }

        private static async Task EnsureEnabledAsync(IMetaPublishingStore store, string platform)
        {
            var provider = (await store.ProvidersAsync()).FirstOrDefault(p => string.Equals(p.Key, platform, StringComparison.OrdinalIgnoreCase)).Value;
            if (provider == null || !provider.Supported || !provider.OAuthEnabled || !provider.PostingEnabled)
                throw ResponseStatusFactory.BadRequest($"{platform} publishing is not enabled. Complete provider configuration and access approval first.");
        }

        public async Task<List<SocialPage>> FacebookPagesAsync(SocialConnectionDb connection, ILambdaContext context)
        {
            if (connection.Platform != SocialPlatform.Facebook) throw ResponseStatusFactory.BadRequest("Select a Facebook connection.");
            var token = await TokenAsync(storeOverride ?? new MetaPublishingStore(context), connection);
            return (await client.FacebookPagesAsync(token.AccessToken)).Select(p => new SocialPage(p.ID, p.Name)).ToList();
        }

        public async Task<SocialPostDb> PublishAsync(SocialPostDb post, ILambdaContext context)
        {
            if (post.Status == SocialPostStatus.Cancelled) throw ResponseStatusFactory.BadRequest("Cancelled posts cannot be published.");
            var store = storeOverride ?? new MetaPublishingStore(context);
            foreach (string platform in post.Platforms) Validate(post, platform);
            var connections = await store.ConnectionsAsync(post.OrganizationID);
            var existing = await store.DeliveriesAsync(post);
            if (existing.Any(d => d.Status == "queued"))
                throw ResponseStatusFactory.BadRequest("This post uses the retired queue. Cancel it and create a new post to confirm its destinations.");
            var pending = new List<(SocialPostDeliveryDb Delivery, string Token)>();
            // Preflight every target before sending anything to a provider.
            foreach (string platform in post.Platforms)
            {
                if (existing.Any(d => d.Platform == platform)) continue;
                await EnsureEnabledAsync(store, platform);
                var options = Options(post, platform);
                var candidates = connections.Where(c => c.OrganizationID == post.OrganizationID && c.Platform == platform &&
                    c.Status == SocialConnectionStatus.Connected && (string.IsNullOrWhiteSpace(options.ConnectionID) || c.SocialConnectionID == options.ConnectionID)).ToList();
                if (candidates.Count != 1) throw ResponseStatusFactory.BadRequest($"Select one connected {platform} account in meta_social.connection_id.");
                var connection = candidates[0];
                var token = await TokenAsync(store, connection);
                string target = MetaSocialProviderClient.ID(connection.ExternalAccountID);
                string accessToken = token.AccessToken;
                if (platform == SocialPlatform.Facebook)
                {
                    var pages = (await client.FacebookPagesAsync(accessToken)).Where(p =>
                        string.IsNullOrWhiteSpace(options.FacebookPageID) || p.ID == options.FacebookPageID).ToList();
                    if (pages.Count != 1) throw ResponseStatusFactory.BadRequest("Select one manageable Facebook Page in meta_social.facebook_page_id.");
                    target = pages[0].ID;
                    accessToken = pages[0].AccessToken;
                }
                pending.Add((new SocialPostDeliveryDb
                {
                    DeliveryID = platform + "-" + post.SocialPostID, SocialPostID = post.SocialPostID,
                    OrganizationID = post.OrganizationID, SocialConnectionID = connection.SocialConnectionID,
                    Platform = platform, TargetAccountID = target, Status = "initializing"
                }, accessToken));
            }
            if (pending.Count > 0 && !await store.BeginAsync(post))
                throw ResponseStatusFactory.BadRequest("This post changed in another request. Reload its status before publishing.");
            await Task.WhenAll(pending.Select(async item =>
            {
                var delivery = item.Delivery;
                if (!await store.CreateAsync(delivery)) return; // Another request already owns this submission.
                try
                {
                    var options = Options(post, delivery.Platform);
                    if (delivery.Platform == SocialPlatform.Facebook)
                    {
                        delivery.ExternalPostID = await client.PublishFacebookAsync(delivery.TargetAccountID, item.Token,
                            post.Content.Trim(), post.DestinationUrl, options.ImageUrl);
                        SetPublished(delivery);
                    }
                    else
                    {
                        delivery.ContainerID = await client.CreateContainerAsync(delivery.Platform, delivery.TargetAccountID,
                            item.Token, Validate(post, delivery.Platform), options.ImageUrl);
                        delivery.Status = "processing";
                    }
                    await store.SaveAsync(delivery);
                }
                catch
                {
                    // A timeout or storage failure may follow an accepted remote write. Never resend automatically.
                    delivery.Status = "submission_unknown";
                    delivery.ErrorMessage = "Submission could not be confirmed. Check the platform before creating another post.";
                    await store.SaveAsync(delivery);
                }
            }));
            await AggregateAsync(post, store);
            return post;
        }

        public async Task RefreshStatusAsync(SocialPostDb post, ILambdaContext context)
        {
            if (post.Status == SocialPostStatus.Cancelled) return;
            var store = storeOverride ?? new MetaPublishingStore(context);
            var connections = await store.ConnectionsAsync(post.OrganizationID);
            var deliveries = await store.DeliveriesAsync(post);
            await Task.WhenAll(deliveries.Where(d => d.Status == "processing" &&
                d.Platform is SocialPlatform.Instagram or SocialPlatform.Threads).Select(async delivery =>
            {
                await EnsureEnabledAsync(store, delivery.Platform);
                var connection = connections.SingleOrDefault(c => c.OrganizationID == post.OrganizationID &&
                    c.SocialConnectionID == delivery.SocialConnectionID && c.Platform == delivery.Platform);
                if (connection == null) throw ResponseStatusFactory.BadRequest("Reconnect the original publishing account.");
                var token = await TokenAsync(store, connection);
                if (connection.ExternalAccountID != delivery.TargetAccountID)
                    throw ResponseStatusFactory.BadRequest("The connected account changed. Check the original post before publishing again.");
                string status = await client.ContainerStatusAsync(delivery.Platform, token.AccessToken, delivery.ContainerID);
                if (status != "FINISHED" && status != "ERROR" && status != "EXPIRED") return;
                if (!await store.ClaimAsync(delivery)) return; // Only one poll may publish a finished container.
                delivery.Status = "publishing";
                try
                {
                    if (status == "FINISHED")
                    {
                        delivery.ExternalPostID = await client.PublishContainerAsync(delivery.Platform,
                            delivery.TargetAccountID, token.AccessToken, delivery.ContainerID);
                        SetPublished(delivery);
                    }
                    else
                    {
                        delivery.Status = SocialPostDeliveryStatus.Failed;
                        delivery.ErrorCode = "container_" + status.ToLowerInvariant();
                        delivery.ErrorMessage = "The media could not be processed or its container expired. Create a new post with a valid public image.";
                    }
                    await store.SaveAsync(delivery);
                }
                catch
                {
                    delivery.Status = "submission_unknown";
                    delivery.ErrorMessage = "Publication could not be confirmed. Check the platform before creating another post.";
                    await store.SaveAsync(delivery);
                }
            }));
            await AggregateAsync(post, store);
        }

        private static void SetPublished(SocialPostDeliveryDb delivery)
        {
            delivery.Status = SocialPostDeliveryStatus.Published;
            delivery.PublishedDateUtc = DateTime.UtcNow;
            delivery.LastUpdate = DateTime.UtcNow;
            if (delivery.Platform == SocialPlatform.Facebook)
                delivery.ExternalPostUrl = "https://www.facebook.com/" + delivery.ExternalPostID;
        }

        public static string AggregateStatus(IReadOnlyCollection<SocialPostDeliveryDb> deliveries, int expected)
        {
            if (deliveries.Count == expected && deliveries.All(d => d.Status == "published")) return SocialPostStatus.Published;
            if (deliveries.Any(d => d.Status == "submission_unknown")) return "submission_unknown";
            if (deliveries.Any(d => d.Status is "initializing" or "processing" or "publishing")) return SocialPostStatus.Publishing;
            if (deliveries.Any(d => d.Status == "published")) return SocialPostStatus.PartiallyPublished;
            return SocialPostStatus.Failed;
        }

        private static async Task AggregateAsync(SocialPostDb post, IMetaPublishingStore store)
        {
            var deliveries = (await store.DeliveriesAsync(post)).Where(d => post.Platforms.Contains(d.Platform)).ToList();
            post.Status = AggregateStatus(deliveries, post.Platforms.Count);
            post.PublishedDateUtc = post.Status == SocialPostStatus.Published ? deliveries.Max(d => d.PublishedDateUtc) : null;
            post.LastUpdate = DateTime.UtcNow;
            await store.SaveAsync(post);
        }
    }
}
