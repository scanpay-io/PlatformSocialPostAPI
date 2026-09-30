using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static ScanPay.Utility.Model.ResponseStatusException;

namespace ScanPay.SocialPostService
{
    public record SocialPage(string ID, string Name);
    public record SocialPublishedResult(string ID, string Url);

    public class TextSocialProviderClient
    {
        private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(20) };
        private readonly HttpClient client;
        public TextSocialProviderClient(HttpClient? client = null) => this.client = client ?? SharedClient;

        private static HttpRequestMessage Request(HttpMethod method, string url, string token, bool linkedIn)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (linkedIn)
            {
                request.Headers.Add("LinkedIn-Version", Environment.GetEnvironmentVariable("SOCIAL_LINKEDIN_API_VERSION") ?? "202609");
                request.Headers.Add("X-Restli-Protocol-Version", "2.0.0");
            }
            return request;
        }

        private static void EnsureSuccess(HttpResponseMessage response, string platform)
        {
            if (response.IsSuccessStatusCode) return;
            string reason = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "The connection expired or was revoked. Reconnect the account.",
                HttpStatusCode.Forbidden => "Check the app's approved product permissions and the user's Page role.",
                HttpStatusCode.PaymentRequired => "The application's API billing or credits must be configured in the provider console.",
                HttpStatusCode.TooManyRequests => "The platform rate limit was reached. Wait before trying again.",
                _ => "The platform rejected the request. Check the content and application permissions."
            };
            throw ResponseStatusFactory.BadRequest($"{platform}: {reason}");
        }

        private async Task<JObject> GetAsync(string url, string token, bool linkedIn)
        {
            using var request = Request(HttpMethod.Get, url, token, linkedIn);
            using var response = await client.SendAsync(request);
            EnsureSuccess(response, linkedIn ? "LinkedIn" : "X");
            return JObject.Parse(await response.Content.ReadAsStringAsync());
        }

        public static bool CanPublishPage(JToken acl) =>
            acl.Value<string>("state") == "APPROVED" &&
            (acl.Value<string>("role") == "ADMINISTRATOR" || acl.Value<string>("role") == "CONTENT_ADMINISTRATOR");

        private async Task<List<JToken>> LinkedInAclsAsync(string token)
        {
            var result = new List<JToken>();
            for (int start = 0; start < 1000;)
            {
                var page = await GetAsync($"https://api.linkedin.com/rest/organizationAcls?q=roleAssignee&state=APPROVED&count=100&start={start}", token, true);
                var elements = page["elements"] as JArray ?? new JArray();
                result.AddRange(elements.Where(CanPublishPage));
                bool hasNext = page["paging"]?["links"]?.Any(link => link.Value<string>("rel") == "next") == true;
                if (!hasNext) return result;
                int count = page["paging"]?.Value<int>("count") ?? elements.Count;
                if (count <= 0) break;
                start += count;
            }
            throw ResponseStatusFactory.BadRequest("LinkedIn returned too many Pages to load safely. Contact support.");
        }

        private static string PageUrn(JToken acl) => acl.Value<string>("organization") ?? acl.Value<string>("organizationTarget") ?? "";

        public async Task<SocialPage> GetIdentityAsync(string platform, string token)
        {
            if (platform == SocialPlatform.X)
            {
                var profile = (await GetAsync("https://api.x.com/2/users/me", token, false))["data"];
                string? id = profile?.Value<string>("id");
                if (string.IsNullOrWhiteSpace(id)) throw ResponseStatusFactory.BadRequest("X did not return an account identity.");
                return new SocialPage(id, "@" + profile!.Value<string>("username"));
            }
            var acls = await LinkedInAclsAsync(token);
            string? person = acls.FirstOrDefault()?.Value<string>("roleAssignee");
            if (string.IsNullOrWhiteSpace(person))
                throw ResponseStatusFactory.BadRequest("No manageable LinkedIn Pages were found. Connect as a Page administrator after Community Management access is approved.");
            return new SocialPage(person, "LinkedIn Pages");
        }

        public async Task<List<SocialPage>> LinkedInPagesAsync(string token)
        {
            var pages = new List<SocialPage>();
            foreach (string urn in (await LinkedInAclsAsync(token)).Select(PageUrn).Distinct())
            {
                if (!Regex.IsMatch(urn, @"^urn:li:organization:[0-9]+$")) continue;
                string id = urn.Split(':').Last();
                var page = await GetAsync("https://api.linkedin.com/rest/organizations/" + id, token, true);
                pages.Add(new SocialPage(urn, page.Value<string>("localizedName") ?? "LinkedIn Page " + id));
            }
            return pages;
        }

        public async Task VerifyLinkedInPageAsync(string token, string pageUrn)
        {
            if (!Regex.IsMatch(pageUrn, @"^urn:li:organization:[0-9]+$") ||
                !(await LinkedInAclsAsync(token)).Any(acl => PageUrn(acl) == pageUrn))
                throw ResponseStatusFactory.BadRequest("You no longer have permission to publish to the selected LinkedIn Page. Reload the available Pages.");
        }

        public static JObject PostBody(string platform, string text, string pageUrn)
        {
            if (platform == SocialPlatform.X) return new JObject { ["text"] = text };
            if (platform != SocialPlatform.LinkedIn) throw new ArgumentException("Unsupported publishing platform.");
            return new JObject
            {
                ["author"] = pageUrn, ["commentary"] = text, ["visibility"] = "PUBLIC",
                ["distribution"] = new JObject { ["feedDistribution"] = "MAIN_FEED", ["targetEntities"] = new JArray(), ["thirdPartyDistributionChannels"] = new JArray() },
                ["lifecycleState"] = "PUBLISHED", ["isReshareDisabledByAuthor"] = false
            };
        }

        public async Task<SocialPublishedResult> PublishAsync(string platform, string token, string text, string pageUrn)
        {
            bool linkedIn = platform == SocialPlatform.LinkedIn;
            using var request = Request(HttpMethod.Post,
                linkedIn ? "https://api.linkedin.com/rest/posts" : "https://api.x.com/2/tweets", token, linkedIn);
            request.Content = new StringContent(PostBody(platform, text, pageUrn).ToString(), Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request);
            EnsureSuccess(response, linkedIn ? "LinkedIn" : "X");
            string? id;
            if (linkedIn)
                id = response.Headers.TryGetValues("x-restli-id", out var ids) ? ids.FirstOrDefault() : null;
            else
                id = JObject.Parse(await response.Content.ReadAsStringAsync())["data"]?.Value<string>("id");
            if (string.IsNullOrWhiteSpace(id))
                throw ResponseStatusFactory.BadRequest("The platform accepted the request but did not return a post ID. Check the account before retrying.");
            return new SocialPublishedResult(id, linkedIn
                ? "https://www.linkedin.com/feed/update/" + Uri.EscapeDataString(id) + "/"
                : "https://x.com/i/status/" + Uri.EscapeDataString(id));
        }
    }
}
