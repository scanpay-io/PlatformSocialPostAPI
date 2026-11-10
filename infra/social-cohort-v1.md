# Social tracking and cohort_v1

Implemented locally; this document is not evidence of a deployment. The backend follows
SMS cohort_v1 authorization, environment isolation, bounded reporting, and cursor binding.
It does not treat a social delivery as an individually addressed SMS recipient.

## Security and data ownership

All post-management handlers, including legacy analytics, resolve the organization from
API Gateway `requestContext.authorizer.claims.sub`. The existing Account/OrganizationStaff
mapping is used; body/query organization identifiers cannot select another tenant.
Missing trusted identity is rejected. OAuth callbacks retain their existing state-based flow.

Posts, deliveries and clicks store `AppName`, `Runway`, and
`AccountScopeKey = app:runway:organization`. Readers check the component tags independently
of the key. Payment completion writes `TransactionAmount.SocialAccountScopeKey`; both legacy
and cohort analytics require that field and matching `Runway`. Competing SMS attribution is
excluded. The shared payment validator requires the post's organization/environment and an
actually published, scoped delivery, even when another platform's submission is unresolved.

Unscoped history is excluded. Do not infer environment from a shared table, organization,
or current alias. Review the migration manifest against deployment/audit evidence before
enabling the new readers. Existing links will fail eligibility until their post and delivery
history is explicitly scoped. Historical transactions retain their original source and amount.

## Delivery links and browser contract

Each publisher persists a `TrackingUrl` on its delivery before submission. The destination
is HTTPS and retains the existing `socialpostid` parameter, adding
`socialdeliveryid=<platform>-<post-guid>[-<attempt>]`. These are direct tagged destinations,
not a new redirect endpoint. Copied SMS/session identifiers are removed from public links.
The actual URL is included in Facebook links, Instagram/Threads captions, X/LinkedIn text,
and direct TikTok captions. TikTok inbox uploads are explicitly untracked because the API
does not publish their caption/link. This does not guarantee a platform renders captions
as clickable links.

Web2.0 captures `socialdeliveryid` and passes `social_delivery_id` to
`POST /social/posts/clicks`. Intake2.0 uses the same contract and sends `social_post_id` on
payment initiation. Cross-app handoff preserves `socialclickid`, `socialvisitorid`,
`socialdeliveryid` and `socialat` (original capture time in Unix milliseconds).
A new delivery creates a new landing event; navigation/retries retain the event ID.
Tracking failures do not block payment. SMS replaces social attribution and vice versa.

Click writes remain conditional on post ID + click ID. The backend derives platform from
the scoped published delivery, never trusts a caller-supplied platform to choose a delivery,
and rejects unpublished/wrong-environment deliveries. Legacy clients omitting a delivery
ID retain post-level counts; a single eligible tracked delivery can be inferred, otherwise
the event's platform is `unknown`. TinyUrl/QR resolver request models and the shared URL
helper preserve `socialdeliveryid`; their callers and gateway mappings must forward it.

## Analytics contract

```text
GET /social/analytics?contract=cohort_v1&from_utc=2026-11-01T00:00:00Z&to_utc=2026-11-08T00:00:00Z
GET /social/analytics?contract=cohort_v1&post_id=POST_GUID
```

The existing analytics Lambda also accepts these query parameters on its existing route.
Without `contract`, its legacy response shape remains available with the strengthened
authorization/scope filters and bounded event loading.

- Cohorts select **delivery publication time**, inclusive `from_utc`, exclusive `to_utc`,
  at most 90 days. Optional `campaign_id` filters the parent resource. All date inputs
  are normalized to UTC. A single `post_id` cannot be mixed with cohort filters.
- Outcomes occur at/after publication and before `outcome_before_utc` (defaults to now).
  For post-only donations, the lower bound is the earliest selected delivery for that post.
  Revenue is post-level: the same post in two delivery cohorts can therefore appear in
  both reports. Do not sum such overlapping reports or infer platform revenue.
- `view=summary` returns the full cohort; `view=deliveries&limit=50` returns detail only.
  Limits are 1–100. A detail page is never presented as a full-cohort summary.
- Signed continuation tokens bind organization, application/environment, post/campaign,
  date range, view, page size and effective outcome cutoff. Retain the same filters.
  Configure distinct `SOCIAL_ANALYTICS_CURSOR_KEY` values (base64 of at least 32 random bytes)
  in each environment's Lambda version configuration. Keys must remain stable across pages;
  rotation invalidates outstanding cursors. Never commit key material.
- Work is capped at 2,000 candidate deliveries and 50,000 evaluated records per request,
  including records later filtered out. Eight concurrent post/event loads are allowed.
  Exceeding a budget or missing an index returns an explicit error, never partial totals.
- Responses expose `as_of_utc`, `consistency`, `revenue_basis`, evaluated records and consumed
  read capacity. Indexes are eventually consistent; refunds can change current-state reports
  even with the same cutoff. Pagination is ordered by publication time then delivery ID;
  late index arrivals may require a refresh.

Reported metrics include publication/tracking coverage, landing events, distinct browser
visitors, deliveries with landings, posts with donations, successful donation counts and
amounts by currency. `donations_per_landing` is explicitly a transaction ratio and can exceed
100%; undefined percentages are null. No impression CTR or verified visitor conversion is
invented. Untracked delivery engagement is null. Delivery-level donation fields remain null:
payment records currently prove only the parent post, not a delivery or visitor identity.

## Deployment order and compatibility gates

1. Back up shared data and inventory the aliases/API integrations. Do not deploy tightened
   readers before reviewing historical scope assignments and the required identity mapping.
2. Apply `socialpost-dynamodb.yaml` through its existing stack owner to add the delivery
   `AccountPublishedAt-index` (`AccountScopeKey`, `PublishedAtKey`). Wait for ACTIVE. The
   new writer always supplies a string sort key: ISO UTC for published deliveries,
   `unpublished` otherwise. Existing post/click/transaction post-ID indexes are retained.
3. Prepare an explicit JSON manifest and run `migrate-social-scope.ps1 -Manifest PATH` for a
   read-only plan. Use `-Apply` only after reviewing it, with affected writers paused.
   Each row is `{ "table": "SocialPost", "id": "GUID", "organization_id": "ORG",
   "app": "gany", "runway": "dev" }`. Supported tables: SocialPost, SocialPostDelivery,
   SocialPostClick, TransactionAmount. Scope only rows whose provenance is known. Include
   parent/delivery pairs. The script validates all entries before writing, refuses scope
   reassignment, uses conditional updates, and does not manufacture historical TrackingUrls.
   If concurrent changes stop a partially applied plan, inspect and rerun the idempotent plan.
4. Grant post handlers GetItem on Account and OrganizationStaff. Grant analytics Query on
   the new delivery index and existing post/click/transaction indexes, and GetItem on
   SocialPost. Payment/CRM validators also need Query on SocialPostDelivery/SocialPostID-index.
   Click ingestion needs delivery-index Query in addition to post GetItem/click PutItem.
5. Existing post routes must use authenticated proxy integration or explicitly inject
   `requestContext.authorizer.claims.sub` from `$context.authorizer.claims.sub` in their VTL.
   Never copy this context from the request body. Add the authenticated analytics route with
   `social-analytics-route.yaml` only if it does not already exist; otherwise update its owner.
   Deploy API stages intentionally; stack changes alone do not publish gateway deployments.
6. Publish updated shared-library consumers (payment initiation/completion, pledges/schedules,
   TinyUrl/QR resolvers), social Lambdas and Web2.0/Intake2.0. Preserve SocialPostID through existing
   payment gateway templates and add socialdeliveryid to resolver templates. Both payment
   writer branches now persist the environment scope. Resolve any unknown historical
   transactions before comparing old and new totals.
7. Apply existing public-route throttling/monitoring conventions to clicks. Validate DEV
   cross-tenant and cross-environment rejection, distinct platform links, direct/short/QR
   handoff, login, retry, payment completion and refund behavior before production promotion.

The implementation does not deploy resources, send posts/messages, or create payments.

## Verification

Use single-worker `dotnet build ... -m:1` in this workspace. Run the SocialAttributionRegression
and PublishingRegression executables after building their projects. `node tests/browser-attribution.cjs`
executes the actual Web2.0 and Intake2.0 attribution services with mocked navigation/storage/HTTP;
it does not replace deployed-browser acceptance. Web2.0's focused attribution suite and its
component/platform/control-flow checks are also applicable. Build payment and resolver projects
against the changed shared library before release.

Remaining independent work: server-enforced attribution expiry, verified click-to-donor and
delivery-to-payment linkage, provider impression/reaction ingestion, partial-refund net revenue,
authoritative currency persistence, bot filtering, retention, and large asynchronous exports.
