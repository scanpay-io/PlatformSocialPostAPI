# Analytics v19 verification — 2026-11-10

DEV `scanpay_get_social_post_analytics:gany_dev` now invokes version **19**, modified 2026-11-10 17:54:40 UTC. Hurtt Demo organization: `1d3a11d5-ece9-4a24-ab3a-1b601fa15861`.

## Passed

- Public DEV click persistence with correct delivery, platform and environment scope.
- Retry deduplication; platform relabeling, unpublished delivery and wrong-environment rejection.
- Post analytics: three published deliveries, two tracked, two landing events, one browser visitor, two deliveries with landings, zero donations. Untracked delivery engagement remains null.
- Publication-date cohort query now returns HTTP 200 and the expected fixture totals: the blank-post fallback fix is deployed.
- Analytics rejects wrong-environment posts and missing trusted actor context; a caller organization parameter cannot replace the trusted actor's organization.
- Gateway unauthenticated requests return 401; analytics CORS returns 200.

## Remaining deployment issues

1. Signed pagination returns HTTP **500** because `SOCIAL_ANALYTICS_CURSOR_KEY` is absent from **both v19 and $LATEST**. Only account_number, runway_type and region remain. The previously staged key was removed during the deployment interval. Restore a cryptographically random base64 key of at least 32 bytes on $LATEST, merge/preserve environment variables during deployment, then publish a fresh version and promote only the intended DEV alias. Immutable version 19 cannot be repaired in place.
2. **gany_prod now also points to version 19**, previously version 17. This verification did not move aliases or deploy PROD. Review the external deployment's alias targets; no rollback was attempted.
3. The documented Intake2.0 bucket still serves the November 9 index. Its documented hostname was unreachable and its listed CloudFront distribution was absent from this account. A different deployed frontend URL has not been supplied, so the updated deployed frontend could not be verified.

## Scope

Used temporary, uniquely tagged DEV fixtures to simulate published deliveries; no provider was contacted and no social content was published. Analytics checks used IAM Lambda invocation with a trusted Hurtt Demo test event, not a signed-in browser session. No payments or historical backfills were performed. No infrastructure or alias changes were made.

Full browser-to-backend acceptance, successful payment completion attribution, and signed pagination continuation/tampering remain pending. See `hurtt-tests.json` for test and cleanup results, and `v19-configuration.json` for sanitized alias configuration.
