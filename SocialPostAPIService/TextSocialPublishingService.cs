using Amazon.Lambda.Core;
using Newtonsoft.Json.Linq;
using ScanPay.DataModel.Model;
using ScanPay.Utility.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using static ScanPay.Utility.Model.ResponseStatusException;

namespace ScanPay.SocialPostService
{
    public class TextSocialPublishingService
    {
        private static readonly HttpClient TokenClient = new() { Timeout = TimeSpan.FromSeconds(20) };
        private readonly TextSocialProviderClient providerClient = new();

        public static bool Supports(string platform) => platform == SocialPlatform.X || platform == SocialPlatform.LinkedIn;

        private static async Task<SocialServiceProvider> ProviderAsync(string platform, ILambdaContext context)
        {
            var providers = await AppConfigSettings.GetSocialServiceProvidersAsync(context);
            var provider = providers?.FirstOrDefault(p => string.Equals(p.Key, platform, StringComparison.OrdinalIgnoreCase)).Value;
            if (provider == null || !provider.Supported || !provider.OAuthEnabled)
                throw ResponseStatusFactory.BadRequest($"{platform} is not configured for connections yet.");
            return provider;
        }

        public async Task<SocialConnectionTokenDb> TokenAsync(SocialConnectionDb connection, ILambdaContext context)
        {
            if (!Supports(connection.Platform) || connection.Status != SocialConnectionStatus.Connected)
                throw ResponseStatusFactory.BadRequest("Connect the social account first.");
            var token = await DbCRUD<SocialConnectionTokenDb>.ReadConsistentAsync(connection.TokenSecretID, context);
            if (token == null || token.OrganizationID != connection.OrganizationID ||
                token.SocialConnectionID != connection.SocialConnectionID || token.Platform != connection.Platform)
                throw ResponseStatusFactory.BadRequest("The account credentials are unavailable. Reconnect the account.");
            if (token.TokenExpiresDateUtc > DateTime.UtcNow.AddMinutes(2)) return token;
            if (string.IsNullOrWhiteSpace(token.RefreshToken) || !string.IsNullOrWhiteSpace(token.RefreshLease))
                throw ResponseStatusFactory.BadRequest("The connection expired or its refresh is incomplete. Reconnect the account.");
            var provider = await ProviderAsync(connection.Platform, context);
            if (string.IsNullOrWhiteSpace(provider.ClientID) || string.IsNullOrWhiteSpace(provider.ClientSecret))
                throw ResponseStatusFactory.BadRequest("OAuth credentials are not configured.");

            string lease = Guid.NewGuid().ToString("N");
            if (!await DbCRUD<SocialConnectionTokenDb>.TryPatchAsync(token.TokenSecretID, token.OrganizationID, token,
                new Dictionary<string, object> { [nameof(token.RefreshLease)] = lease }, context))
                throw ResponseStatusFactory.BadRequest("The account is refreshing in another request. Try again shortly.");
            token.RefreshLease = lease;
            try
            {
                var form = new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = token.RefreshToken, ["client_id"] = provider.ClientID };
                using var request = new HttpRequestMessage(HttpMethod.Post, connection.Platform == SocialPlatform.X
                    ? "https://api.x.com/2/oauth2/token" : "https://www.linkedin.com/oauth/v2/accessToken");
                if (connection.Platform == SocialPlatform.X)
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(provider.ClientID + ":" + provider.ClientSecret)));
                else form["client_secret"] = provider.ClientSecret;
                request.Content = new FormUrlEncodedContent(form);
                using var response = await TokenClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException();
                var result = JObject.Parse(await response.Content.ReadAsStringAsync());
                string accessToken = result.Value<string>("access_token") ?? "";
                int expires = result.Value<int>("expires_in");
                if (string.IsNullOrWhiteSpace(accessToken) || expires <= 0) throw new InvalidOperationException();
                string required = connection.Platform == SocialPlatform.X ? "tweet.write" : "w_organization_social";
                string? scope = result.Value<string>("scope");
                if (scope != null && !scope.Split(' ', ',').Contains(required)) throw new InvalidOperationException();
                string refreshToken = result.Value<string>("refresh_token") ?? token.RefreshToken;
                DateTime expiration = DateTime.UtcNow.AddSeconds(expires);
                if (!await DbCRUD<SocialConnectionTokenDb>.TryPatchAsync(token.TokenSecretID, token.OrganizationID, token,
                    new Dictionary<string, object>
                    {
                        [nameof(token.AccessToken)] = accessToken, [nameof(token.RefreshToken)] = refreshToken,
                        [nameof(token.TokenExpiresDateUtc)] = expiration, [nameof(token.RefreshLease)] = "",
                        [nameof(token.LastUpdate)] = DateTime.UtcNow
                    }, context)) throw new InvalidOperationException();
                token.AccessToken = accessToken;
                token.RefreshToken = refreshToken;
                token.TokenExpiresDateUtc = expiration;
                token.RefreshLease = "";
                return token;
            }
            catch
            {
                // A failed/ambiguous refresh may have rotated the remote token. Do not retry it blindly.
                throw ResponseStatusFactory.BadRequest("The social connection could not be refreshed. Reconnect the account.");
            }
        }

        public async Task<List<SocialPage>> LinkedInPagesAsync(SocialConnectionDb connection, ILambdaContext context)
        {
            if (connection.Platform != SocialPlatform.LinkedIn) throw ResponseStatusFactory.BadRequest("Select a LinkedIn connection.");
            return await providerClient.LinkedInPagesAsync((await TokenAsync(connection, context)).AccessToken);
        }

        public static string Validate(SocialPostDb post)
        {
            var options = post.TextSocial;
            if (post.Platforms.Count != 1 || !Supports(post.Platforms[0]) || options == null ||
                string.IsNullOrWhiteSpace(options.ConnectionID) || !options.Consent)
                throw ResponseStatusFactory.BadRequest("Select one connected account and confirm the post before publishing.");
            string text = post.Content.Trim();
            if (string.IsNullOrWhiteSpace(text)) throw ResponseStatusFactory.BadRequest("Post text is required.");
            if (!string.IsNullOrWhiteSpace(post.DestinationUrl))
            {
                if (!Uri.TryCreate(post.DestinationUrl, UriKind.Absolute, out var url) ||
                    (url.Scheme != "https" && url.Scheme != "http") || !string.IsNullOrEmpty(url.UserInfo))
                    throw ResponseStatusFactory.BadRequest("The post link must be an HTTP or HTTPS URL.");
                text += "\n\n" + post.DestinationUrl;
            }
            // X performs weighted length validation (emoji and shortened URLs are not .NET string length).
            if (text.Length > (post.Platforms[0] == SocialPlatform.LinkedIn ? 3000 : 10000))
                throw ResponseStatusFactory.BadRequest("The post exceeds the platform's text limit.");
            return text;
        }

        public async Task<SocialPostDb> PublishAsync(SocialPostDb post, ILambdaContext context)
        {
            string text = Validate(post);
            var options = post.TextSocial!;
            string platform = post.Platforms[0];
            var provider = await ProviderAsync(platform, context);
            if (!provider.PostingEnabled)
                throw ResponseStatusFactory.BadRequest($"{platform} publishing is not enabled. Complete the platform's access approval and configuration first.");
            var connection = await new SocialConnectionService().ReadAsync(post.OrganizationID, options.ConnectionID, context);
            if (connection.Platform != platform) throw ResponseStatusFactory.BadRequest("The selected account does not match the post platform.");
            var token = await TokenAsync(connection, context);
            string scope = platform == SocialPlatform.X ? "tweet.write" : "w_organization_social";
            if (!connection.GrantedScopes.Split(' ', ',').Contains(scope))
                throw ResponseStatusFactory.BadRequest("Reconnect the account with publishing permission.");
            if (platform == SocialPlatform.LinkedIn)
                await providerClient.VerifyLinkedInPageAsync(token.AccessToken, options.LinkedInPageUrn);
            var delivery = new SocialPostDeliveryDb
            {
                DeliveryID = platform + "-" + post.SocialPostID, SocialPostID = post.SocialPostID,
                OrganizationID = post.OrganizationID, SocialConnectionID = connection.SocialConnectionID,
                Platform = platform, Status = "initializing"
            };
            if (!await DbCRUD<SocialPostDeliveryDb>.TryCreateAsync(delivery, context))
                throw ResponseStatusFactory.BadRequest("This post was already submitted. Check its status before creating another.");
            try
            {
                var published = await providerClient.PublishAsync(platform, token.AccessToken, text, options.LinkedInPageUrn);
                delivery.ExternalPostID = published.ID;
                delivery.ExternalPostUrl = published.Url;
                delivery.Status = SocialPostDeliveryStatus.Published;
                delivery.PublishedDateUtc = DateTime.UtcNow;
                await DbCRUD<SocialPostDeliveryDb>.SaveAsync(delivery, context);
                post.Status = SocialPostStatus.Published;
                post.PublishedDateUtc = delivery.PublishedDateUtc;
                await DbCRUD<SocialPostDb>.SaveAsync(post, context);
                return post;
            }
            catch
            {
                delivery.Status = "submission_unknown";
                delivery.ErrorMessage = "Publication could not be confirmed. Check the platform before creating another post.";
                await DbCRUD<SocialPostDeliveryDb>.SaveAsync(delivery, context);
                post.Status = "submission_unknown";
                await DbCRUD<SocialPostDb>.SaveAsync(post, context);
                throw ResponseStatusFactory.BadRequest(delivery.ErrorMessage);
            }
        }
    }
}
