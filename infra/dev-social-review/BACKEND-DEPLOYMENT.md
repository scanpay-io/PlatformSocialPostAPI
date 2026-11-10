# Backend release after DEV infrastructure changes

Rebuild against the updated PlatformLibrary and deploy new versions to `gany_dev` only. PlatformLibrary is packaged into consuming Lambdas; it is not deployed independently. Do not promote a pre-infrastructure release manifest for analytics: its version will not contain the staged cursor key.

## Required tracking consumers (19 Lambdas)

| Repository | AWS function |
| --- | --- |
| PlatformSocialPostAPI | `scanpay_create_social_post` |
| PlatformSocialPostAPI | `scanpay_update_social_post` |
| PlatformSocialPostAPI | `scanpay_delete_social_post` |
| PlatformSocialPostAPI | `scanpay_cancel_social_post` |
| PlatformSocialPostAPI | `scanpay_schedule_social_post` |
| PlatformSocialPostAPI | `scanpay_publish_social_post` |
| PlatformSocialPostAPI | `scanpay_get_social_post` |
| PlatformSocialPostAPI | `scanpay_get_social_posts` |
| PlatformSocialPostAPI | `scanpay_get_social_post_status` |
| PlatformSocialPostAPI | `scanpay_get_social_post_analytics` |
| PlatformSocialPostAPI | `scanpay_record_social_post_click` |
| PlatformPaymentAPI | `scanpay_one_time_pay` |
| PlatformPaymentAPI | `scanpay_complete_one_time_pay` |
| PlatformPaymentAPI | `scanpay_complete_payment` |
| PlatformPaymentAPI | `scanpay_create_schedule_payment` |
| PlatformPaymentAPI | `scanpay_process_schedule_pay` |
| PlatformCRMAPI | `scanpay_add_donor_pledge` |
| PlatformDataAPI | `scanpay_lookup_tinyurl` |
| PlatformQRCodeAPI | `scanpay_get_qrcode_url` |

The SocialPost API's existing release runner publishes all 16 functions in `lambda-runner.json`; using that complete inventory is fine. The five connection/OAuth functions beyond the 11 listed here have no additional tracking infrastructure requirement.

## Release gates

1. Publish fresh backend packages from the updated sources. Publish analytics after infrastructure configuration is staged. Verify its new immutable version contains `SOCIAL_ANALYTICS_CURSOR_KEY` without logging the value. The inspected social release runner preserves existing live configuration; avoid deployment tools that replace environment variables with the three entries in `aws-lambda-tools-defaults.json`.
2. Promote only `gany_dev`. No backend aliases or published versions were changed by this infrastructure task. Leave `gany_prod` untouched; a future PROD release needs its own cursor key and separate approval.
3. Deploy the prepared Web2.0/Intake2.0 changes through their normal DEV frontend process for delivery-aware browser capture and handoff. Infrastructure alone does not update browser assets.
4. Validate with newly created, scoped DEV records. Historical posts/deliveries remain unscoped and excluded; no backfill was performed. Verify authorized cross-tenant/environment rejection, separate delivery links, landing deduplication, analytics pagination, and attribution through payment completion using an agreed noncharging DEV fixture.

No social posts, messages, payments, or historical data writes are part of infrastructure verification.
