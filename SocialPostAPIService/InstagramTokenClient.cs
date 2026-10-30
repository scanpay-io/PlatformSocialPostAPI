using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using static ScanPay.Utility.Model.ResponseStatusException;

namespace ScanPay.SocialPostService
{
    public record InstagramLongLivedToken(string AccessToken, DateTime ExpiresDateUtc);

    public sealed class InstagramTokenClient
    {
        private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(8) };
        private readonly HttpClient client;
        public InstagramTokenClient(HttpClient? client = null) => this.client = client ?? SharedClient;

        public async Task<InstagramLongLivedToken> ExchangeAsync(string shortLivedToken, string clientSecret)
        {
            // Meta requires these parameters on its token endpoint. Never log the URI or response.
            const string message = "Instagram token exchange failed. Reconnect the account before posting.";
            if (string.IsNullOrWhiteSpace(shortLivedToken) || string.IsNullOrWhiteSpace(clientSecret))
                throw ResponseStatusFactory.BadRequest(message);
            try
            {
                using var response = await client.GetAsync("https://graph.instagram.com/access_token?grant_type=ig_exchange_token" +
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
