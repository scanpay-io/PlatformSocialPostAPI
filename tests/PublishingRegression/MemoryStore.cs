using Newtonsoft.Json;
using ScanPay.DataModel.Model;
using ScanPay.SocialPostService;

internal sealed class MemoryStore : IMetaPublishingStore
{
    public Dictionary<string, SocialServiceProvider> Providers = new();
    public List<SocialConnectionDb> Connections = new();
    public Dictionary<string, SocialConnectionTokenDb> Tokens = new();
    public Dictionary<string, SocialPostDeliveryDb> Deliveries = new();
    public SocialPostDb? SavedPost;
    public int FailDeliverySaves;
    private readonly object gate = new();
    private static T Copy<T>(T value) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value))!;
    public Task<Dictionary<string, SocialServiceProvider>> ProvidersAsync() => Task.FromResult(Providers);
    public Task<List<SocialConnectionDb>> ConnectionsAsync(string organizationID) => Task.FromResult(Copy(Connections));
    public Task<SocialConnectionTokenDb?> TokenAsync(string id) => Task.FromResult(Tokens.TryGetValue(id, out var token) ? Copy(token) : null);
    public Task<List<SocialPostDeliveryDb>> DeliveriesAsync(SocialPostDb post)
    { lock (gate) return Task.FromResult(Copy(Deliveries.Values.Where(d => d.OrganizationID == post.OrganizationID && d.SocialPostID == post.SocialPostID).ToList())); }
    public Task<bool> BeginAsync(SocialPostDb post) => Task.FromResult(true);
    public Task<bool> CreateAsync(SocialPostDeliveryDb delivery)
    { lock (gate) return Task.FromResult(Deliveries.TryAdd(delivery.DeliveryID, Copy(delivery))); }
    public Task<bool> ClaimAsync(SocialPostDeliveryDb delivery)
    {
        lock (gate)
        {
            if (!Deliveries.TryGetValue(delivery.DeliveryID, out var current) || current.Status != delivery.Status) return Task.FromResult(false);
            current.Status = "publishing";
            return Task.FromResult(true);
        }
    }
    public Task SaveAsync(SocialPostDeliveryDb delivery)
    {
        lock (gate)
        {
            if (FailDeliverySaves-- > 0) throw new IOException("Simulated database outage after remote write.");
            Deliveries[delivery.DeliveryID] = Copy(delivery);
        }
        return Task.CompletedTask;
    }
    public Task SaveAsync(SocialPostDb post) { SavedPost = Copy(post); return Task.CompletedTask; }

    public static MemoryStore For(params string[] platforms)
    {
        var store = new MemoryStore();
        foreach (string platform in platforms)
        {
            store.Providers[platform] = new() { Supported = true, OAuthEnabled = true, PostingEnabled = true };
            store.Connections.Add(new() { SocialConnectionID = platform, OrganizationID = "org", Platform = platform,
                ExternalAccountID = "123", TokenSecretID = platform, Status = "connected" });
            store.Tokens[platform] = new() { SocialConnectionID = platform, OrganizationID = "org", Platform = platform,
                AccessToken = "user-token", TokenExpiresDateUtc = DateTime.UtcNow.AddHours(1) };
        }
        return store;
    }
}
