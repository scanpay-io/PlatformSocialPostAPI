using System.Reflection;
using ScanPay.DataModel.Model;
using ScanPay.SocialPostService;
using SocialPlatform = ScanPay.SocialPostService.SocialPlatform;

static object? Call(string name, params object?[] args) =>
    typeof(SocialOAuthService).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!
        .Invoke(null, args);

int checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    checks++;
}

Check(SocialPlatform.IsValid("threads"), "Threads must pass connection/post validation");
Check(SocialPlatform.IsValid("THREADS"), "Platform validation must ignore case");
Check(!SocialPlatform.IsValid("unknown"), "Unknown platforms must remain invalid");

foreach (var platform in new[] { "instagram", "threads" })
{
    var provider = new SocialServiceProvider
    {
        ClientID = "configured-id", Supported = true, OAuthEnabled = true,
        RedirectUri = "https://stage.gogiveanywhere.com/social/connections/callback"
    };
    var providers = new Dictionary<string, SocialServiceProvider> { [platform.ToUpperInvariant()] = provider };
    Check(ReferenceEquals(Call("FindProvider", providers, platform), provider), "Mixed-case configured provider must resolve");
    Check(Call("FindProvider", providers, "unknown") == null, "Unrelated provider must not be reused");

    string envName = $"SOCIAL_{platform.ToUpperInvariant()}_CLIENT_ID";
    string? oldSpecific = Environment.GetEnvironmentVariable(envName);
    string? oldShared = Environment.GetEnvironmentVariable("SOCIAL_POST_CLIENT_ID");
    try
    {
        Environment.SetEnvironmentVariable(envName, "environment-id");
        Check((string)Call("GetRequiredSetting", provider.ClientID, platform, "CLIENT_ID")! == "configured-id", "Cloud configuration must take precedence");
        Check((string)Call("GetRequiredSetting", " ", platform, "CLIENT_ID")! == "environment-id", "Blank config must fall back");
        Environment.SetEnvironmentVariable(envName, " ");
        Environment.SetEnvironmentVariable("SOCIAL_POST_CLIENT_ID", "shared-id");
        Check((string)Call("GetRequiredSetting", null, platform, "CLIENT_ID")! == "shared-id", "Blank environment setting must not block fallback");
        Environment.SetEnvironmentVariable("SOCIAL_POST_CLIENT_ID", null);
        try
        {
            Call("GetRequiredSetting", null, platform, "CLIENT_ID");
            throw new Exception("Missing credentials must fail");
        }
        catch (TargetInvocationException ex)
        {
            Check(ex.InnerException!.Message.Contains($"social_service_providers.{platform}.client_id"), "Missing credential error must identify cloud configuration");
        }
    }
    finally
    {
        Environment.SetEnvironmentVariable(envName, oldSpecific);
        Environment.SetEnvironmentVariable("SOCIAL_POST_CLIENT_ID", oldShared);
    }

    string scope = (string)Call("DefaultScopes", platform)!;
    string url = (string)Call("BuildAuthorizeUrl", platform, "app-id", provider.RedirectUri, scope, "test-state")!;
    Check(url.StartsWith(platform == "instagram" ? "https://www.instagram.com/oauth/authorize?" : "https://threads.net/oauth/authorize?"), "Authorization host must match provider");
    Check(url.Contains("redirect_uri=" + Uri.EscapeDataString(provider.RedirectUri)), "Redirect must be encoded exactly");
    Check(scope == (platform == "instagram" ? "instagram_business_basic,instagram_business_content_publish" : "threads_basic,threads_content_publish"), "Provider scopes must support publishing");
    Check((string)Call("TokenUrl", platform)! == (platform == "instagram" ? "https://api.instagram.com/oauth/access_token" : "https://graph.threads.net/oauth/access_token"), "Token host must match provider");
    Check((string)Call("ResolveConfiguredRedirectUri", provider, platform)! == provider.RedirectUri, "Configured redirect must be preserved");
}
string? oldInstagramScopes = Environment.GetEnvironmentVariable("SOCIAL_INSTAGRAM_SCOPES");
string? oldSharedScopes = Environment.GetEnvironmentVariable("SOCIAL_POST_SCOPES");
try
{
    const string expectedScopes = "instagram_business_basic,instagram_business_content_publish";
    Environment.SetEnvironmentVariable("SOCIAL_INSTAGRAM_SCOPES", null);
    Environment.SetEnvironmentVariable("SOCIAL_POST_SCOPES", null);
    Check((string)Call("ResolveScopes", null!, "instagram")! == expectedScopes, "Missing scopes must use Instagram defaults");

    foreach (string legacy in new[] { "user_profile,user_media", "user_profile", "user_media", " user_profile, user_media\tuser_profile " })
    {
        var provider = new SocialServiceProvider { Scopes = legacy };
        string resolved = (string)Call("ResolveScopes", provider, "instagram")!;
        Check(resolved == expectedScopes, "Legacy cloud scopes must migrate to Instagram Login publishing scopes");
        string url = (string)Call("BuildAuthorizeUrl", "instagram", "1648676266617314", "https://stage.gogiveanywhere.com/social/connections/callback", resolved, "test-state")!;
        Check(url.Contains("scope=" + Uri.EscapeDataString(expectedScopes)) && !url.Contains("user_profile") && !url.Contains("user_media"), "Authorization URL must not emit legacy scopes");
    }

    Environment.SetEnvironmentVariable("SOCIAL_INSTAGRAM_SCOPES", "user_profile,user_media");
    Check((string)Call("ResolveScopes", null!, "instagram")! == expectedScopes, "Platform environment legacy scopes must migrate");
    var modern = new SocialServiceProvider { Scopes = "instagram_business_basic,instagram_business_manage_comments" };
    Check((string)Call("ResolveScopes", modern, "instagram")! == modern.Scopes, "Modern cloud scopes must remain authoritative");
    var mixed = new SocialServiceProvider { Scopes = "user_media,instagram_business_manage_comments,instagram_business_basic" };
    Check((string)Call("ResolveScopes", mixed, "instagram")! == "instagram_business_manage_comments,instagram_business_basic,instagram_business_content_publish", "Migration must preserve additional permissions without duplicating defaults");
    Environment.SetEnvironmentVariable("SOCIAL_INSTAGRAM_SCOPES", null);
    Environment.SetEnvironmentVariable("SOCIAL_POST_SCOPES", "user_profile,user_media");
    Check((string)Call("ResolveScopes", null!, "instagram")! == expectedScopes, "Shared environment legacy scopes must migrate");
    var other = new SocialServiceProvider { Scopes = "custom_scope another_scope" };
    Check((string)Call("ResolveScopes", other, "threads")! == other.Scopes, "Other platforms must retain their configured scopes");
}
finally
{
    Environment.SetEnvironmentVariable("SOCIAL_INSTAGRAM_SCOPES", oldInstagramScopes);
    Environment.SetEnvironmentVariable("SOCIAL_POST_SCOPES", oldSharedScopes);
}
Check((string)Call("ProfileEndpoint", "instagram", "17841400000000000")! ==
    "https://graph.instagram.com/v25.0/17841400000000000?fields=user_id,username",
    "Instagram profile lookup must use the versioned token-exchange user node");
Check((string)Call("ProfileEndpoint", "instagram", "id/path?query")! ==
    "https://graph.instagram.com/v25.0/id%2Fpath%3Fquery?fields=user_id,username",
    "Profile account ID must be escaped as a single path segment");
foreach (string missingID in new[] { "", " " })
{
    try
    {
        Call("ProfileEndpoint", "instagram", missingID);
        throw new Exception("Missing Instagram user ID must fail before profile lookup");
    }
    catch (TargetInvocationException ex)
    {
        Check(ex.InnerException!.Message.Contains("did not return user_id"), "Missing token-exchange user ID must produce an actionable error");
    }
}
Check((string)Call("ProfileEndpoint", "facebook", "")! == "https://graph.facebook.com/v20.0/me?fields=id,name", "Facebook profile lookup must remain unchanged");
Check((string)Call("ProfileEndpoint", "threads", "")! == "https://graph.threads.net/v1.0/me?fields=id,username", "Threads profile lookup must remain unchanged");
Check(Call("ProfileEndpoint", "linkedin", "") == null, "Platforms without profile enrichment must still skip lookup");
Console.WriteLine($"Passed {checks} OAuth regression checks.");
