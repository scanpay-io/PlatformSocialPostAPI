# SocialPost API alignment

The existing root Git repository, history, and origin (git@github.com:scanpay-io/PlatformSocialPostAPI.git) are preserved. No new repository was needed.

All 15 Lambdas and SocialPostAPIService now reference the sibling PlatformLibrary. The existing root solution is preserved with library paths updated. The old nested library and .gitmodules are backed up under _git_metadata_backup/alignment-*. Its obsolete gitlink removal is staged. Backups are excluded from compilation and Git.

Commands:
- .\build-all-lambdas.bat Release
- .\deploy-all-lambdas.bat development Release default

Build cleans/restores/builds with one worker. Deployment handles all 15 Lambdas and fingerprints local files in the root and sibling PlatformLibrary. No initial commit is required; deployment argument order is alias/configuration/profile.

Checked 32 project reference paths, all solution paths, 15 Lambda defaults, and backup exclusions. No build, test, AWS deployment, commit, or push was performed.

Changed: 16 .csproj files, SocialPostAPIService.sln, both main batch files, .gitignore, and REPOSITORY.md; nested library and .gitmodules backed up. Existing application code and infrastructure files are preserved. Concurrent deletions of get-latest.bat, deploy-socialpost-dynamodb.bat, promote-all-lambdas.bat, and publish-all-lambdas.bat were not restored or changed by this migration.