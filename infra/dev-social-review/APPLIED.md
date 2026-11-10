# Approved DEV infrastructure applied — 2026-11-10

Account `145505076425`, region `us-east-1`. Applied following explicit user approval. No historical scope migration, PROD deployment, backend publication, or Lambda alias version promotion was performed.

## Completed

- `platform-socialpost-dynamodb`: UPDATE_COMPLETE. Change set modified only SocialPostDeliveryTable, replacement False. `AccountPublishedAt-index` is ACTIVE with AccountScopeKey / PublishedAtKey string keys and ALL projection. Existing table data was not scoped or rewritten by this task; DynamoDB performed its normal index population.
- `giveanywhere-social-analytics-dev`: CREATE_COMPLETE. Added analytics resource, authenticated GET, CORS OPTIONS, and invocation permission limited to the DEV route and `gany_dev`.
- Applied 13 integration mappings and four Cognito authorization changes. Verified all existing API methods against their before-state plus approved patches; no unrelated method changes were included. AWS's omission of an empty requestModels object was normalized during comparison.
- DEV deployment changed from `c7k3t4` to **`iarb8e`**. DEV click throttle is 50 requests/sec, burst 100; analytics is 10 requests/sec, burst 20.
- PROD remains on deployment **`vrokob`**. Analytics alias versions remain DEV **18**, PROD **17**; no aliases were promoted. The DEV alias policy RevisionId changed because the approved invocation permission was added.
- Generated and staged `SOCIAL_ANALYTICS_CURSOR_KEY` on analytics `$LATEST`, retaining all existing environment entries. Update status Successful. Key material was not printed or saved in review artifacts. A newly published version is required to expose the key through `gany_dev`.

## Verification and limitations

Five unauthenticated GET checks passed with HTTP 401: the new analytics route and existing list, detail, status, and post analytics routes. Analytics OPTIONS passed with HTTP 200 and GET/OPTIONS plus X-Authorization-SP CORS headers. Initial HTTP checks observed the previous edge configuration; repeat checks passed after propagation. The exported deployed DEV API also confirms the route and Cognito configuration.

Authenticated cross-tenant/environment checks, delivery attribution, payment completion, and signed analytics pagination remain release acceptance work using new scoped DEV fixtures. No authenticated user token or payment fixture was used in this task. The 73 historical posts and 64 deliveries remain unscoped and excluded by strict readers.

## Implementation corrections

Before applying the payment mapping, corrected a preparation-script string substitution error to use the existing `$inputRoot.containsKey('social_post_id')`; the approved field and behavior are unchanged. API Gateway rejected `add /authorizerId`, so the four authorization patches use its required `replace /authorizerId` operation. Current proposed artifacts reflect both corrections.

## Evidence and handoff

Sanitized change sets, patch receipts, final stage/alias state, deployed API export, and HTTP results are in `applied/`. The historical manifest remains empty. See `BACKEND-DEPLOYMENT.md` for the 19 required tracking-consumer Lambdas and release gates. Use a fresh release so analytics includes the staged key; the inspected release runner preserves live environment configuration.

For a DEV API rollback, deployment `c7k3t4` remains the baseline. Restoring the shared API control-plane mappings and prior stage throttles is a separate rollback step using captured before-state; leave the additive index in place unless its removal is separately approved. No rollback was performed.
