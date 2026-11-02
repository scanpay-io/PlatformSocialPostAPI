using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using static ScanPay.Utility.Model.ResponseStatusException;

namespace ScanPay.SocialPostService
{
    public record MetaLongLivedToken(string AccessToken, DateTime ExpiresDateUtc);

    public sealed class MetaUserTokenClient
    {
        private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(8) };
        private readonly HttpClient client;
        public MetaUserTokenClient(HttpClient? client = null) => this.client = client ?? SharedClient;

        public async Task<MetaLongLivedToken> ExchangeAsync(string shortLivedToken, string clientSecret,
            string platform = SocialPlatform.Instagram)
        {
            if (platform is not (SocialPlatform.Instagram or SocialPlatform.Threads))
                throw new ArgumentException("Unsupported token exchange platform.", nameof(platform));
            // Meta requires these parameters on its token endpoint. Never log the URI or response.
            string message = $"{platform} token exchange failed. Reconnect the account before posting.";
            string endpoint = platform == SocialPlatform.Threads
                ? "https://graph.threads.net/access_token?grant_type=th_exchange_token"
                : "https://graph.instagram.com/access_token?grant_type=ig_exchange_token";
            if (string.IsNullOrWhiteSpace(shortLivedToken) || string.IsNullOrWhiteSpace(clientSecret))
                throw ResponseStatusFactory.BadRequest(message);
            try
            {
                using var response = await client.GetAsync(endpoint +
                    "&client_secret=" + Uri.EscapeDataString(clientSecret) + "&access_token=" + Uri.EscapeDataString(shortLivedToken));
                if (!response.IsSuccessStatusCode) throw ResponseStatusFactory.BadRequest(message);
                var body = JObject.Parse(await response.Content.ReadAsStringAsync());
                string token = body.Value<string>("access_token") ?? "";
                int seconds = body.Value<int?>("expires_in") ?? 0;
                if (body["error"] != null || string.IsNullOrWhiteSpace(token) || seconds <= 0)
                    throw ResponseStatusFactory.BadRequest(message);
                return new(token, DateTime.UtcNow.AddSeconds(seconds));
            }
            catch (Exception)
            {
                // Even transport exceptions can include the credential-bearing request URI.
                throw ResponseStatusFactory.BadRequest(message);
            }
        }
    }
}
