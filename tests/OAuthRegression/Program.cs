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
Console.WriteLine($"Passed {checks} OAuth regression checks.");
