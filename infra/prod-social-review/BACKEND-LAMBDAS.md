# Backend Lambda rollout inventory

Observed November 10, 2026. Backend account 145505076425, us-east-1. No aliases were changed. PlatformLibrary is compiled into consumers, not independently deployed.

| Repository | Function | Current PROD | DEV reference | Proposed action |
|---|---|---:|---:|---|
| PlatformSocialPostAPI | `scanpay_delete_social_post` | 17 | 18 | Promote existing immutable v18 after full configuration/hash gate |
| PlatformSocialPostAPI | `scanpay_update_social_post` | 17 | 18 | Promote existing immutable v18 after full configuration/hash gate |
| PlatformSocialPostAPI | `scanpay_create_social_post` | 21 | 22 | Promote existing immutable v22 after full configuration/hash gate |
| PlatformSocialPostAPI | `scanpay_publish_social_post` | 19 | 20 | Promote existing immutable v20 after full configuration/hash gate |
| PlatformSocialPostAPI | `scanpay_schedule_social_post` | 18 | 19 | Promote existing immutable v19 after full configuration/hash gate |
| PlatformSocialPostAPI | `scanpay_cancel_social_post` | 18 | 19 | Promote existing immutable v19 after full configuration/hash gate |
| PlatformSocialPostAPI | `scanpay_get_social_post` | 16 | 17 | Promote existing immutable v17 after full configuration/hash gate |
| PlatformSocialPostAPI | `scanpay_get_social_post_status` | 17 | 18 | Promote existing immutable v18 after full configuration/hash gate |
| PlatformSocialPostAPI | `scanpay_get_social_posts` | 16 | 17 | Promote existing immutable v17 after full configuration/hash gate |
| PlatformSocialPostAPI | `scanpay_get_social_post_analytics` | 19 | 20 | Publish a new version with distinct PROD cursor key; promote that version |
| PlatformPaymentAPI | `scanpay_one_time_pay` | 545 | 545 | Keep current PROD version; code and version already match DEV |
| PlatformSocialPostAPI | `scanpay_record_social_post_click` | 9 | 10 | Promote existing immutable v10 after full configuration/hash gate |
| PlatformPaymentAPI | `scanpay_complete_one_time_pay` | 613 | 613 | Keep current PROD version; code and version already match DEV |
| PlatformPaymentAPI | `scanpay_create_schedule_payment` | 185 | 185 | Keep current PROD version; code and version already match DEV |
| PlatformPaymentAPI | `scanpay_complete_payment` | 52 | 52 | Keep current PROD version; code and version already match DEV |
| PlatformCRMAPI | `scanpay_add_donor_pledge` | 61 | 62 | Promote existing immutable v62 after full configuration/hash gate |
| PlatformDataAPI | `scanpay_lookup_tinyurl` | 24 | 24 | Keep current PROD version; code and version already match DEV |
| PlatformPaymentAPI | `scanpay_process_schedule_pay` | 163 | 163 | Keep current PROD version; code and version already match DEV |
| PlatformQRCodeAPI | `scanpay_get_qrcode_url` | 40 | 40 | Keep current PROD version; code and version already match DEV |

11 existing immutable versions can be promoted; analytics requires one new configuration-specific version. Seven consumers already use the same DEV/PROD version and need no rebuild or alias change for this rollout. If rebuilding instead of promoting, rebuild the 12 changed functions against the approved PlatformLibrary sources, publish fresh immutable versions, and regenerate the manifest before promotion. Do not assume source HEAD matches the verified package.

Full runtime parity was subsequently verified for all 11 promotion candidates; analytics differs only in Environment. See runtime-parity.json. Repeat the comparison immediately before execution to reject drift. Function code hashes and alias revision IDs are in lambda-inventory.json.

The five connection/OAuth functions in the full social runner have no tracking-specific change and are excluded. Do not run an indiscriminate full-runner PROD promotion.
