using Amazon.Lambda.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ScanPay.DataModel.Model;
using ScanPay.Utility.Model;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using static ScanPay.Utility.Model.ResponseStatusException;

namespace ScanPay.SocialPostService
{
    public class SocialOAuthService
    {
        private static readonly HttpClient HttpClient =
            new();

        public async Task<SocialConnectionAuthorizeResponse> AuthorizeAsync(
            SocialConnectionAuthorizeRequest request,
            ILambdaContext context)
        {
            if (request == null)
            {
                throw ResponseStatusFactory.BadRequest(
                    "Request is required.");
            }

            string platform =
                NormalizePlatform(
                    request.SocialPlatform);

            SocialServiceProvider? provider =
                await GetProviderAsync(
                    platform,
                    context);

            string clientID =
                GetRequiredSetting(
                    provider?.ClientID,
                    platform,
                    "CLIENT_ID");

            /*
             * IMPORTANT:
             * The OAuth redirect URI is backend-controlled.
             *
             * Do not use request.RedirectUri here. With Meta Strict Mode,
             * the redirect_uri sent to Facebook must exactly match the
             * configured Valid OAuth Redirect URI.
             */
            string redirectUri =
                ResolveConfiguredRedirectUri(
                    provider,
                    platform);

            string scope =
                FormatValue.NotEmptyValue(provider?.Scopes)
                    ? provider!.Scopes
                    : GetSetting(
                        platform,
                        "SCOPES",
                        DefaultScopes(platform));

            string state =
                EncodeState(
                    request.EffectiveOrganizationID,
                    platform);

            string authorizationUrl =
                BuildAuthorizeUrl(
                    platform,
                    clientID,
                    redirectUri,
                    scope,
                    state);

            Logger.LogLine(
                $"Social OAuth authorization URL created." +
                $"{Environment.NewLine}" +
                $"platform={platform}" +
                $"{Environment.NewLine}" +
                $"organization_id={request.EffectiveOrganizationID}" +
                $"{Environment.NewLine}" +
                $"redirect_uri={redirectUri}",
                context);

            return new SocialConnectionAuthorizeResponse
            {
                AuthorizationUrl =
                    authorizationUrl,

                OAuthMessage =
                    "Redirect the user to authorization_url.",

                Status =
                    "success",

                Code =
                    200
            };
        }

        public async Task<SocialConnectionDb> CompleteCallbackAsync(
            SocialConnectionCallbackRequest request,
            ILambdaContext context)
        {
            if (request == null)
            {
                throw ResponseStatusFactory.BadRequest(
                    "Request is required.");
            }

            ApplyState(
                request);

            string platform =
                NormalizePlatform(
                    request.SocialPlatform);

            SocialServiceProvider? provider =
                await GetProviderAsync(
                    platform,
                    context);

            if (FormatValue.EmptyValue(request.Code) &&
                FormatValue.EmptyValue(request.AccessToken) &&
                FormatValue.EmptyValue(request.TokenSecretID))
            {
                throw ResponseStatusFactory.BadRequest(
                    "code, access_token, or token_secret_id is required.");
            }

            TokenExchangeResult token =
                await ResolveTokenAsync(
                    request,
                    platform,
                    provider,
                    context);

            string connectionID =
                FormatValue.NewID();

            string tokenSecretID =
                FormatValue.NotEmptyValue(request.TokenSecretID)
                    ? request.TokenSecretID
                    : $"social/{request.EffectiveOrganizationID}/{platform}/{connectionID}";

            var connection =
                new SocialConnectionDb
                {
                    SocialConnectionID =
                        connectionID,

                    OrganizationID =
                        request.EffectiveOrganizationID,

                    Platform =
                        platform,

                    ExternalAccountID =
                        FormatValue.NotEmptyValue(request.ExternalAccountID)
                            ? request.ExternalAccountID
                            : token.ExternalAccountID,

                    DisplayName =
                        FormatValue.NotEmptyValue(request.DisplayName)
                            ? request.DisplayName
                            : token.DisplayName,

                    TokenSecretID =
                        tokenSecretID,

                    TokenExpiresDateUtc =
                        request.TokenExpiresDateUtc
                        ?? token.ExpiresDateUtc,

                    GrantedScopes =
                        FormatValue.NotEmptyValue(request.GrantedScopes)
                            ? request.GrantedScopes
                            : token.Scope,

                    Status =
                        SocialConnectionStatus.Connected
                };

            if (FormatValue.NotEmptyValue(token.AccessToken) ||
                FormatValue.NotEmptyValue(token.RefreshToken))
            {
                await DbCRUD<SocialConnectionTokenDb>.CreateAsync(
                    new SocialConnectionTokenDb
                    {
                        TokenSecretID =
                            tokenSecretID,

                        SocialConnectionID =
                            connectionID,

                        OrganizationID =
                            connection.OrganizationID,

                        Platform =
                            platform,

                        AccessToken =
                            token.AccessToken,

                        RefreshToken =
                            token.RefreshToken,

                        TokenExpiresDateUtc =
                            connection.TokenExpiresDateUtc
                    },
                    context);
            }

            var service =
                new SocialConnectionService();

            return await service.CreateAsync(
                connection,
                context);
        }

        private static string NormalizePlatform(
            string platform)
        {
            if (!SocialPlatform.IsValid(platform))
            {
                throw ResponseStatusFactory.BadRequest(
                    "platform is invalid.");
            }

            return platform.Trim().ToLowerInvariant();
        }

        private static async Task<TokenExchangeResult> ResolveTokenAsync(
            SocialConnectionCallbackRequest request,
            string platform,
            SocialServiceProvider? provider,
            ILambdaContext context)
        {
            if (FormatValue.NotEmptyValue(request.AccessToken) ||
                FormatValue.NotEmptyValue(request.TokenSecretID))
            {
                return new TokenExchangeResult
                {
                    AccessToken =
                        request.AccessToken,

                    RefreshToken =
                        request.RefreshToken,

                    ExpiresDateUtc =
                        request.TokenExpiresDateUtc,

                    Scope =
                        request.GrantedScopes
                };
            }

            string clientID =
                GetRequiredSetting(
                    provider?.ClientID,
                    platform,
                    "CLIENT_ID");

            string clientSecret =
                GetRequiredSetting(
                    provider?.ClientSecret,
                    platform,
                    "CLIENT_SECRET");

            /*
             * IMPORTANT:
             * The token exchange must use the exact same redirect URI that was
             * used in the authorization request.
             *
             * Never take this value from request.RedirectUri.
             */
            string redirectUri =
                ResolveConfiguredRedirectUri(
                    provider,
                    platform);

            Logger.LogLine(
                $"Social OAuth token exchange starting." +
                $"{Environment.NewLine}" +
                $"platform={platform}" +
                $"{Environment.NewLine}" +
                $"organization_id={request.EffectiveOrganizationID}" +
                $"{Environment.NewLine}" +
                $"redirect_uri={redirectUri}",
                context);

            string tokenUrl =
                TokenUrl(
                    platform);

            using var content =
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["grant_type"] =
                            "authorization_code",

                        ["code"] =
                            request.Code,

                        ["redirect_uri"] =
                            redirectUri,

                        ["client_id"] =
                            clientID,

                        ["client_secret"] =
                            clientSecret
                    });

            using HttpResponseMessage response =
                await HttpClient.PostAsync(
                    tokenUrl,
                    content);

            string body =
                await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Logger.LogLine(
                    $"OAuth token exchange failed." +
                    $"{Environment.NewLine}" +
                    $"platform={platform}" +
                    $"{Environment.NewLine}" +
                    $"redirect_uri={redirectUri}" +
                    $"{Environment.NewLine}" +
                    $"response={body}",
                    context);

                throw ResponseStatusFactory.BadRequest(
                    $"OAuth token exchange failed for {platform}: {body}");
            }

            JObject json =
                JObject.Parse(
                    body);

            int? expiresIn =
                json.Value<int?>(
                    "expires_in");

            TokenExchangeResult token =
                new()
                {
                    AccessToken =
                        json.Value<string>(
                            "access_token")
                        ?? DefaultValue.EMPTY_STRING,

                    RefreshToken =
                        json.Value<string>(
                            "refresh_token")
                        ?? DefaultValue.EMPTY_STRING,

                    Scope =
                        json.Value<string>(
                            "scope")
                        ?? DefaultValue.EMPTY_STRING,

                    ExpiresDateUtc =
                        expiresIn.HasValue
                            ? DefaultValue.UtcNow()
                                .AddSeconds(
                                    expiresIn.Value)
                            : null
                };

            await EnrichTokenAsync(
                token,
                platform);

            return token;
        }

        private static async Task EnrichTokenAsync(
            TokenExchangeResult token,
            string platform)
        {
            if (FormatValue.EmptyValue(
                    token.AccessToken))
            {
                return;
            }

            if (!string.Equals(
                    platform,
                    SocialPlatform.Facebook,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string profileUrl =
                "https://graph.facebook.com/v20.0/me" +
                "?fields=id,name" +
                $"&access_token={Uri.EscapeDataString(token.AccessToken)}";

            using HttpResponseMessage response =
                await HttpClient.GetAsync(
                    profileUrl);

            string body =
                await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw ResponseStatusFactory.BadRequest(
                    $"OAuth profile lookup failed for {platform}: {body}");
            }

            JObject json =
                JObject.Parse(
                    body);

            token.ExternalAccountID =
                json.Value<string>(
                    "id")
                ?? DefaultValue.EMPTY_STRING;

            token.DisplayName =
                json.Value<string>(
                    "name")
                ?? DefaultValue.EMPTY_STRING;
        }

        private static string ResolveConfiguredRedirectUri(
            SocialServiceProvider? provider,
            string platform)
        {
            string redirectUri =
                GetRequiredSetting(
                    provider?.RedirectUri,
                    platform,
                    "REDIRECT_URI");

            redirectUri =
                redirectUri.Trim();

            if (!Uri.TryCreate(
                    redirectUri,
                    UriKind.Absolute,
                    out Uri? uri))
            {
                throw ResponseStatusFactory.BadRequest(
                    $"Configured OAuth redirect URI for '{platform}' is invalid.");
            }

            if (!string.Equals(
                    uri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw ResponseStatusFactory.BadRequest(
                    $"Configured OAuth redirect URI for '{platform}' must use HTTPS.");
            }

            return redirectUri;
        }

        private static string BuildAuthorizeUrl(
            string platform,
            string clientID,
            string redirectUri,
            string scope,
            string state)
        {
            return $"{AuthorizeUrl(platform)}?response_type=code" +
                   $"&client_id={Uri.EscapeDataString(clientID)}" +
                   $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                   $"&scope={Uri.EscapeDataString(scope)}" +
                   $"&state={Uri.EscapeDataString(state)}";
        }

        private static string AuthorizeUrl(
            string platform)
        {
            return platform switch
            {
                SocialPlatform.Facebook =>
                    "https://www.facebook.com/v20.0/dialog/oauth",

                SocialPlatform.Instagram =>
                    "https://api.instagram.com/oauth/authorize",

                SocialPlatform.LinkedIn =>
                    "https://www.linkedin.com/oauth/v2/authorization",

                _ =>
                    throw ResponseStatusFactory.BadRequest(
                        "platform is invalid.")
            };
        }

        private static string TokenUrl(
            string platform)
        {
            return platform switch
            {
                SocialPlatform.Facebook =>
                    "https://graph.facebook.com/v20.0/oauth/access_token",

                SocialPlatform.Instagram =>
                    "https://api.instagram.com/oauth/access_token",

                SocialPlatform.LinkedIn =>
                    "https://www.linkedin.com/oauth/v2/accessToken",

                _ =>
                    throw ResponseStatusFactory.BadRequest(
                        "platform is invalid.")
            };
        }

        private static string DefaultScopes(
            string platform)
        {
            return platform switch
            {
                SocialPlatform.Facebook =>
                    "pages_manage_posts pages_read_engagement",

                SocialPlatform.Instagram =>
                    "user_profile,user_media",

                SocialPlatform.LinkedIn =>
                    "openid profile w_member_social",

                _ =>
                    DefaultValue.EMPTY_STRING
            };
        }

        private static string GetRequiredSetting(
            string? configuredValue,
            string platform,
            string key)
        {
            string value =
                FormatValue.NotEmptyValue(
                    configuredValue)
                    ? configuredValue!
                    : GetSetting(
                        platform,
                        key,
                        DefaultValue.EMPTY_STRING);

            if (FormatValue.EmptyValue(
                    value))
            {
                throw ResponseStatusFactory.BadRequest(
                    $"Missing OAuth setting SOCIAL_{platform.ToUpperInvariant()}_{key}.");
            }

            return value;
        }

        private static string GetSetting(
            string platform,
            string key,
            string defaultValue)
        {
            return Environment.GetEnvironmentVariable(
                       $"SOCIAL_{platform.ToUpperInvariant()}_{key}")
                   ?? Environment.GetEnvironmentVariable(
                       $"SOCIAL_POST_{key}")
                   ?? defaultValue;
        }

        private static async Task<SocialServiceProvider?> GetProviderAsync(
            string platform,
            ILambdaContext context)
        {
            Dictionary<string, SocialServiceProvider>? providers =
                await AppConfigSettings.GetSocialServiceProvidersAsync(
                    context);

            if (providers == null ||
                !providers.TryGetValue(
                    platform,
                    out SocialServiceProvider? provider))
            {
                return null;
            }

            if (!provider.Supported)
            {
                throw ResponseStatusFactory.BadRequest(
                    $"Social platform '{platform}' is not supported.");
            }

            if (!provider.OAuthEnabled)
            {
                throw ResponseStatusFactory.BadRequest(
                    $"OAuth is not enabled for social platform '{platform}'.");
            }

            return provider;
        }

        private static string EncodeState(
            string organizationID,
            string platform)
        {
            string json =
                JsonConvert.SerializeObject(
                    new Dictionary<string, string>
                    {
                        ["organization_id"] =
                            organizationID,

                        ["platform"] =
                            platform,

                        ["nonce"] =
                            FormatValue.NewID()
                    });

            return Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(
                        json))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static void ApplyState(
            SocialConnectionCallbackRequest request)
        {
            if (FormatValue.EmptyValue(
                    request.State))
            {
                return;
            }

            try
            {
                string padded =
                    request.State
                        .Replace('-', '+')
                        .Replace('_', '/');

                padded =
                    padded.PadRight(
                        padded.Length +
                        ((4 - padded.Length % 4) % 4),
                        '=');

                JObject state =
                    JObject.Parse(
                        Encoding.UTF8.GetString(
                            Convert.FromBase64String(
                                padded)));

                if (FormatValue.EmptyValue(
                        request.OrganizationID))
                {
                    request.OrganizationID =
                        state.Value<string>(
                            "organization_id")
                        ?? DefaultValue.EMPTY_STRING;
                }

                if (FormatValue.EmptyValue(
                        request.SocialPlatform))
                {
                    request.SocialPlatform =
                        state.Value<string>(
                            "platform")
                        ?? DefaultValue.EMPTY_STRING;
                }
            }
            catch
            {
                throw ResponseStatusFactory.BadRequest(
                    "OAuth state is invalid.");
            }
        }

        private class TokenExchangeResult
        {
            public string AccessToken { get; set; } =
                DefaultValue.EMPTY_STRING;

            public string RefreshToken { get; set; } =
                DefaultValue.EMPTY_STRING;

            public string Scope { get; set; } =
                DefaultValue.EMPTY_STRING;

            public string ExternalAccountID { get; set; } =
                DefaultValue.EMPTY_STRING;

            public string DisplayName { get; set; } =
                DefaultValue.EMPTY_STRING;

            public DateTime? ExpiresDateUtc { get; set; }
        }
    }
}