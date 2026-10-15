using Amazon.Lambda.Core;
using ScanPay.DataModel.Model;
using ScanPay.Utility.Model;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ScanPay.SocialPostService
{
    public interface IMetaPublishingStore
    {
        Task<Dictionary<string, SocialServiceProvider>> ProvidersAsync();
        Task<List<SocialConnectionDb>> ConnectionsAsync(string organizationID);
        Task<SocialConnectionTokenDb?> TokenAsync(string id);
        Task<List<SocialPostDeliveryDb>> DeliveriesAsync(SocialPostDb post);
        Task<bool> BeginAsync(SocialPostDb post);
        Task<bool> CreateAsync(SocialPostDeliveryDb delivery);
        Task<bool> ClaimAsync(SocialPostDeliveryDb delivery);
        Task SaveAsync(SocialPostDeliveryDb delivery);
        Task SaveAsync(SocialPostDb post);
    }

    internal sealed class MetaPublishingStore(ILambdaContext context) : IMetaPublishingStore
    {
        public async Task<Dictionary<string, SocialServiceProvider>> ProvidersAsync() =>
            await AppConfigSettings.GetSocialServiceProvidersAsync(context) ?? new();
        public Task<List<SocialConnectionDb>> ConnectionsAsync(string organizationID) =>
            new SocialConnectionService().GetByOrganizationAsync(organizationID, context);
        public async Task<SocialConnectionTokenDb?> TokenAsync(string id) =>
            await DbCRUD<SocialConnectionTokenDb>.ReadConsistentAsync(id, context);
        public Task<List<SocialPostDeliveryDb>> DeliveriesAsync(SocialPostDb post) =>
            new SocialPostService().GetDeliveriesAsync(post.OrganizationID, post.SocialPostID, context);
        public Task<bool> BeginAsync(SocialPostDb post) => DbCRUD<SocialPostDb>.TryPatchAsync(
            post.SocialPostID, post.OrganizationID, post, new Dictionary<string, object>
            { [nameof(post.Status)] = SocialPostStatus.Publishing, [nameof(post.LastUpdate)] = System.DateTime.UtcNow }, context);
        public Task<bool> CreateAsync(SocialPostDeliveryDb delivery) => DbCRUD<SocialPostDeliveryDb>.TryCreateAsync(delivery, context);
        public Task<bool> ClaimAsync(SocialPostDeliveryDb delivery) => DbCRUD<SocialPostDeliveryDb>.TryPatchAsync(
            delivery.DeliveryID, delivery.OrganizationID, delivery,
            new Dictionary<string, object> { [nameof(delivery.Status)] = "publishing" }, context);
        public async Task SaveAsync(SocialPostDeliveryDb delivery) => await DbCRUD<SocialPostDeliveryDb>.SaveAsync(delivery, context);
        public async Task SaveAsync(SocialPostDb post)
        {
            var current = await DbCRUD<SocialPostDb>.ReadConsistentAsync(post.SocialPostID, context);
            if (current == null || current.OrganizationID != post.OrganizationID || current.Status == SocialPostStatus.Cancelled) return;
            var deliveries = (await DeliveriesAsync(post)).Where(d => post.Platforms.Contains(d.Platform)).ToList();
            post.Status = MetaSocialPublishingService.AggregateStatus(deliveries, post.Platforms.Count);
            if (post.Status == SocialPostStatus.Published) post.PublishedDateUtc = deliveries.Max(d => d.PublishedDateUtc);
            var changes = new Dictionary<string, object> { [nameof(post.Status)] = post.Status, [nameof(post.LastUpdate)] = post.LastUpdate };
            if (post.PublishedDateUtc.HasValue) changes[nameof(post.PublishedDateUtc)] = post.PublishedDateUtc.Value;
            await DbCRUD<SocialPostDb>.TryPatchAsync(post.SocialPostID, post.OrganizationID, current, changes, context);
        }
    }
}
