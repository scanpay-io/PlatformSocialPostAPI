# DEV social tracking infrastructure approval

Prepared from read-only AWS inspection on 2026-11-10. **No cloud changes have been applied.** No change sets, deployments, keys, or historical updates have been created. Backend builds and version publication remain with Joe.

## Verified baseline

- Account: `145505076425`; region: `us-east-1`; API: `6op50ujgu6`.
- DEV deployment: `c7k3t4`, stage variable `runwayAlias=gany_dev`. PROD deployment: `vrokob`, alias variable `gany_prod`.
- Cognito authorizer: `waut26`, identity header `X-Authorization-SP`.
- Analytics DEV alias: version `18`; PROD: version `17`. DEV and `$LATEST` lack `SOCIAL_ANALYTICS_CURSOR_KEY`.
- DynamoDB stack: `platform-socialpost-dynamodb`, on-demand social tables shared between environments. Delivery index `AccountPublishedAt-index` is absent. Existing indexes are active; social table PITR is enabled.
- Complete API inventory: 553 resources / 1,047 methods. Compared current method authorization and core integration configuration against the deployed DEV export; no differences were found in the compared fields. This is not an audit of every API setting.

## Exact proposed changes

| Area | Proposed change | Review artifact |
| --- | --- | --- |
| Shared DynamoDB | Add only `SocialPostDelivery.AccountPublishedAt-index`: HASH `AccountScopeKey` (String), RANGE `PublishedAtKey` (String), projection ALL. Preserve billing, existing indexes, PITR, and all other resources. | `proposed/dynamodb.diff`, `proposed/dynamodb-template.yaml` |
| Existing post authorization | Change four GET methods from NONE to Cognito authorizer `waut26`: posts list (`m8r49v`), post detail (`5dyq1f`), status (`ji0l5g`), post analytics (`u9uc4l`). | `proposed/api-patches.json` |
| Trusted identity | Update all ten existing post method templates to inject Cognito `sub` into `requestContext.authorizer.claims.sub`; set passthrough to NEVER. Preserve AWS nonproxy integrations and response mappings. Unsupported unmapped content types will be rejected. | `proposed/api-patches.json`; corresponding before/after VTL files |
| Analytics parameters | Existing post analytics template forwards `contract`, `post_id`, `campaign_id`, `from_utc`, `to_utc`, `outcome_before_utc`, `view`, `limit`, `next_token`. | `proposed/u9uc4l-GET.after.vtl` |
| Delivery attribution | Forward optional query parameters `socialpostid` and `socialdeliveryid` through tiny URL (`6zm71h`) and QR resolver (`0gh7xx`) GET mappings. | `proposed/6zm71h-GET.after.vtl`, `proposed/0gh7xx-GET.after.vtl` |
| Payment attribution | Forward optional JSON body field `social_post_id` through POST `/give/onetime` (`shnome`); preserve existing fields. | `proposed/shnome-POST.after.vtl` |
| New analytics route | Create `/social/analytics` under `huiirm`, authenticated GET using Cognito `waut26`, AWS_PROXY to analytics Lambda with stage alias, plus CORS OPTIONS. Proposed stack name: `giveanywhere-social-analytics-dev`. | `proposed/analytics-route-dev.yaml`, `proposed/analytics-route-parameters.json` |
| Lambda invocation | Add permission only to `scanpay_get_social_post_analytics:gany_dev` for `arn:aws:execute-api:us-east-1:145505076425:6op50ujgu6/dev/GET/social/analytics`. | Same route template |
| DEV throttling | POST `/social/posts/clicks`: 50 requests/sec, burst 100. GET `/social/analytics`: 10 requests/sec, burst 20. Click route remains public. | `proposed/dev-stage-settings.json` |
| Cursor signing | Generate 32 cryptographically random bytes, base64 encode, add as `SOCIAL_ANALYTICS_CURSOR_KEY` on analytics `$LATEST`, preserving every existing environment entry and guarding against configuration drift. Do not log or save the value in review artifacts. | `proposed/lambda-configuration-plan.json` |
| DEV deployment | Create a deployment snapshot after verification and point only `dev` at it. No PROD stage or alias change. | Deployment ID is generated at apply time; rollback baseline is `c7k3t4`. |

`proposed/api-patches.json` contains 17 exact calls: 13 integration patches and four authorization patches, with expected before-state. `proposed/routes.json` enumerates every affected path and method. All 13 mappings have separate before/after VTL files.

The API control plane is shared: edits become candidates for future deployments to any stage, although this proposal deploys only DEV. The DynamoDB index is a shared additive change and can incur storage/write costs; it is not DEV-exclusive.

## Data and permissions boundaries

All 73 existing SocialPost records and all 64 SocialPostDelivery records lack AppName/Runway scope. Stored metadata does not establish DEV ownership. **No backfill is proposed**: `proposed/historical-scope-manifest.json` is empty. Strict scoped readers will exclude these records until a separately reviewed, evidence-backed migration is approved. New scoped records can be used for DEV validation.

SocialPostClick has zero records. The transaction social index contains 391 entries, but the inspected entries have no nonblank social attribution; they are not 391 attributed gifts. No transaction migration, index, or capacity change is proposed. Preserve TransactionAmount's provisioned 8 RCU / 2 WCU.

The shared DeployLambda execution role already grants DynamoDB access through AmazonDynamoDBFullAccess; no execution-role expansion is required or proposed. This does not assert an audit of organization SCPs. The new route-specific Lambda invoke permission above is the only proposed IAM-related resource addition.

## Apply sequence, only after approval

1. Re-read affected stack, stage, mappings, authorizer, Lambda revision, and aliases. Stop on drift and refresh this proposal rather than overwriting concurrent work. Recheck unrelated pending API changes before creating a deployment.
2. Create and inspect a change set for `platform-socialpost-dynamodb` using the proposed live-derived template. Require only an additive delivery-table index modification, no replacement or unrelated changes. Execute only that change set and wait for the index to become ACTIVE.
3. Coordinate analytics `$LATEST` configuration with Joe's release. Merge the new cursor key into the existing environment using the current RevisionId. Existing version 18 is immutable: Joe must publish a version containing the key and deploy it to `gany_dev` before analytics validation. Do not move aliases or publish backend versions as part of this approval.
4. Create and inspect the new route stack change set: four resources (resource, GET, OPTIONS, DEV invocation permission), no PROD permission. Apply the exact 17 API patches after their before-state guards pass.
5. Create a deployment and update only DEV, then apply the DEV route throttles. Verify PROD deployment `vrokob` and analytics PROD version 17 remain unchanged.
6. Verify authorized/unauthorized requests, tenant/environment isolation, delivery-specific URL resolution, payment attribution, cohort totals, pagination signature rejection, and CORS. Any test that writes data or initiates payment needs an agreed DEV fixture and must not charge real payment methods.

Both proposed CloudFormation templates passed AWS `validate-template`; responses are in `proposed/*-validation.json`. Validation does not replace change-set review or deployed integration testing. No runtime mapping or end-to-end tests have been performed against the proposed cloud configuration.

## Rollback

Return DEV to deployment `c7k3t4` and restore prior DEV method settings from the captured stage snapshot. Restore the changed shared API method/integration configuration using captured before-state so a future PROD deployment cannot pick up unintended changes. Coordinate any backend alias rollback separately with Joe. Prefer leaving the additive GSI in place during incident rollback; do not delete tables or historical data. Removing the new route stack or index is a separate reviewed cleanup, not an automatic rollback action.

## Requested approval

Approve the exact package above: shared additive delivery index, shared API configuration edits with DEV-only deployment, DEV throttles/invoke permission, and staging the analytics cursor key coordinated with Joe's backend release. Exclude historical backfill, PROD deployment/alias changes, backend publication, and execution-role expansion.
