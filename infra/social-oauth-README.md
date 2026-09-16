# Instagram and Threads OAuth configuration

The API reads providers from `cloud_services.social_service_providers` in the
configuration selected by the invoked Lambda alias. Merge the following entries
into that dictionary, preserving other providers. Replace the placeholder IDs
and secrets using the existing configuration administration process; do not
commit credentials. This example is not automatically deployed.

```json
{
  "instagram": {
    "display_name": "Instagram",
    "supported": true,
    "oauth_enabled": true,
    "client_id": "<Instagram app ID>",
    "client_secret": "<Instagram app secret>",
    "redirect_uri": "https://stage.gogiveanywhere.com/social/connections/callback",
    "scopes": "instagram_business_basic,instagram_business_content_publish"
  },
  "threads": {
    "display_name": "Threads",
    "supported": true,
    "oauth_enabled": true,
    "client_id": "<Threads app ID>",
    "client_secret": "<Threads app secret>",
    "redirect_uri": "https://stage.gogiveanywhere.com/social/connections/callback",
    "scopes": "threads_basic,threads_content_publish"
  }
}
```

Use credentials belonging to each product. Instagram uses Instagram Login for
professional accounts. Explicitly configured scopes override defaults: replace
old `user_profile,user_media` scopes if present. Preserve posting/analytics flags
according to the capabilities enabled in the deployment.

Register the configured redirect in each product's OAuth settings. Both Lambda
functions (authorize connection and connection callback) must resolve the same
provider configuration. The request body's redirect URI is not used.

Provider names are matched without case sensitivity. Credential field names are
`client_id` and `client_secret`, not `app_id` and `app_secret`. The shared loader
supports plaintext values and its existing encrypted credential format.

If credentials are absent, code changes cannot supply them. The error identifies
the missing cloud configuration field and environment fallback. After updating
configuration, invalidate the applicable configuration cache and replace warm
Lambda instances using the established deployment process; the shared loader
also caches providers in memory.

Validation: `dotnet run --project tests/OAuthRegression -c Release`.

References: [Meta Instagram collection](https://www.postman.com/meta/instagram/folder/1z5vxzu/instagram-api-with-instagram-login),
[Meta Threads collection](https://www.postman.com/meta/threads/documentation/dht3nzz/threads-api).
