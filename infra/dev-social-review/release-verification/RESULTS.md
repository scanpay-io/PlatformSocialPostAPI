# DEV release verification — 2026-11-10

## Result: end-to-end acceptance blocked

Inspected AWS account 145505076425, region us-east-1. All 19 gany_dev functions still point to the pre-infrastructure versions and code hashes. This establishes the active release state; it does not establish whether a local rebuild was attempted. The checked development aliases for analytics, one-time payment, and donor pledge also point to the same older versions.

Analytics gany_dev is version 18 (Release social-20261110-001), published before cursor configuration. Its cursor key is absent. No newer published analytics version exists. $LATEST retains the staged, valid 32-byte key and has Successful update status. No key material was printed or persisted.

A fresh analytics publication that captures the current environment, followed by promotion to gany_dev, is required. Other required consumers must be promoted from the intended freshly built release manifests. No backend code, configuration, aliases, or infrastructure were changed during this verification.

## Passed live checks

- Delivery AccountPublishedAt-index is ACTIVE.
- DEV API deployment: iarb8e; PROD remains vrokob.
- Unauthenticated GETs to new analytics and the four secured existing post routes return HTTP 401.
- Analytics OPTIONS returns HTTP 200 with GET/OPTIONS and X-Authorization-SP CORS support.
- Public click endpoint rejects malformed IDs (400), unknown post (404), and unscoped historical post (404).
- Consistent DynamoDB reads confirm none of the three rejected test events created click records.

## Active DEV Lambda versions

| Function | Before infrastructure | Current DEV | Release |
| --- | --- | --- | --- |
| `scanpay_add_donor_pledge` | 61 | 61 | Release crm-20261105-001 |
| `scanpay_cancel_social_post` | 19 | 19 | Release social-20261110-001 |
| `scanpay_complete_one_time_pay` | 613 | 613 | Release payment-20261105-001 |
| `scanpay_complete_payment` | 52 | 52 | Release payment-20261105-001 |
| `scanpay_create_schedule_payment` | 185 | 185 | Release payment-20261105-001 |
| `scanpay_create_social_post` | 22 | 22 | Release social-20261110-001 |
| `scanpay_delete_social_post` | 18 | 18 | Release social-20261110-001 |
| `scanpay_get_qrcode_url` | 40 | 40 | Release qr-20261108-002 |
| `scanpay_get_social_post` | 17 | 17 | Release social-20261110-001 |
| `scanpay_get_social_posts` | 17 | 17 | Release social-20261110-001 |
| `scanpay_get_social_post_analytics` | 18 | 18 | Release social-20261110-001 |
| `scanpay_get_social_post_status` | 18 | 18 | Release social-20261110-001 |
| `scanpay_lookup_tinyurl` | 24 | 24 | Release data-20261105-001 |
| `scanpay_one_time_pay` | 545 | 545 | Release payment-20261105-001 |
| `scanpay_process_schedule_pay` | 163 | 163 | Release payment-20261105-001 |
| `scanpay_publish_social_post` | 20 | 20 | Release social-20261110-001 |
| `scanpay_record_social_post_click` | 10 | 10 | Release social-20261110-001 |
| `scanpay_schedule_social_post` | 19 | 19 | Release social-20261110-001 |
| `scanpay_update_social_post` | 18 | 18 | Release social-20261110-001 |

## Remaining acceptance work

No scoped DEV fixtures exist: 73 posts and 64 deliveries remain unscoped. Historical data was not migrated or modified. An authenticated DEV test account/organization and isolated scoped fixture are needed for positive gateway-to-Lambda tests.

Not yet verified: authenticated organization isolation; delivery-specific link generation and URL resolver handoff; successful landing persistence and retry deduplication; correct cohort metrics; valid pagination and tampered/filter-changed cursor rejection; payment completion attribution. No external social publication, message, payment, or historical backfill was performed.

User clarification requested: deployed release IDs/account/region, and the DEV test organization/account with an authenticated session. These are required to reconcile the active release and complete positive end-to-end testing.
