using System.Net;
using Newtonsoft.Json.Linq;
using ScanPay.SocialPostService;
using ScanPay.Utility.Model;

int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); Interlocked.Increment(ref checks); }
async Task Reject(Func<Task> action, string message)
{
    try { await action(); } catch (ResponseStatusException) { checks++; return; }
    throw new Exception(message);
}
static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
static SocialPostDb Post(params string[] platforms) => new()
{
    SocialPostID = "post", OrganizationID = "org", Content = "Support our campaign", DestinationUrl = "https://example.org/give",
    Platforms = platforms.ToList(), MetaSocial = platforms.Select(p => new MetaSocialPostOptions
    { Platform = p, ImageUrl = p == "instagram" ? "https://example.org/photo.jpg" : "" }).ToList()
};
static async Task<Dictionary<string, string>> Form(HttpRequestMessage request) => (await request.Content!.ReadAsStringAsync())
    .Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));

// Full multi-platform workflow: Page discovery, direct Facebook send, containers and status polling.
var store = MemoryStore.For("facebook", "instagram", "threads");
int sends = 0;
using var http = new HttpClient(new ProviderHttpHandler(async request =>
{
    string path = request.RequestUri!.AbsolutePath;
    string host = request.RequestUri.Host;
    Check(request.RequestUri.Scheme == "https" && !request.RequestUri.Query.Contains("access_token="), "Credentials must not be in URLs");
    if (path.EndsWith("/accounts"))
    {
        Check(request.Headers.Authorization?.Parameter == "user-token", "Page lookup uses user token");
        return Json("{'data':[{'id':'456','name':'Nonprofit','access_token':'page-token','tasks':['CREATE_CONTENT']}]} ");
    }
    if (request.Method == HttpMethod.Get)
        return Json(host == "graph.instagram.com" ? "{'status_code':'FINISHED'}" : "{'status':'FINISHED'}");
    Interlocked.Increment(ref sends);
    var form = await Form(request);
    if (path.EndsWith("/feed"))
    {
        Check(path == "/v25.0/456/feed" && request.Headers.Authorization?.Parameter == "page-token", "Facebook posts as the selected Page");
        Check(form["message"] == "Support our campaign" && form["link"] == "https://example.org/give", "Facebook link contract");
        return Json("{'id':'456_789'}");
    }
    Check(request.Headers.Authorization?.Parameter == "user-token", "Instagram/Threads use their own user token");
    if (path.EndsWith("/media"))
    {
        Check(host == "graph.instagram.com" && form["image_url"].EndsWith("photo.jpg") && form["caption"].Contains("/give"), "Instagram Login image payload");
        return Json("{'id':'111'}");
    }
    if (path.EndsWith("/threads"))
    {
        Check(host == "graph.threads.net" && form["media_type"] == "TEXT" && form["text"].Contains("/give"), "Threads text payload");
        return Json("{'id':'222'}");
    }
    Check(form["creation_id"] == (host == "graph.instagram.com" ? "111" : "222"), "Publish uses saved container ID");
    return Json("{'id':'999'}");
}));
var service = new MetaSocialPublishingService(new(http), store);
var post = Post("facebook", "instagram", "threads");
await service.PublishAsync(post, null!);
Check(store.Deliveries["facebook-post"].Status == "published", "Facebook must confirm publication");
Check(store.Deliveries["instagram-post"].ContainerID == "111" && store.Deliveries["threads-post"].Status == "processing", "Containers are saved for polling");
Check(post.Status == "publishing" && sends == 3, "Initialization is not publication");
await service.PublishAsync(post, null!);
Check(sends == 3, "Repeated publish must not duplicate remote sends");
await Task.WhenAll(service.RefreshStatusAsync(post, null!), service.RefreshStatusAsync(post, null!));
Check(sends == 5 && store.Deliveries.Values.All(d => d.Status == "published"), "Concurrent polls publish each container once");
await service.RefreshStatusAsync(post, null!);
Check(sends == 5 && post.Status == "published" && post.PublishedDateUtc.HasValue, "Published containers are never sent again");
var safePages = await service.FacebookPagesAsync(store.Connections.First(), null!);
Check(!Newtonsoft.Json.JsonConvert.SerializeObject(safePages).Contains("page-token"), "Page tokens never enter frontend responses");

foreach (string platform in new[] { "facebook", "instagram", "threads" })
{
    var badStore = MemoryStore.For(platform);
    badStore.Providers[platform].PostingEnabled = false;
    await Reject(() => new MetaSocialPublishingService(new(http), badStore).PublishAsync(Post(platform), null!), "Disabled provider must reject");
    badStore.Providers[platform].PostingEnabled = true;
    badStore.Tokens[platform].OrganizationID = "other";
    await Reject(() => new MetaSocialPublishingService(new(http), badStore).PublishAsync(Post(platform), null!), "Cross-tenant token must reject");
    badStore.Tokens[platform].OrganizationID = "org";
    badStore.Tokens[platform].TokenExpiresDateUtc = DateTime.UtcNow.AddMinutes(-1);
    await Reject(() => new MetaSocialPublishingService(new(http), badStore).PublishAsync(Post(platform), null!), "Expired token must reject");
    Check(badStore.Deliveries.Count == 0, "Preflight failure must not create deliveries");
}
var invalid = Post("instagram");
invalid.MetaSocial.Clear();
await Reject(() => Task.FromResult(MetaSocialPublishingService.Validate(invalid, "instagram")), "Instagram text-only post must reject");
invalid = Post("threads"); invalid.Content = new string('x', 501);
await Reject(() => Task.FromResult(MetaSocialPublishingService.Validate(invalid, "threads")), "Threads text length must reject");
invalid = Post("instagram"); invalid.MetaSocial[0].ImageUrl = "http://localhost/photo.jpg";
await Reject(() => Task.FromResult(MetaSocialPublishingService.Validate(invalid, "instagram")), "Invalid image URL must reject");
invalid = Post("threads", "x");
await Reject(() => Task.FromResult(MetaSocialPublishingService.Validate(invalid, "threads")), "Mixed dedicated platforms must reject");
invalid = Post("threads"); invalid.MetaSocial.Add(new() { Platform = "threads" });
await Reject(() => Task.FromResult(MetaSocialPublishingService.Validate(invalid, "threads")), "Duplicate platform settings must reject");

// Provider timeout and storage failure must never cause an automatic resend.
foreach (bool databaseFailure in new[] { false, true })
{
    var failureStore = MemoryStore.For("threads");
    failureStore.FailDeliverySaves = databaseFailure ? 1 : 0;
    int attempts = 0;
    using var failureHttp = new HttpClient(new ProviderHttpHandler(_ =>
    {
        attempts++;
        if (!databaseFailure) throw new TaskCanceledException("timeout");
        return Task.FromResult(Json("{'id':'333'}"));
    }));
    var failureService = new MetaSocialPublishingService(new(failureHttp), failureStore);
    var failurePost = Post("threads");
    await failureService.PublishAsync(failurePost, null!);
    await failureService.PublishAsync(failurePost, null!);
    Check(attempts == 1 && failurePost.Status == "submission_unknown", "Ambiguous initialization must not retry");
}
foreach (string status in new[] { "IN_PROGRESS", "ERROR", "EXPIRED" })
{
    var statusStore = MemoryStore.For("instagram");
    statusStore.Deliveries["instagram-post"] = new() { DeliveryID = "instagram-post", SocialPostID = "post", OrganizationID = "org",
        Platform = "instagram", SocialConnectionID = "instagram", TargetAccountID = "123", ContainerID = "333", Status = "processing" };
    using var statusHttp = new HttpClient(new ProviderHttpHandler(request =>
    {
        Check(request.Method == HttpMethod.Get, "Unready/failed containers must not publish");
        return Task.FromResult(Json("{'status_code':'" + status + "'}"));
    }));
    var statusPost = Post("instagram");
    await new MetaSocialPublishingService(new(statusHttp), statusStore).RefreshStatusAsync(statusPost, null!);
    Check(statusStore.Deliveries.Values.Single().Status == (status == "IN_PROGRESS" ? "processing" : "failed"), "Container state must be reflected accurately");
}
using var rejectedHttp = new HttpClient(new ProviderHttpHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
{ Content = new StringContent("secret-token-diagnostic") })));
try { await new MetaSocialProviderClient(rejectedHttp).CreateContainerAsync("threads", "123", "token", "text", ""); }
catch (ResponseStatusException ex) { Check(!ex.Message.Contains("secret-token"), "Provider diagnostics must not leak"); }
Check(MetaSocialPublishingService.AggregateStatus(new[] { new SocialPostDeliveryDb { Status = "published" }, new SocialPostDeliveryDb { Status = "failed" } }, 2) == "partially_published", "Partial failure aggregation");

using var imageHttp = new HttpClient(new ProviderHttpHandler(async request =>
{
    var form = await Form(request);
    if (request.RequestUri!.Host == "graph.facebook.com")
        Check(request.RequestUri.AbsolutePath.EndsWith("/photos") && form["url"] == "https://example.org/photo.jpg" &&
            form["caption"].Contains("https://example.org/give") && form["published"] == "true", "Facebook image contract includes campaign link");
    else Check(form["media_type"] == "IMAGE" && form["image_url"] == "https://example.org/photo.jpg", "Threads image contract");
    return Json("{'id':'555','post_id':'123_555'}");
}));
Check(await new MetaSocialProviderClient(imageHttp).PublishFacebookAsync("123", "token", "Caption", "https://example.org/give", "https://example.org/photo.jpg") == "123_555", "Facebook photo result uses post ID");
await new MetaSocialProviderClient(imageHttp).CreateContainerAsync("threads", "123", "token", "Caption", "https://example.org/photo.jpg");

int pageRequests = 0;
using var pagesHttp = new HttpClient(new ProviderHttpHandler(request =>
{
    pageRequests++;
    Check(request.RequestUri!.Host == "graph.facebook.com", "Pagination cannot exfiltrate token to next URL");
    if (pageRequests == 1) return Task.FromResult(Json("{'data':[{'id':'1','access_token':'secret','tasks':['ANALYZE']}], 'paging':{'next':'https://evil.example/steal','cursors':{'after':'cursor&x=1'}}}"));
    Check(request.RequestUri.Query.Contains("after=cursor%26x%3D1"), "Pagination cursor must be escaped");
    return Task.FromResult(Json("{'data':[{'id':'2','name':'Page','access_token':'secret','tasks':['CREATE_CONTENT']}]}"));
}));
Check((await new MetaSocialProviderClient(pagesHttp).FacebookPagesAsync("token")).Single().ID == "2", "Analytics-only Page role excluded");

var ambiguousStore = MemoryStore.For("facebook");
using var ambiguousHttp = new HttpClient(new ProviderHttpHandler(request =>
{
    Check(request.Method == HttpMethod.Get, "Ambiguous Page selection must never send a post");
    return Task.FromResult(Json("{'data':[{'id':'1','access_token':'a','tasks':['CREATE_CONTENT']},{'id':'2','access_token':'b','tasks':['CREATE_CONTENT']}]}"));
}));
await Reject(() => new MetaSocialPublishingService(new(ambiguousHttp), ambiguousStore).PublishAsync(Post("facebook"), null!), "Multiple Pages require explicit selection");
var wrongPage = Post("facebook"); wrongPage.MetaSocial[0].FacebookPageID = "999";
await Reject(() => new MetaSocialPublishingService(new(ambiguousHttp), ambiguousStore).PublishAsync(wrongPage, null!), "Unmanaged Page must reject");

using var noIdHttp = new HttpClient(new ProviderHttpHandler(_ => Task.FromResult(Json("{}"))));
await Reject(() => new MetaSocialProviderClient(noIdHttp).CreateContainerAsync("threads", "123", "token", "Caption", ""), "Missing container ID must reject");
await Reject(() => new MetaSocialProviderClient(noIdHttp).PublishContainerAsync("instagram", "123", "token", "456"), "Missing publication ID must reject");

var timeoutStore = MemoryStore.For("threads");
timeoutStore.Deliveries["threads-post"] = new() { DeliveryID = "threads-post", SocialPostID = "post", OrganizationID = "org", Platform = "threads",
    SocialConnectionID = "threads", TargetAccountID = "123", ContainerID = "444", Status = "processing" };
int publishAttempts = 0;
using var timeoutHttp = new HttpClient(new ProviderHttpHandler(request =>
{
    if (request.Method == HttpMethod.Get) return Task.FromResult(Json("{'status':'FINISHED'}"));
    publishAttempts++;
    throw new TaskCanceledException();
}));
var timeoutService = new MetaSocialPublishingService(new(timeoutHttp), timeoutStore);
var timeoutPost = Post("threads");
await timeoutService.RefreshStatusAsync(timeoutPost, null!);
await timeoutService.RefreshStatusAsync(timeoutPost, null!);
Check(publishAttempts == 1 && timeoutPost.Status == "submission_unknown", "Ambiguous final publish cannot be retried by polling");

var legacyStore = MemoryStore.For("threads");
legacyStore.Deliveries["old"] = new() { DeliveryID = "old", SocialPostID = "post", OrganizationID = "org", Platform = "threads", Status = "queued" };
await Reject(() => new MetaSocialPublishingService(new(http), legacyStore).PublishAsync(Post("threads"), null!), "Retired queue records need explicit recreation");
var cancelled = Post("threads"); cancelled.Status = "cancelled";
await Reject(() => service.PublishAsync(cancelled, null!), "Cancelled post must reject");
Console.WriteLine($"Passed {checks} Meta publishing regression checks.");
