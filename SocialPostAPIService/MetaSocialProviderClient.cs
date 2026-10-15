using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static ScanPay.Utility.Model.ResponseStatusException;

namespace ScanPay.SocialPostService
{
    // Never expose this type in an API response; Page tokens stay on the server.
    public record MetaPageCredential(string ID, string Name, string AccessToken);

    public class MetaSocialProviderClient
    {
        private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(8) };
        private readonly HttpClient client;
        public MetaSocialProviderClient(HttpClient? client = null) => this.client = client ?? SharedClient;

        private static string Root(string platform) => platform switch
        {
            SocialPlatform.Facebook => "https://graph.facebook.com/v25.0/",
            SocialPlatform.Instagram => "https://graph.instagram.com/v25.0/",
            SocialPlatform.Threads => "https://graph.threads.net/v1.0/",
            _ => throw new ArgumentException("Unsupported Meta platform.")
        };

        public static string ID(string value) => Regex.IsMatch(value ?? "", @"^[0-9]+(?:_[0-9]+)?$")
            ? value! : throw ResponseStatusFactory.BadRequest("The platform returned an invalid account or post ID.");

        private async Task<JObject> SendAsync(string platform, string token, string path,
            Dictionary<string, string>? form = null)
        {
            using var request = new HttpRequestMessage(form == null ? HttpMethod.Get : HttpMethod.Post, Root(platform) + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (form != null) request.Content = new FormUrlEncodedContent(form);
            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                throw ResponseStatusFactory.BadRequest($"{platform} rejected the request (HTTP {(int)response.StatusCode}). Check permissions, content and rate limits; reconnect if the token expired.");
            var body = JObject.Parse(await response.Content.ReadAsStringAsync());
            if (body["error"] != null)
                throw ResponseStatusFactory.BadRequest($"{platform} rejected the request. Check permissions and content.");
            return body;
        }

        public async Task<List<MetaPageCredential>> FacebookPagesAsync(string token)
        {
            var pages = new List<MetaPageCredential>();
            string after = "";
            for (int page = 0; page < 20; page++)
            {
                var result = await SendAsync(SocialPlatform.Facebook, token,
                    "me/accounts?fields=id,name,access_token,tasks&limit=100" +
                    (after.Length == 0 ? "" : "&after=" + Uri.EscapeDataString(after)));
                foreach (var item in result["data"] as JArray ?? new JArray())
                {
                    var tasks = item["tasks"]?.Values<string>().ToArray() ?? Array.Empty<string>();
                    if (!tasks.Contains("CREATE_CONTENT") && !tasks.Contains("PROFILE_PLUS_CREATE_CONTENT")) continue;
                    string pageToken = item.Value<string>("access_token") ?? "";
                    if (string.IsNullOrWhiteSpace(pageToken)) continue;
                    pages.Add(new MetaPageCredential(ID(item.Value<string>("id") ?? ""), item.Value<string>("name") ?? "Facebook Page", pageToken));
                }
                if (string.IsNullOrWhiteSpace(result["paging"]?.Value<string>("next"))) return pages;
                string cursor = result["paging"]?["cursors"]?.Value<string>("after") ?? "";
                if (string.IsNullOrWhiteSpace(cursor) || cursor == after) break;
                // Reconstruct the URL; never follow a provider-supplied URL with a bearer token.
                after = cursor;
            }
            throw ResponseStatusFactory.BadRequest("Facebook returned too many Pages or incomplete pagination.");
        }

        public async Task<string> PublishFacebookAsync(string pageID, string pageToken, string text, string link, string imageUrl)
        {
            var form = new Dictionary<string, string>();
            string endpoint;
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                endpoint = "/feed";
                form["message"] = text;
                if (!string.IsNullOrWhiteSpace(link)) form["link"] = link;
            }
            else
            {
                endpoint = "/photos";
                form["url"] = imageUrl;
                form["caption"] = text + (string.IsNullOrWhiteSpace(link) ? "" : "\n\n" + link);
                form["published"] = "true";
            }
            var result = await SendAsync(SocialPlatform.Facebook, pageToken, ID(pageID) + endpoint, form);
            return ID(result.Value<string>("post_id") ?? result.Value<string>("id") ?? "");
        }

        public async Task<string> CreateContainerAsync(string platform, string accountID, string token, string text, string imageUrl)
        {
            if (platform != SocialPlatform.Instagram && platform != SocialPlatform.Threads)
                throw new ArgumentException("This platform does not use media containers.");
            var form = new Dictionary<string, string>();
            if (platform == SocialPlatform.Instagram)
            {
                form["image_url"] = imageUrl;
                form["caption"] = text;
            }
            else
            {
                form["media_type"] = string.IsNullOrWhiteSpace(imageUrl) ? "TEXT" : "IMAGE";
                form["text"] = text;
                if (!string.IsNullOrWhiteSpace(imageUrl)) form["image_url"] = imageUrl;
            }
            var result = await SendAsync(platform, token, ID(accountID) +
                (platform == SocialPlatform.Instagram ? "/media" : "/threads"), form);
            return ID(result.Value<string>("id") ?? "");
        }

        public async Task<string> ContainerStatusAsync(string platform, string token, string containerID)
        {
            string field = platform == SocialPlatform.Instagram ? "status_code" : "status";
            return (await SendAsync(platform, token, ID(containerID) + "?fields=" + field)).Value<string>(field) ?? "";
        }

        public async Task<string> PublishContainerAsync(string platform, string accountID, string token, string containerID)
        {
            var result = await SendAsync(platform, token, ID(accountID) +
                (platform == SocialPlatform.Instagram ? "/media_publish" : "/threads_publish"),
                new Dictionary<string, string> { ["creation_id"] = ID(containerID) });
            return ID(result.Value<string>("id") ?? "");
        }
    }
}
