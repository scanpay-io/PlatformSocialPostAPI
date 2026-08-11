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

            var post =
                new SocialPostDb
                {
                    SocialPostID =
                        FormatValue.NewID(),

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
                        request.DestinationUrl,

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
                await DbCRUD<SocialPostDb>.ReadAsync(
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
                    request.DestinationUrl;
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

            await DbCRUD<SocialPostDb>.SaveAsync(
                post,
                context);

            return post;
        }

        public async Task DeleteAsync(
            string organizationID,
            string socialPostID,
            ILambdaContext context)
        {
            SocialPostDb post =
                await ReadAsync(
                    organizationID,
                    socialPostID,
                    context);

            post.Status =
                SocialPostStatus.Cancelled;

            post.LastUpdate =
                DefaultValue.UtcNow();

            await DbCRUD<SocialPostDb>.SaveAsync(
                post,
                context);
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

            List<SocialConnectionDb> connections =
                await new SocialConnectionService()
                    .GetByOrganizationAsync(
                        organizationID,
                        context);

            List<SocialPostDeliveryDb> deliveries =
                await QueueDeliveriesAsync(
                    post,
                    connections,
                    context);

            post.Status =
                deliveries.Any(delivery =>
                    string.Equals(
                        delivery.Status,
                        SocialPostDeliveryStatus.Queued,
                        StringComparison.OrdinalIgnoreCase))
                    ? SocialPostStatus.Queued
                    : SocialPostStatus.Failed;

            post.LastUpdate =
                DefaultValue.UtcNow();

            await DbCRUD<SocialPostDb>.SaveAsync(
                post,
                context);

            return post;
        }

        public async Task<SocialPostDb> ScheduleAsync(
            ScheduleSocialPostRequest request,
            ILambdaContext context)
        {
            if (!request.ScheduledDateUtc.HasValue ||
                request.ScheduledDateUtc.Value <= DefaultValue.UtcNow())
            {
                throw ResponseStatusFactory.BadRequest(
                    "scheduled_date_utc must be in the future.");
            }

            SocialPostDb post =
                await ReadAsync(
                    request.EffectiveOrganizationID,
                    request.SocialPostID,
                    context);

            post.ScheduledDateUtc =
                request.ScheduledDateUtc;

            post.Status =
                SocialPostStatus.Scheduled;

            post.LastUpdate =
                DefaultValue.UtcNow();

            await DbCRUD<SocialPostDb>.SaveAsync(
                post,
                context);

            return post;
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

            post.Status =
                SocialPostStatus.Cancelled;

            post.LastUpdate =
                DefaultValue.UtcNow();

            await DbCRUD<SocialPostDb>.SaveAsync(
                post,
                context);

            List<SocialPostDeliveryDb> deliveries =
                await GetDeliveriesAsync(
                    organizationID,
                    socialPostID,
                    context);

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
            await ReadAsync(
                organizationID,
                socialPostID,
                context);

            List<SocialPostDeliveryDb> deliveries =
                await DbCRUD<SocialPostDeliveryDb>.ReadAsync(
                    context);

            return deliveries
                .Where(delivery =>
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

        private static async Task<List<SocialPostDeliveryDb>> QueueDeliveriesAsync(
            SocialPostDb post,
            List<SocialConnectionDb> connections,
            ILambdaContext context)
        {
            List<SocialPostDeliveryDb> existingDeliveries =
                await DbCRUD<SocialPostDeliveryDb>.ReadAsync(
                    context);

            List<SocialPostDeliveryDb> postDeliveries =
                existingDeliveries
                    .Where(delivery =>
                        string.Equals(
                            delivery.SocialPostID,
                            post.SocialPostID,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

            var queuedDeliveries =
                new List<SocialPostDeliveryDb>();

            foreach (string platform in post.Platforms)
            {
                SocialPostDeliveryDb? existingDelivery =
                    postDeliveries.FirstOrDefault(delivery =>
                        string.Equals(
                            delivery.Platform,
                            platform,
                            StringComparison.OrdinalIgnoreCase));

                if (existingDelivery != null &&
                    (string.Equals(
                         existingDelivery.Status,
                         SocialPostDeliveryStatus.Queued,
                         StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(
                         existingDelivery.Status,
                         SocialPostDeliveryStatus.Publishing,
                         StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(
                         existingDelivery.Status,
                         SocialPostDeliveryStatus.Published,
                         StringComparison.OrdinalIgnoreCase)))
                {
                    queuedDeliveries.Add(
                        existingDelivery);

                    continue;
                }

                SocialConnectionDb? connection =
                    connections.FirstOrDefault(item =>
                        string.Equals(
                            item.Platform,
                            platform,
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(
                            item.Status,
                            SocialConnectionStatus.Connected,
                            StringComparison.OrdinalIgnoreCase));

                SocialPostDeliveryDb delivery =
                    existingDelivery
                    ?? new SocialPostDeliveryDb
                    {
                        DeliveryID =
                            FormatValue.NewID(),

                        SocialPostID =
                            post.SocialPostID,

                        OrganizationID =
                            post.OrganizationID,

                        Platform =
                            platform,

                        CreateDate =
                            DefaultValue.UtcNow()
                    };

                delivery.SocialConnectionID =
                    connection?.SocialConnectionID
                    ?? "missing_connection";

                delivery.Status =
                    connection == null
                        ? SocialPostDeliveryStatus.Failed
                        : SocialPostDeliveryStatus.Queued;

                delivery.ErrorCode =
                    connection == null
                        ? "missing_connection"
                        : DefaultValue.EMPTY_STRING;

                delivery.ErrorMessage =
                    connection == null
                        ? $"No connected {platform} social connection was found."
                        : DefaultValue.EMPTY_STRING;

                delivery.LastUpdate =
                    DefaultValue.UtcNow();

                await DbCRUD<SocialPostDeliveryDb>.SaveAsync(
                    delivery,
                    context);

                queuedDeliveries.Add(
                    delivery);
            }

            return queuedDeliveries;
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

            if (FormatValue.EmptyValue(request.Content))
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
