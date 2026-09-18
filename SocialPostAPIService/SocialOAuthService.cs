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
                ResolveScopes(provider, platform);

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
                $"redirect_uri={redirectUri}" +
                $"{Environment.NewLine}" +
                $"scope={scope}",
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

                    ExternalAccountID =
                        json.Value<string>("user_id")
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
                platform,
                context);

            return token;
        }

        private static async Task EnrichTokenAsync(
            TokenExchangeResult token,
            string platform,
            ILambdaContext context)
        {
            if (FormatValue.EmptyValue(
                    token.AccessToken))
            {
                return;
            }

            string? profileEndpoint =
                ProfileEndpoint(platform, token.ExternalAccountID);

            if (profileEndpoint == null)
            {
                return;
            }

            // Log only the endpoint before credentials are appended. Include the
            // invoked version so an API Gateway alias can be checked from one run.
            Logger.LogLine(
                $"Social OAuth profile lookup starting." +
                $"{Environment.NewLine}method=GET" +
                $"{Environment.NewLine}profile_endpoint={profileEndpoint}" +
                $"{Environment.NewLine}lambda_version={context.FunctionVersion}" +
                $"{Environment.NewLine}invoked_function_arn={context.InvokedFunctionArn}" +
                $"{Environment.NewLine}lambda_request_id={context.AwsRequestId}",
                context);

            string profileUrl =
                profileEndpoint +
                $"&access_token={Uri.EscapeDataString(token.AccessToken)}";

            using HttpResponseMessage response =
                await HttpClient.GetAsync(
                    profileUrl);

            string body =
                await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw ResponseStatusFactory.BadRequest(
                    $"OAuth profile lookup failed for {platform} " +
                    $"[GET {profileEndpoint}; HTTP {(int)response.StatusCode}; " +
                    $"lambda_version={context.FunctionVersion}; " +
                    $"request_id={context.AwsRequestId}]: {body}");
            }

            JObject json =
                JObject.Parse(
                    body);

            token.ExternalAccountID =
                json.Value<string>(
                        platform == SocialPlatform.Instagram ? "user_id" : "id")
                ?? token.ExternalAccountID;

            token.DisplayName =
                json.Value<string>(
                        platform == SocialPlatform.Facebook ? "name" : "username")
                ?? DefaultValue.EMPTY_STRING;
        }

        private static string? ProfileEndpoint(
            string platform,
            string externalAccountID)
        {
            if (platform == SocialPlatform.Instagram)
            {
                // Instagram Login returns user_id during token exchange. Use the
                // versioned user node rather than the legacy unversioned /me route.
                if (string.IsNullOrWhiteSpace(externalAccountID))
                    throw ResponseStatusFactory.BadRequest(
                        "Instagram OAuth token exchange did not return user_id.");

                return "https://graph.instagram.com/v25.0/" +
                    Uri.EscapeDataString(externalAccountID) +
                    "?fields=user_id,username";
            }

            return platform switch
            {
                SocialPlatform.Facebook =>
                    "https://graph.facebook.com/v20.0/me?fields=id,name",
                SocialPlatform.Threads =>
                    "https://graph.threads.net/v1.0/me?fields=id,username",
                _ => null
            };
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
                    "https://www.instagram.com/oauth/authorize",

                SocialPlatform.Threads =>
                    "https://threads.net/oauth/authorize",

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

                SocialPlatform.Threads =>
                    "https://graph.threads.net/oauth/access_token",

                SocialPlatform.LinkedIn =>
                    "https://www.linkedin.com/oauth/v2/accessToken",

                _ =>
                    throw ResponseStatusFactory.BadRequest(
                        "platform is invalid.")
            };
        }

        private static string ResolveScopes(
            SocialServiceProvider? provider,
            string platform)
        {
            string scope =
                FormatValue.NotEmptyValue(provider?.Scopes)
                    ? provider!.Scopes
                    : GetSetting(platform, "SCOPES", DefaultScopes(platform));

            if (platform != SocialPlatform.Instagram)
                return scope;

            // Old cloud configuration (including cached values) and environment
            // overrides may still contain Basic Display scopes. This service uses
            // Instagram Login for professional accounts and content publishing.
            var scopes = new List<string>();
            bool hasLegacyScopes = false;
            foreach (string permission in scope.Split(
                new[] { ',', ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                if (permission == "user_profile" || permission == "user_media")
                {
                    hasLegacyScopes = true;
                    continue;
                }

                if (!scopes.Contains(permission))
                    scopes.Add(permission);
            }

            if (hasLegacyScopes)
            {
                foreach (string permission in DefaultScopes(platform).Split(','))
                {
                    if (!scopes.Contains(permission))
                        scopes.Add(permission);
                }
            }

            return string.Join(",", scopes);
        }

        private static string DefaultScopes(
            string platform)
        {
            return platform switch
            {
                SocialPlatform.Facebook =>
                    "pages_manage_posts pages_read_engagement",

                SocialPlatform.Instagram =>
                    "instagram_business_basic,instagram_business_content_publish",

                SocialPlatform.Threads =>
                    "threads_basic,threads_content_publish",

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
                !string.IsNullOrWhiteSpace(configuredValue)
                    ? configuredValue!.Trim()
                    : GetSetting(
                        platform,
                        key,
                        DefaultValue.EMPTY_STRING);

            if (string.IsNullOrWhiteSpace(value))
            {
                throw ResponseStatusFactory.BadRequest(
                    $"Missing OAuth setting SOCIAL_{platform.ToUpperInvariant()}_{key}. " +
                    $"Configure cloud_services.social_service_providers.{platform}.{key.ToLowerInvariant()} " +
                    "or the corresponding Lambda environment variable.");
            }

            return value;
        }

        private static string GetSetting(
            string platform,
            string key,
            string defaultValue)
        {
            string? value = Environment.GetEnvironmentVariable(
                $"SOCIAL_{platform.ToUpperInvariant()}_{key}");

            if (string.IsNullOrWhiteSpace(value))
            {
                value = Environment.GetEnvironmentVariable($"SOCIAL_POST_{key}");
            }

            return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
        }

        private static async Task<SocialServiceProvider?> GetProviderAsync(
            string platform,
            ILambdaContext context)
        {
            Dictionary<string, SocialServiceProvider>? providers =
                await AppConfigSettings.GetSocialServiceProvidersAsync(
                    context);

            SocialServiceProvider? provider = FindProvider(providers, platform);

            if (provider == null)
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

        private static SocialServiceProvider? FindProvider(
            Dictionary<string, SocialServiceProvider>? providers,
            string platform)
        {
            if (providers == null)
                return null;

            if (providers.TryGetValue(platform, out SocialServiceProvider? exact))
                return exact;

            foreach (var entry in providers)
            {
                if (string.Equals(entry.Key, platform, StringComparison.OrdinalIgnoreCase))
                    return entry.Value;
            }

            return null;
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
