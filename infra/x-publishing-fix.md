# X publishing validation fix

The DEV stage export inspected on 2026-11-26 omits `text_social` from the POST `/organization/{organizationid}/social/posts` integration mapping. The frontend supplies `connection_id` and `consent`, but the Lambda never receives them. Publishing then rejects the saved draft before calling X. Connecting the account again cannot repair that draft.

`social-post-create.vtl` shows the corrected contract. Generate a narrowly scoped integration patch from a fresh gateway export using `prepare-social-post-mapping.ps1 -ApiExport <export.json> -OutputFile <patch.json>`. The generator preserves the existing authorizer context and all existing mapping fields, adding optional `text_social` and `tiktok` passthrough. Review and apply that patch to the existing create-post integration, then deploy the intended DEV stage. No gateway change or deployment is performed by the generator.

The Web2.0 text composer now uses the existing `validationErrorMessage` helper for capitalized and lowercase API messages, and a friendly fallback for transport failures. It rejects missing account IDs before creating a draft. The backend distinguishes missing platform, account, and consent errors while preserving mandatory consent.

After deployment, create a fresh post with the selected X account and explicit confirmation. Existing drafts whose options were dropped cannot be repaired by repeating Publish. Verify the returned provider post ID and URL after an approved test publication. Do not automatically retry an ambiguous submission.
