# GiveAnywhere Lambda releases — DevOps handoff

## Purpose

Use one release ID to identify a coordinated set of Lambda versions. AWS version numbers remain independent per function. A release can correctly map Account to version 42 and Payment to version 18. The authoritative record is a JSON manifest in a private, versioned S3 bucket; an alias description is only a convenient label.

## The three commands

Run from `E:\GiveAnywhere\Platform` for all PlatformAPIs, or from an individual PlatformAPI root for a scoped release.

| Script | What it does | AWS changes |
| --- | --- | --- |
| `build-all-lambdas.bat [configuration]` | Incrementally builds each Lambda and its dependencies locally. | None. |
| `publish-all-lambdas.bat release-id [configuration] [aws-profile]` | Builds release packages, stores ZIPs in S3 or an image in ECR, updates function code, publishes/reuses exact numbered versions, and stores the release manifest. | Changes `$LATEST` and publishes versions. Does not move aliases. |
| `deploy-all-lambdas.bat alias release-id [aws-profile]` | Reads the Ready manifest from S3 and points every requested alias to its recorded version. | Alias changes only. No compilation, code upload, or version publication. |

**Breaking change:** deploy's second argument is now the release ID, not `Release` or `Debug`. Update CI jobs and scheduled tasks. The old `deploy-all-lambdas.bat development Release` is rejected.

Example after completing setup:

```powershell
cd E:\GiveAnywhere\Platform

# Optional early compile check. Publishing builds its own packages regardless.
.\build-all-lambdas.bat Release

# Choose a new, unique ID for each publish attempt.
.\publish-all-lambdas.bat 20260911-001 Release default

# Only a fully published Ready release can be promoted.
.\deploy-all-lambdas.bat development 20260911-001 default

# After application validation, promote the same exact versions.
.\deploy-all-lambdas.bat production 20260911-001 default
```

`--help` is available on all three scripts. `--list` on build/publish lists the configured inventory without AWS access. Publication exclusion settings are applied during publishing, not to the build inventory preview.

## One-time setup

Prerequisites:

- Windows PowerShell 5.1 or later, the .NET SDK matching these projects, Git, and Amazon.Lambda.Tools.
- A recent AWS CLI v2 supporting S3 `put-object --if-none-match` and Lambda revision guards.
- An authenticated AWS profile for the target account.
- Existing Lambda functions with the correct runtime, handler, architecture, environment variables, roles, networking, layers, and other infrastructure configuration.
- For FileAPIHtmltoPDF: Docker with Linux container support and access to the existing ECR repository.

The scripts do not create the S3 bucket, ECR repositories, Lambda functions, or IAM roles. No real AWS publishing/deployment was performed when these scripts were installed.

Provision a private S3 bucket in `us-east-1` (all current Lambda defaults use that region). Choose your own globally unique name. Example commands for DevOps to run, replacing the bucket name:

```powershell
$bucket = 'YOUR-PRIVATE-RELEASE-BUCKET'
aws s3api create-bucket --bucket $bucket --region us-east-1 --profile default
aws s3api put-public-access-block --bucket $bucket --public-access-block-configuration BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true --profile default
aws s3api put-bucket-versioning --bucket $bucket --versioning-configuration Status=Enabled --profile default
```

Use the organization's encryption and TLS policies. If using a customer-managed KMS key, grant the necessary KMS permissions to the publisher/promoter and any relevant AWS services. Do not allow deletion or overwriting of release records through normal deployment roles. Consider an S3 policy requiring conditional creates for immutable release keys. Retain Lambda versions and ECR image digests referenced by releases you may roll back to; S3 retention alone does not preserve deleted Lambda versions.

Configure `release-settings.json` in the root from which commands run, or use these environment variables (recommended for CI and to configure all roots together):

```powershell
$env:GANY_RELEASE_BUCKET = 'YOUR-PRIVATE-RELEASE-BUCKET'
$env:GANY_RELEASE_REGION = 'us-east-1'
$env:GANY_RELEASE_ACCOUNT = 'YOUR-12-DIGIT-AWS-ACCOUNT-ID'
# Optional; defaults to the release account.
$env:GANY_RELEASE_BUCKET_OWNER = 'YOUR-12-DIGIT-BUCKET-OWNER'
# Optional; default prefix is giveanywhere.
$env:GANY_RELEASE_PREFIX = 'giveanywhere'
```

The checked-in settings intentionally leave Bucket and AccountId blank. Confirm the account with `aws sts get-caller-identity --profile default`; current project defaults reference account `145505076425`, but the operator must explicitly select the intended account. The script verifies the profile's account, bucket owner, region, versioning, and all four Block Public Access settings before proceeding.

The release bucket and functions must be in the same region. This implementation promotes within the same AWS account/region. Cross-account or cross-region artifact replication is a separate workflow; changing the profile is not a supported cross-account promotion method.

## Encryption deployment ownership

`PlatformDataAPI/DataAPIEncrypt` owns `scanpay_encrypt_data`. The duplicate `PlatformUtilityAPI/UtilityAPIEncrypt` project was removed from the active UtilityAPI solution and Lambda inventory; its files and Git history were preserved under UtilityAPI's ignored `_git_metadata_backup/removed-encrypt-*` directory. No exclusion setting is needed for this resolved duplicate. This local removal does not delete or update the AWS function.
## Storage and release records

S3 keys under the configured prefix:

```text
giveanywhere/<account-id>/releases/<release-id>/reservation.json
giveanywhere/<account-id>/releases/<release-id>/packages/<platform>/<lambda>.zip
giveanywhere/<account-id>/releases/<release-id>/manifest.json

giveanywhere/<account-id>/deployments/<release-id>/<alias>/<attempt>/plan.json
giveanywhere/<account-id>/deployments/<release-id>/<alias>/<attempt>/result.json
```

Keys are created conditionally and are never overwritten by these scripts. S3 versioning supplies object versions for ZIP artifacts. ECR images are referenced by their immutable digest, not `latest` tags. The reservation prevents concurrent publishers from using the same release ID.

The manifest includes:

- Release ID, AWS account/region, scope, build configuration, timestamps, publisher identity, and expected function count.
- Source repository commits and whether the working copy was dirty. These are provenance hints, not a substitute for package checksums.
- Per-Lambda platform/project, exact function ARN, numbered version, code SHA-256, configuration fingerprint, artifact location/digest, and success/failure detail.
- `Ready` or `Incomplete` status. A local in-progress manifest uses `Publishing`.

Environment variable values are not written into the manifest; configuration is represented by a fingerprint. Each published Lambda version snapshots the existing AWS configuration. Publishing intentionally changes code only: it does not apply arbitrary local default settings to live infrastructure. A runtime, handler, package-type, or architecture mismatch fails that Lambda so infrastructure can be corrected separately.

For unchanged ZIP code with matching configuration, a previously published exact snapshot may be reused. That is expected: one release does not require every Lambda to increment its AWS version number.

## Deployment, verification, and rollback

Before changing any alias, deploy checks that the release is Ready, all entries exist, the account/region/scope match, function ARNs are unique, and every exact published version matches the manifest's code/configuration fingerprints. It reads all current aliases and saves their previous versions, descriptions, routing, and revision IDs in the deployment plan in S3.

If preflight fails anywhere, **no aliases are changed**. This avoids beginning a promotion with a known incomplete or invalid target set. During promotion, individual alias failures do not stop later functions. Each update uses the previously read alias revision ID to reject concurrent operator changes and is read back for verification. A full promotion clears additional weighted routing; this is not a canary deployment workflow.

Each alias description becomes `Release <release-id>`. The deployment result contains exact previous/target versions and status for every function. Look at this result to answer whether an environment is fully on a release; do not compare raw version numbers across functions.

Retry a partially successful promotion using the **same** deploy command and release ID. It does not rebuild or republish. Earlier successful alias updates are not automatically rolled back.

Rollback uses the same command with a known-good release of the same scope:

```powershell
.\deploy-all-lambdas.bat production 20260910-003 default
```

Master releases use scope `All`; a platform-root release uses that platform's name. Deploy a release from the matching root/scope. Rollback is to the versions in the chosen manifest, not automatically to a mixture of previously observed alias versions. Retain a known-good baseline release before relying on coordinated rollback.

Aliases do not isolate environment configuration: all versions belong to their recorded function ARN and contain the configuration captured at publication. Verify whether your development/production setup uses shared functions or separate accounts/functions before promoting the same release. All runtime integrations must use aliases if publishing should leave live traffic unchanged; unqualified function invocations execute `$LATEST`, which publishing updates.

## Failure handling and logs

All commands report successes first and failures second per platform. Build/publish continue after individual Lambda failures. A publish with any failed Lambda stores an Incomplete manifest, which deploy refuses. Inventory errors such as duplicate targets, invalid settings, wrong account, or inaccessible release storage stop before work starts.

Local logs are under `artifacts/lambda-runs/<UTC-time>-<attempt>-<operation>/`:

- Per-Lambda log files.
- `results.csv` and `summary.txt`, including platform summaries.
- Publish: local `manifest.json`, reservation, and artifacts.
- Deploy: downloaded `manifest.json` and `deployment.json` with plan/results.

Results are flushed as each function completes. Completed commands return 0 for success and 1/nonzero for failure; CI must inspect the exit code. An alias-update failure yields a Partial deployment result; a preflight failure yields Blocked. A process interruption or AWS storage failure may leave only the reservation/plan and local progress: inspect that attempt before retrying.

An interrupted or failed **publish** ID is not reused. Choose a new ID. Functions already published remain available but aliases are unchanged. A failed final manifest upload means the release is not available for promotion, even if its local file says Ready. A failed final deployment-result upload can occur after aliases changed; use local `deployment.json`, the stored plan, and current aliases to reconcile and retry.

Run one publisher/promoter at a time for overlapping functions/aliases. Keep the source checkout stable throughout packaging. Revision guards detect common races, but this is not a cross-function transaction or a distributed deployment lock. Do not run cleanup of referenced versions/images during publishing, promotion, or rollback.

## IAM responsibilities

Use least-privilege publisher and promoter roles scoped to the selected account, region, functions, bucket prefix, and ECR repository:

- Both: `sts:GetCallerIdentity`, S3 bucket-location/versioning/public-access-block reads.
- Publisher: Git/source access; `s3:PutObject` for reservations, packages and manifests; Lambda `GetFunction`, `GetFunctionConfiguration`, `ListVersionsByFunction`, `UpdateFunctionCode`, `PublishVersion`; artifact read permissions where needed by Lambda; relevant KMS permissions if configured.
- Promoter: `s3:GetObject` for manifests; `s3:PutObject` for deployment plans/results; Lambda `GetFunctionConfiguration`, `GetAlias`, `CreateAlias`, `UpdateAlias`. It does not need code-upload or publish-version permissions.
- Container publisher additionally needs ECR authorization, layer upload/push, and image-description permissions for the existing repository. Docker's credential store handles the short-lived login token; it is not saved in the manifest.

## Files to commit and hand off

At the master root and each PlatformAPI root, keep these together:

- `build-all-lambdas.bat`, `publish-all-lambdas.bat`, `deploy-all-lambdas.bat`
- `lambda-runner.ps1`, `lambda-runner.json` (inventory exists at platform roots)
- `release-runner.ps1`, `release-lib.ps1`, `release-settings.json`
- `RELEASES-README.md`

Also commit PlatformFileAPI's `lambda-runner.json` image mapping and `FileAPIHtmltoPDF/Dockerfile.release`. The release Dockerfile consumes explicitly published output so sibling shared-library references are resolved before the container build; it retains the existing runtime/native dependencies.

Each PlatformAPI carries its own helper files and can operate independently. The master consumes platform inventory and its own settings/helpers. Do not mix helper versions between machines. Original scripts are backed up under `_git_metadata_backup/release-*`; backups and artifacts remain ignored. Existing standalone `promote-all-lambdas.bat` files are legacy tools outside this release workflow; use the new deploy command for manifest-based promotions.

## References

- AWS Lambda versions: https://docs.aws.amazon.com/lambda/latest/dg/configuration-versions.html
- Update function code and revision guards: https://docs.aws.amazon.com/lambda/latest/api/API_UpdateFunctionCode.html
- Conditional S3 writes: https://docs.aws.amazon.com/AmazonS3/latest/userguide/conditional-writes.html