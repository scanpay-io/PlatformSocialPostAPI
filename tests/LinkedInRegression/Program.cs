using System.Net;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ScanPay.SocialPostService;

int checks = 0;
void Check(bool ok) { if (!ok) throw new Exception("Check failed: " + (checks + 1)); checks++; }
void Reject(Action action) { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected); }
object? OAuth(string name, params object?[] args) => typeof(SocialOAuthService).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
var scopes = (string)OAuth("ResolveScopes", new ScanPay.DataModel.Model.SocialServiceProvider { Scopes = "r_organization_admin,w_organization_social" }, "linkedin")!;
Check(scopes.Split(' ').ToHashSet().SetEquals(new[] { "rw_organization_admin", "w_organization_social" }));
OAuth("ValidateLinkedInAuthorization", scopes, 3600); checks++;
Reject(() => OAuth("ValidateLinkedInAuthorization", "r_organization_admin", 3600));
Reject(() => OAuth("ValidateLinkedInAuthorization", scopes, null));
Reject(() => OAuth("ValidateLinkedInAuthorization", scopes, 0));
var state = SocialOAuthState.Create("org", "secret", "linkedin");
SocialOAuthState.Validate(state, "org", "secret", "linkedin"); checks++;
Reject(() => SocialOAuthState.Validate(state, "other-org", "secret", "linkedin"));
var post = new SocialPostDb { Platforms = new() { "linkedin" }, Content = new string('a', 3000), TextSocial = new() { ConnectionID = "connection", LinkedInPageUrn = "urn:li:organization:123", Consent = true } };
Check(TextSocialPublishingService.Validate(post).Length == 3000);
post.DestinationUrl = "https://example.org/give";
Reject(() => TextSocialPublishingService.Validate(post));
post.Content = "Approved post";
Check(TextSocialPublishingService.Validate(post).EndsWith(post.DestinationUrl));
post.TextSocial.LinkedInPageUrn = "urn:li:person:123";
Reject(() => TextSocialPublishingService.Validate(post));
post.TextSocial = JsonConvert.DeserializeObject<TextSocialPostOptions>("{'connection_id':'connection','linkedin_page_urn':'urn:li:organization:123','consent':true,'image_url':'https://example.org/a.jpg'}")!;
Reject(() => TextSocialPublishingService.Validate(post));
post.TextSocial.AdditionalOptions!.Clear();
post.TextSocial.Consent = false;
Reject(() => TextSocialPublishingService.Validate(post));
int calls = 0;
using var http = new HttpClient(new ProviderHttpHandler(async request => {
    Check(request.Headers.Authorization?.Parameter == "secret-token");
    Check(request.Headers.Contains("LinkedIn-Version"));
    calls++;
    if (request.Method == HttpMethod.Get) {
        bool second = request.RequestUri!.Query.Contains("start=100");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(second
            ? "{'elements':[{'state':'APPROVED','role':'ADMINISTRATOR','organization':'urn:li:organization:123'}]}"
            : "{'elements':[{'state':'REVOKED','role':'ADMINISTRATOR','organization':'urn:li:organization:123'}],'paging':{'count':100,'links':[{'rel':'next'}]}}") };
    }
    var body = JObject.Parse(await request.Content!.ReadAsStringAsync());
    Check(body.Value<string>("author") == "urn:li:organization:123");
    Check(body.Value<string>("commentary") == "Approved post");
    var response = new HttpResponseMessage(HttpStatusCode.Created);
    response.Headers.Add("x-restli-id", "urn:li:share:456");
    return response;
}));
var client = new TextSocialProviderClient(http);
await client.VerifyLinkedInPageAsync("secret-token", "urn:li:organization:123");
Check(calls == 2);
var published = await client.PublishAsync("linkedin", "secret-token", "Approved post", "urn:li:organization:123");
Check(published.ID == "urn:li:share:456" && published.Url.Contains("456"));
using var missingIdHttp = new HttpClient(new ProviderHttpHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created))));
bool missingIdRejected = false;
try { await new TextSocialProviderClient(missingIdHttp).PublishAsync("linkedin", "secret-token", "Approved post", "urn:li:organization:123"); } catch { missingIdRejected = true; }
Check(missingIdRejected);
Console.WriteLine($"LinkedIn regression passed: {checks} checks.");

