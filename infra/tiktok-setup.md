# TikTok integration

TikTok app: `7691338013133916167` (GiveAnywhere Nonprofit Publishing).
Stage sandbox: `7691343093962934280` (GiveAnywhere Stage).

The development AppConfig record (`gany` / `dev`, AWS account `145505076425`,
us-east-1) now contains the sandbox credentials encrypted by
`scanpay_encrypt_data:gany_dev`. Client key maps to `client_id`. The configured
callback is `https://stage.gogiveanywhere.com/social/connections/callback` and
scopes are `user.info.basic,video.publish,video.upload`. No credentials belong
in source control or frontend configuration. The unqualified encryption Lambda
currently fails with TypeLoadException; its gany_dev alias worked.

## Runtime behavior

- OAuth uses TikTok's client_key parameter, identifies the account by open_id,
  and retrieves its display name. State is signed with the backend client secret,
  expires after ten minutes, and is bound to the current organization. The browser
  also verifies the returned state against its own session before exchanging the code.
- The Web2.0 CRM composer retrieves fresh creator settings and previews a video
  already hosted on the verified gogiveanywhere.com domain or a subdomain.
  This implementation uses PULL_FROM_URL, not local file upload. Video URLs must
  be public and must refer directly to media, not a web page.
- Privacy has no default. Interaction permissions default off. Direct publishing
  requires consent, valid duration, available privacy, and compliant commercial
  disclosure. Draft uploads are delivered to the TikTok inbox for completion there.
- One conditional delivery record per post prevents duplicate initialization.
  Ambiguous failures are not retried automatically. Status polling distinguishes
  processing, published, inbox delivery, and failure; initialization is not success.
- Tokens that expire require reconnection. Automatic refresh rotation and scheduled
  TikTok posting are not implemented. TikTok posts cannot be edited after creation;
  create a new one with fresh settings. The generic other-platform composer is unchanged.

## Remaining activation steps

1. `appconfig-settings-ganydev` is a Memcached key, managed through
   `scanpay_memcached:gany_dev` in the application account's us-east-1 region.
   A targeted delete returned false; a subsequent get returned an empty value.
   No cached value was returned, but deletion of an existing entry was not confirmed.
   Warm Lambda processes may still hold their own in-memory provider settings.
2. Add the intended TikTok tester to the sandbox and authorize that account.
3. Deploy the API and Web2.0 changes to stage using the normal deployment process.
   These source changes have not been deployed.
4. Verify a real, user-approved media post and inbox upload, then record the required
   end-to-end sandbox demo. Do not submit a mocked demo as working functionality.
5. Finish the production application and audit before enabling production credentials.
   Production draft settings remain open in the developer portal; its demo is missing.

Validation: the OAuthRegression console suite includes signature/tenant checks,
TikTok endpoint checks, consent, video-host, privacy, disclosure, interaction and
duration validation. Angular template compilation and social callback/workflow
tests run separately in Web2.0. These do not prove live TikTok publishing.

References: [OAuth](https://developers.tiktok.com/doc/oauth-user-access-token-management),
[Direct Post](https://developers.tiktok.com/doc/content-posting-api-reference-direct-post),
[Upload](https://developers.tiktok.com/doc/content-posting-api-reference-upload-video).
