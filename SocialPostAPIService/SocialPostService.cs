using Amazon.Lambda.Core;
using ScanPay.Utility.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static ScanPay.Utility.Model.ResponseStatusException;

namespace ScanPay.SocialPostService
{
    public class SocialPostService
    {
        public async Task<SocialPostDb> CreateAsync(
            CreateSocialPostRequest request,
            ILambdaContext context)
        {
            ValidateCreateRequest(
                request);

            request.Platforms = NormalizePlatforms(request.Platforms);
            if (request.Platforms.Any(p => p == SocialPlatform.TikTok || TextSocialPublishingService.Supports(p)) && request.Platforms.Count != 1)
                throw ResponseStatusFactory.BadRequest("X, LinkedIn and TikTok must each be created as separate posts.");
            if (request.MetaSocial == null || request.MetaSocial.Any(o => o == null))
                throw ResponseStatusFactory.BadRequest("meta_social must be an array of platform settings.");
            foreach (var options in request.MetaSocial) options.Platform = options.Platform?.Trim().ToLowerInvariant() ?? "";
            if (request.MetaSocial.Any(o => !MetaSocialPublishingService.Supports(o.Platform) || !request.Platforms.Contains(o.Platform)))
                throw ResponseStatusFactory.BadRequest("meta_social settings must match the selected Meta platforms.");

            if (request.TikTok != null &&
                (request.Platforms.Count != 1 || request.Platforms[0] != SocialPlatform.TikTok))
                throw ResponseStatusFactory.BadRequest("TikTok videos must be created as separate posts.");

            if (request.TextSocial != null &&
                (request.Platforms.Count != 1 || !TextSocialPublishingService.Supports(request.Platforms[0])))
                throw ResponseStatusFactory.BadRequest("X and LinkedIn posts must each target one selected account.");

            string socialPostID =
                FormatValue.NewID();

            var post =
                new SocialPostDb
                {
                    TikTok = request.TikTok,
                    TextSocial = request.TextSocial,
                    MetaSocial = request.MetaSocial,
                    SocialPostID =
                        socialPostID,

                    OrganizationID =
                        request.EffectiveOrganizationID,

                    ResourceType =
                        request.ResourceType,

                    ResourceID =
                        request.ResourceID,

                    OutreachItemID =
                        request.OutreachItemID,

                    Content =
                        request.Content,

                    DestinationUrl =
                        AddSocialPostTracking(
                            request.DestinationUrl,
                            socialPostID),

                    Platforms =
                        NormalizePlatforms(
                            request.Platforms),

                    Status =
                        SocialPostStatus.Draft,

                    CreateDate =
                        DefaultValue.UtcNow(),

                    LastUpdate =
                        DefaultValue.UtcNow()
                };

            if (post.Platforms.All(MetaSocialPublishingService.Supports))
                foreach (string platform in post.Platforms) MetaSocialPublishingService.Validate(post, platform);

            await DbCRUD<SocialPostDb>.CreateAsync(
                post,
                context);

            return post;
        }

        public async Task<SocialPostDb> ReadAsync(
            string organizationID,
            string socialPostID,
            ILambdaContext context)
        {
            if (FormatValue.EmptyValue(socialPostID))
            {
                throw ResponseStatusFactory.BadRequest(
                    "post_id is required.");
            }

            SocialPostDb post =
                await DbCRUD<SocialPostDb>.ReadConsistentAsync(
                    socialPostID,
                    context);

            if (post == null)
            {
                throw ResponseStatusFactory.NotFound(
                    $"Social post({socialPostID}) was not found.");
            }

            EnsureOrganizationOwnsPost(
                organizationID,
                post);

            return post;
        }

        public async Task<SocialPostDb> UpdateAsync(
            UpdateSocialPostRequest request,
            ILambdaContext context)
        {
            SocialPostDb post =
                await ReadAsync(
                    request.EffectiveOrganizationID,
                    request.SocialPostID,
                    context);

            if (post.Status != SocialPostStatus.Draft || (await GetDeliveriesAsync(post.OrganizationID, post.SocialPostID, context)).Count > 0)
                throw ResponseStatusFactory.BadRequest("Only an unsubmitted draft can be edited. Create a new post to change submitted content.");
            if (request.ScheduledDateUtc.HasValue || (!string.IsNullOrWhiteSpace(request.Status) && request.Status != SocialPostStatus.Draft))
                throw ResponseStatusFactory.BadRequest("Use Publish now. Delivery status is managed by the publishing service; scheduling is not enabled.");
            if (request.Platforms?.Count > 0) request.Platforms = NormalizePlatforms(request.Platforms);
            if (request.MetaSocial == null || request.MetaSocial.Any(o => o == null))
                throw ResponseStatusFactory.BadRequest("meta_social must be an array of platform settings.");

            if (post.Platforms.Contains(SocialPlatform.TikTok) ||
                request.Platforms?.Contains(SocialPlatform.TikTok) == true)
                throw ResponseStatusFactory.BadRequest("Create a new TikTok video post with fresh privacy settings and consent.");

            if (post.Platforms.Any(TextSocialPublishingService.Supports) ||
                request.Platforms?.Any(TextSocialPublishingService.Supports) == true)
                throw ResponseStatusFactory.BadRequest("Create a new X or LinkedIn post and confirm its destination before publishing.");

            var original = Newtonsoft.Json.JsonConvert.DeserializeObject<SocialPostDb>(Newtonsoft.Json.JsonConvert.SerializeObject(post))!;

            if (FormatValue.NotEmptyValue(request.ResourceType))
            {
                post.ResourceType =
                    request.ResourceType;
            }

            if (FormatValue.NotEmptyValue(request.ResourceID))
            {
                post.ResourceID =
                    request.ResourceID;
            }

            if (FormatValue.NotEmptyValue(request.OutreachItemID))
            {
                post.OutreachItemID =
                    request.OutreachItemID;
            }

            if (FormatValue.NotEmptyValue(request.Content))
            {
                post.Content =
                    request.Content;
            }

            if (FormatValue.NotEmptyValue(request.DestinationUrl))
            {
                post.DestinationUrl =
                    AddSocialPostTracking(
                        request.DestinationUrl,
                        post.SocialPostID);
            }

            if (request.Platforms?.Count > 0)
            {
                post.Platforms =
                    NormalizePlatforms(
                        request.Platforms);
            }

            if (FormatValue.NotEmptyValue(request.Status))
            {
                if (!SocialPostStatus.IsValid(
                        request.Status))
                {
                    throw ResponseStatusFactory.BadRequest(
                        "status is invalid.");
                }

                post.Status =
                    request.Status.Trim().ToLowerInvariant();
            }

            if (request.ScheduledDateUtc.HasValue)
            {
                post.ScheduledDateUtc =
                    request.ScheduledDateUtc;
            }

            post.LastUpdate =
                DefaultValue.UtcNow();

            if (request.MetaSocial?.Count > 0) post.MetaSocial = request.MetaSocial;
            foreach (var options in post.MetaSocial) options.Platform = options.Platform?.Trim().ToLowerInvariant() ?? "";
            if (post.Platforms.All(MetaSocialPublishingService.Supports))
                foreach (string platform in post.Platforms) MetaSocialPublishingService.Validate(post, platform);

            if (!await DbCRUD<SocialPostDb>.TryPatchAsync(post.SocialPostID, post.OrganizationID, original,
                new Dictionary<string, object>
                {
                    [nameof(post.Status)] = post.Status, [nameof(post.LastUpdate)] = post.LastUpdate,
                    [nameof(post.Content)] = post.Content, [nameof(post.DestinationUrl)] = post.DestinationUrl,
                    [nameof(post.Platforms)] = post.Platforms, [nameof(post.MetaSocial)] = post.MetaSocial,
                    [nameof(post.ResourceType)] = post.ResourceType, [nameof(post.ResourceID)] = post.ResourceID,
                    [nameof(post.OutreachItemID)] = post.OutreachItemID
                }, context))
                throw ResponseStatusFactory.BadRequest("The draft changed or was submitted in another request. Reload it before editing.");

            return post;
        }

        public async Task DeleteAsync(
            string organizationID,
            string socialPostID,
            ILambdaContext context)
        {
            await CancelAsync(organizationID, socialPostID, context);
        }

        public async Task<List<SocialPostDb>> GetByOrganizationAsync(
            string organizationID,
            ILambdaContext context)
        {
            ValidateOrganizationID(
                organizationID);

            List<SocialPostDb> posts =
                await DbCRUD<SocialPostDb>.ReadAsync(
                    context);

            return posts
                .Where(post =>
                    string.Equals(
                        post.OrganizationID,
                        organizationID,
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(post =>
                    post.CreateDate)
                .ToList();
        }

        public async Task<SocialPostDb> QueueForPublishAsync(
            string organizationID,
            string socialPostID,
            ILambdaContext context)
        {
            SocialPostDb post =
                await ReadAsync(
                    organizationID,
                    socialPostID,
                    context);

            if (string.Equals(
                    post.Status,
                    SocialPostStatus.Cancelled,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw ResponseStatusFactory.BadRequest(
                    "Cancelled social posts cannot be published.");
            }

            if (post.Platforms.Contains(SocialPlatform.TikTok))
                return await new TikTokPublishingService().PublishAsync(post, context);

            if (post.Platforms.Any(TextSocialPublishingService.Supports))
                return await new TextSocialPublishingService().PublishAsync(post, context);

            return await new MetaSocialPublishingService().PublishAsync(post, context);
        }

        public async Task<SocialPostDb> ScheduleAsync(
            ScheduleSocialPostRequest request,
            ILambdaContext context)
        {
            await ReadAsync(request.EffectiveOrganizationID, request.SocialPostID, context);
            throw ResponseStatusFactory.BadRequest("Scheduled delivery is not enabled. Use Publish now.");
        }
        public async Task<SocialPostDb> CancelAsync(
            string organizationID,
            string socialPostID,
            ILambdaContext context)
        {
            SocialPostDb post =
                await ReadAsync(
                    organizationID,
                    socialPostID,
                    context);

            List<SocialPostDeliveryDb> deliveries =
                await GetDeliveriesAsync(
                    organizationID,
                    socialPostID,
                    context);

            if (post.Status == SocialPostStatus.Publishing || deliveries.Any(d =>
                d.Status is "initializing" or "processing" or "publishing" or "published" or "submission_unknown" or "sent_to_inbox"))
                throw ResponseStatusFactory.BadRequest("This post has already been submitted. Check its status and manage it on the social platform.");
            var updated = DefaultValue.UtcNow();
            if (!await DbCRUD<SocialPostDb>.TryPatchAsync(post.SocialPostID, post.OrganizationID, post,
                new Dictionary<string, object> { [nameof(post.Status)] = SocialPostStatus.Cancelled, [nameof(post.LastUpdate)] = updated }, context))
                throw ResponseStatusFactory.BadRequest("This post changed in another request. Reload its status before cancelling.");
            post.Status = SocialPostStatus.Cancelled;
            post.LastUpdate = updated;

            foreach (SocialPostDeliveryDb delivery in deliveries
                         .Where(delivery =>
                             !string.Equals(
                                 delivery.Status,
                                 SocialPostDeliveryStatus.Published,
                                 StringComparison.OrdinalIgnoreCase)))
            {
                delivery.Status =
                    SocialPostDeliveryStatus.Cancelled;

                delivery.LastUpdate =
                    DefaultValue.UtcNow();

                await DbCRUD<SocialPostDeliveryDb>.SaveAsync(
                    delivery,
                    context);
            }

            return post;
        }

        public async Task<List<SocialPostDeliveryDb>> GetDeliveriesAsync(
            string organizationID,
            string socialPostID,
            ILambdaContext context)
        {
            var post = await ReadAsync(
                organizationID,
                socialPostID,
                context);

            List<SocialPostDeliveryDb> deliveries =
                await DbCRUD<SocialPostDeliveryDb>.ReadAsync(
                    context);

            foreach (string platform in post.Platforms)
            {
                var delivery = await DbCRUD<SocialPostDeliveryDb>.ReadConsistentAsync(platform + "-" + socialPostID, context);
                if (delivery == null || delivery.OrganizationID != organizationID) continue;
                deliveries.RemoveAll(d => d.DeliveryID == delivery.DeliveryID);
                deliveries.Add(delivery);
            }

            return deliveries
                .Where(delivery => delivery.OrganizationID == organizationID &&
                    string.Equals(
                        delivery.SocialPostID,
                        socialPostID,
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(delivery =>
                    delivery.CreateDate)
                .ToList();
        }

        public async Task<SocialPostAnalyticsSummary> GetAnalyticsSummaryAsync(
            string organizationID,
            string socialPostID,
            ILambdaContext context)
        {
            List<SocialPostDeliveryDb> deliveries =
                await GetDeliveriesAsync(
                    organizationID,
                    socialPostID,
                    context);

            return new SocialPostAnalyticsSummary
            {
                Queued =
                    CountDeliveries(
                        deliveries,
                        SocialPostDeliveryStatus.Queued),

                Publishing =
                    CountDeliveries(
                        deliveries,
                        SocialPostDeliveryStatus.Publishing),

                Published =
                    CountDeliveries(
                        deliveries,
                        SocialPostDeliveryStatus.Published),

                Failed =
                    CountDeliveries(
                        deliveries,
                        SocialPostDeliveryStatus.Failed),

                Cancelled =
                    CountDeliveries(
                        deliveries,
                        SocialPostDeliveryStatus.Cancelled),

                TotalDeliveries =
                    deliveries.Count
            };
        }

        private static int CountDeliveries(
            List<SocialPostDeliveryDb> deliveries,
            string status)
        {
            return deliveries.Count(delivery =>
                string.Equals(
                    delivery.Status,
                    status,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static string AddSocialPostTracking(
            string resourceUrl,
            string socialPostID)
        {
            if (FormatValue.EmptyValue(resourceUrl) ||
                FormatValue.EmptyValue(socialPostID))
            {
                return resourceUrl;
            }

            UriBuilder uriBuilder;

            try
            {
                uriBuilder =
                    new UriBuilder(
                        resourceUrl);
            }
            catch (UriFormatException)
            {
                return resourceUrl;
            }

            string query =
                uriBuilder.Query;

            if (query.StartsWith("?"))
            {
                query =
                    query.Substring(1);
            }

            List<string> queryParameters =
                query
                    .Split(
                        '&',
                        StringSplitOptions.RemoveEmptyEntries)
                    .Where(parameter =>
                        !parameter.StartsWith(
                            "socialpostid=",
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

            queryParameters.Add(
                $"socialpostid={Uri.EscapeDataString(socialPostID)}");

            uriBuilder.Query =
                string.Join(
                    "&",
                    queryParameters);

            return uriBuilder.Uri.ToString();
        }

        private static void ValidateCreateRequest(
            CreateSocialPostRequest request)
        {
            if (request == null)
            {
                throw ResponseStatusFactory.BadRequest(
                    "Request is required.");
            }

            ValidateOrganizationID(
                request.EffectiveOrganizationID);

            if (FormatValue.EmptyValue(request.Content) && request.TikTok == null)
            {
                throw ResponseStatusFactory.BadRequest(
                    "content is required.");
            }

            NormalizePlatforms(
                request.Platforms);
        }

        private static List<string> NormalizePlatforms(
            List<string> platforms)
        {
            if (platforms == null ||
                platforms.Count == 0)
            {
                throw ResponseStatusFactory.BadRequest(
                    "platforms is required.");
            }

            List<string> normalized =
                platforms
                    .Where(platform =>
                        FormatValue.NotEmptyValue(platform))
                    .Select(platform =>
                        platform.Trim().ToLowerInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            if (normalized.Count == 0)
            {
                throw ResponseStatusFactory.BadRequest(
                    "platforms is required.");
            }

            string? invalidPlatform =
                normalized
                    .FirstOrDefault(platform =>
                        !SocialPlatform.IsValid(platform));

            if (invalidPlatform != null)
            {
                throw ResponseStatusFactory.BadRequest(
                    $"platform '{invalidPlatform}' is invalid.");
            }

            return normalized;
        }

        private static void ValidateOrganizationID(
            string organizationID)
        {
            if (FormatValue.EmptyValue(organizationID))
            {
                throw ResponseStatusFactory.BadRequest(
                    "organization_id is required.");
            }
        }

        private static void EnsureOrganizationOwnsPost(
            string organizationID,
            SocialPostDb post)
        {
            ValidateOrganizationID(
                organizationID);

            if (!string.Equals(
                    post.OrganizationID,
                    organizationID,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw ResponseStatusFactory.Forbidden(
                    "Social post does not belong to this organization.");
            }
        }
    }
}
