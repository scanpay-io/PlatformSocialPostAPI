param(
 [ValidateSet('Publish','Deploy')][string]$Mode,
 [string]$Root=$PSScriptRoot,[switch]$Master,
 [string]$ReleaseId,[string]$Configuration='Release',[string]$AliasName,
 [Alias('Profile')][string]$AwsProfile='default',[switch]$List
)
$ErrorActionPreference='Stop'
$Root=[IO.Path]::GetFullPath($Root)
. (Join-Path $PSScriptRoot 'release-lib.ps1')
$script:Configuration=$Configuration;$script:ReleaseId=$ReleaseId;$script:AwsProfile=$AwsProfile
$script:Log=$null
$scope=if($Master){'All'}else{Split-Path $Root -Leaf}
$platforms=if($Master){@(Get-ChildItem $Root -Directory -Filter 'Platform*API' | Sort-Object Name)}else{@(Get-Item $Root)}
if($List){foreach($p in $platforms){$c=Get-Content (Join-Path $p.FullName 'lambda-runner.json') -Raw | ConvertFrom-Json;Write-Host "$($p.Name): $($c.Lambdas.Count) configured Lambdas"};exit 0}
if($ReleaseId -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$' -or $ReleaseId -in @('Release','Debug','latest')){throw 'Specify an explicit release ID, e.g. 20260911-001. The old deploy alias/configuration syntax is no longer supported.'}
if($Mode -eq 'Deploy' -and ($AliasName -notmatch '^[A-Za-z0-9_-]+$' -or $AliasName -match '^\d+$')){throw 'A valid alias name is required'}
$script:Settings=Get-Content (Join-Path $Root 'release-settings.json') -Raw | ConvertFrom-Json
foreach($pair in @(@('Bucket','GANY_RELEASE_BUCKET'),@('Region','GANY_RELEASE_REGION'),@('AccountId','GANY_RELEASE_ACCOUNT'),@('BucketOwner','GANY_RELEASE_BUCKET_OWNER'),@('Prefix','GANY_RELEASE_PREFIX'))){$v=[Environment]::GetEnvironmentVariable($pair[1]);if($v){$script:Settings.($pair[0])=$v}}
if(!$script:Settings.Bucket -or !$script:Settings.Region -or $script:Settings.AccountId -notmatch '^\d{12}$'){throw 'Configure Bucket, Region and AccountId in release-settings.json or GANY_RELEASE_* environment variables. No AWS changes made.'}
if(!$script:Settings.BucketOwner){$script:Settings.BucketOwner=$script:Settings.AccountId}
$script:Settings.Prefix=$script:Settings.Prefix.Trim('/')
if($script:Settings.Prefix -notmatch '^[A-Za-z0-9_/-]+$'){throw 'Invalid release key prefix'}
$attempt=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8)
$script:RunDir=Join-Path $Root ('artifacts\lambda-runs\'+$attempt+'-'+$Mode)
New-Item $script:RunDir -ItemType Directory -Force | Out-Null
$env:AWS_PAGER=''
$identity=Invoke-Aws @('sts','get-caller-identity')
$script:AccountId=$identity.Account
if($script:AccountId -ne $script:Settings.AccountId){throw 'AWS profile points at the wrong account; no changes made'}
$location=Invoke-Aws @('s3api','get-bucket-location','--bucket',$script:Settings.Bucket,'--expected-bucket-owner',$script:Settings.BucketOwner)
$bucketRegion=$location.LocationConstraint;if(!$bucketRegion){$bucketRegion='us-east-1'};if($bucketRegion -eq 'EU'){$bucketRegion='eu-west-1'}
if($bucketRegion -ne $script:Settings.Region){throw 'Bucket region does not match release settings'}
$versioning=Invoke-Aws @('s3api','get-bucket-versioning','--bucket',$script:Settings.Bucket,'--expected-bucket-owner',$script:Settings.BucketOwner)
if($versioning.Status -ne 'Enabled'){throw 'Enable S3 versioning on the release bucket before publishing/promoting'}
$privacy=Invoke-Aws @('s3api','get-public-access-block','--bucket',$script:Settings.Bucket,'--expected-bucket-owner',$script:Settings.BucketOwner)
foreach($flag in 'BlockPublicAcls','IgnorePublicAcls','BlockPublicPolicy','RestrictPublicBuckets'){if($privacy.PublicAccessBlockConfiguration.$flag -ne $true){throw 'All four S3 Block Public Access settings must be enabled'}}
$script:ReleaseKey=$script:Settings.Prefix+'/'+$script:AccountId+'/releases/'+$ReleaseId
$manifestPath=Join-Path $script:RunDir 'manifest.json'
$rows=New-Object 'System.Collections.Generic.List[object]'
Write-Host "$Mode release $ReleaseId; scope $scope; account $($script:AccountId). Logs: $($script:RunDir)"
if($Mode -eq 'Publish'){
 $targets=New-Object 'System.Collections.Generic.List[object]';$seen=@{};$inventoryErrors=@()
 foreach($p in $platforms){
  try {
   $c=Get-Content (Join-Path $p.FullName 'lambda-runner.json') -Raw | ConvertFrom-Json
   if(!$c.Lambdas.Count){throw 'Empty Lambda inventory'}
   foreach($name in $c.Lambdas){
    if(($p.Name+'/'+$name) -in @($script:Settings.ExcludedProjects)){Write-Host "Excluded: $($p.Name)/$name";continue}
    if($name -notmatch '^[A-Za-z0-9_-]+$'){throw 'Invalid Lambda project name'}
    $d=Get-Content (Join-Path $p.FullName "$name\aws-lambda-tools-defaults.json") -Raw | ConvertFrom-Json
    $region=$d.region;if(!$region){$region=$script:Settings.Region}
    $key=$region+'/'+$d.'function-name'
    if($seen.ContainsKey($key)){$inventoryErrors+="Duplicate AWS function $key : $($seen[$key]) and $($p.Name)/$name"}else{$seen[$key]=$p.Name+'/'+$name}
    $targets.Add([pscustomobject]@{Platform=$p;Config=$c;Lambda=$name})
   }
  }catch{$inventoryErrors+="$($p.Name): $($_.Exception.Message)"}
 }
 if($inventoryErrors.Count -or !$targets.Count){throw ('Release inventory is invalid; no functions updated. '+($inventoryErrors -join '; '))}
 $reservation=Join-Path $script:RunDir 'reservation.json'
 Write-Json $reservation @{ReleaseId=$ReleaseId;Scope=$scope;AccountId=$script:AccountId;StartedUtc=[DateTime]::UtcNow.ToString('o');PublisherArn=$identity.Arn;Attempt=$attempt}
 Put-ReleaseObject ($script:ReleaseKey+'/reservation.json') $reservation | Out-Null
 # A previously used ID is never reused, including a failed or interrupted attempt.
 $sources=@();foreach($p in $platforms){$sources+=Source-Record $p.FullName}
 foreach($lib in 'PlatformLibrary','PlatformProcessorLibrary'){$parent=if($Master){$Root}else{Split-Path $Root};$path=Join-Path $parent $lib;if(Test-Path $path){$sources+=Source-Record $path}}
 $manifest=[ordered]@{SchemaVersion=1;ReleaseId=$ReleaseId;Scope=$scope;AccountId=$script:AccountId;Region=$script:Settings.Region;Configuration=$Configuration;Status='Publishing';CreatedUtc=[DateTime]::UtcNow.ToString('o');CompletedUtc='';PublisherArn=$identity.Arn;ExpectedCount=$targets.Count;ExcludedProjects=@($script:Settings.ExcludedProjects);Sources=$sources;Functions=@()}
 foreach($group in ($targets | Group-Object {$_.Platform.Name} | Sort-Object Name)){
  $dir=Join-Path $script:RunDir $group.Name;New-Item $dir -ItemType Directory -Force | Out-Null
  foreach($target in ($group.Group | Sort-Object Lambda)){
   $entry=New-Result $group.Name $target.Lambda;$script:Log=Join-Path $dir ($target.Lambda+'.log')
   Write-Host ("Publishing $ReleaseId $($group.Name)/$($target.Lambda)")
   Set-Content $script:Log "Publishing $ReleaseId $($group.Name)/$($target.Lambda)"
   try{Publish-One $entry $target.Platform.FullName $target.Config $dir;$entry.Status='Succeeded'}catch{$entry.Detail=$_.Exception.Message;Add-Content $script:Log $entry.Detail}
   $entry.FinishedUtc=[DateTime]::UtcNow.ToString('o');$rows.Add($entry)
   Write-Host "$($entry.Status): $($entry.Lambda) $($entry.Detail)"
   $manifest.Functions=@($rows.ToArray());Write-Json $manifestPath $manifest
   Save-Results $rows $script:RunDir "Publish $ReleaseId"
  }
  Save-Results @($rows | Where-Object Platform -eq $group.Name) $dir ($group.Name+' publish summary') -Print
 }
 $bad=@($rows | Where-Object Status -ne Succeeded).Count
 $manifest.Status=if($bad){'Incomplete'}else{'Ready'};$manifest.CompletedUtc=[DateTime]::UtcNow.ToString('o')
 Write-Json $manifestPath $manifest
 Put-ReleaseObject ($script:ReleaseKey+'/manifest.json') $manifestPath | Out-Null
 Write-Host "Release $ReleaseId stored in S3: $($manifest.Status). Aliases were not changed."
 if($bad){exit 1};exit 0
}
# Promotion reads only the immutable S3 manifest. It never builds, uploads code, or publishes versions.
$manifest=Get-ReleaseObject ($script:ReleaseKey+'/manifest.json') $manifestPath
if($manifest.SchemaVersion -ne 1 -or $manifest.ReleaseId -ne $ReleaseId -or $manifest.AccountId -ne $script:AccountId -or $manifest.Region -ne $script:Settings.Region -or $manifest.Scope -ne $scope){throw 'Manifest identity/scope does not match this deployment'}
if($manifest.Status -ne 'Ready' -or !$manifest.ExpectedCount -or @($manifest.Functions).Count -ne $manifest.ExpectedCount -or @($manifest.Functions | Where-Object Status -ne Succeeded).Count){throw 'Release is incomplete; no aliases changed'}
$seen=@{};$plan=New-Object 'System.Collections.Generic.List[object]'
# Validate every exact version and capture alias revisions before changing any alias.
foreach($f in $manifest.Functions){
 $row=New-Result $f.Platform $f.Lambda;$row.FunctionName=$f.FunctionName;$row.FunctionArn=$f.FunctionArn;$row.Region=$f.Region;$row.Version=$f.Version;$row.CodeSha256=$f.CodeSha256;$row.ConfigSha256=$f.ConfigSha256
 $prior=$null
 try {
  if($f.Platform -notmatch '^Platform[A-Za-z0-9]+API$' -or $f.Lambda -notmatch '^[A-Za-z0-9_-]+$'){throw 'Invalid manifest project path'}
  if($f.Version -notmatch '^[1-9][0-9]*$' -or $f.Region -ne $script:Settings.Region -or $f.FunctionName -notmatch '^[A-Za-z0-9_-]+$'){throw 'Invalid manifest function/version'}
  $expected='arn:'+($identity.Arn -split ':')[1]+':lambda:'+$f.Region+':'+$script:AccountId+':function:'+$f.FunctionName
  if($f.FunctionArn -ne $expected -or $seen.ContainsKey($expected)){throw 'Invalid or duplicate manifest ARN'};$seen[$expected]=$true
  $v=Invoke-Aws @('lambda','get-function-configuration','--function-name',$f.FunctionArn,'--qualifier',$f.Version) $f.Region
  if($v.CodeSha256 -ne $f.CodeSha256 -or (Config-Hash $v) -ne $f.ConfigSha256){throw 'Published code/configuration checksum differs from manifest'}
  try{$prior=Invoke-Aws @('lambda','get-alias','--function-name',$f.FunctionArn,'--name',$AliasName) $f.Region}
  catch{if($_.Exception.Message -notmatch 'ResourceNotFoundException'){throw}}
  if($prior){$row.PreviousVersion=$prior.FunctionVersion}
  $row.Status='Succeeded';$row.Detail='Preflight passed'
 } catch {$row.Detail=$_.Exception.Message}
 $rows.Add($row);$plan.Add([pscustomobject]@{Target=$f;PriorAlias=$prior})
}
$audit=[ordered]@{SchemaVersion=1;ReleaseId=$ReleaseId;Scope=$scope;AccountId=$script:AccountId;Alias=$AliasName;Attempt=$attempt;OperatorArn=$identity.Arn;StartedUtc=[DateTime]::UtcNow.ToString('o');Status='Preflight';ManifestSha256=(Hash-File $manifestPath);Plan=@($plan.ToArray());Results=@()}
$auditKey=$script:Settings.Prefix+'/'+$script:AccountId+'/deployments/'+$ReleaseId+'/'+$AliasName+'/'+$attempt
$auditPath=Join-Path $script:RunDir 'deployment.json'
Write-Json $auditPath $audit
# Persist the previous pointers before the first mutation; an audit upload failure prevents promotion.
Put-ReleaseObject ($auditKey+'/plan.json') $auditPath | Out-Null
$preflightFailed=@($rows | Where-Object Status -ne Succeeded).Count -gt 0
if(!$preflightFailed){foreach($row in $rows){$row.Status='Pending';$row.Detail='Not attempted'}}
foreach($group in ($rows | Group-Object Platform | Sort-Object Name)){
 $dir=Join-Path $script:RunDir $group.Name
 # Invalid path names were rejected above; never use them for output paths.
 if($group.Name -notmatch '^Platform[A-Za-z0-9]+API$'){$dir=Join-Path $script:RunDir 'invalid-manifest'}
 New-Item $dir -ItemType Directory -Force | Out-Null
 foreach($row in $group.Group){
  if($preflightFailed){if($row.Status -eq 'Succeeded'){$row.Status='Failed';$row.Detail='Not promoted: release preflight failed'}}else{
   try{
    $prior=($plan | Where-Object {$_.Target.FunctionArn -eq $row.FunctionArn}).PriorAlias
    $payload=@{FunctionName=$row.FunctionArn;Name=$AliasName;FunctionVersion=$row.Version;Description=('Release '+$ReleaseId);RoutingConfig=@{AdditionalVersionWeights=@{}}}
    if($prior){$payload.RevisionId=$prior.RevisionId;$op='update-alias'}else{$op='create-alias'}
    # Revision guard prevents silently overwriting another operator's alias change.
    $new=Invoke-Aws @('lambda',$op) $row.Region $payload
    $verify=Invoke-Aws @('lambda','get-alias','--function-name',$row.FunctionArn,'--name',$AliasName) $row.Region
    if($verify.FunctionVersion -ne $row.Version -or $verify.Description -ne ('Release '+$ReleaseId) -or @($verify.RoutingConfig.AdditionalVersionWeights.PSObject.Properties | Where-Object MemberType -eq NoteProperty).Count){throw 'Alias verification failed'}
    $row.Status='Succeeded';$row.Detail='Alias promoted and verified'
   }catch{$row.Status='Failed';$row.Detail=$_.Exception.Message}
  }
  $row.FinishedUtc=[DateTime]::UtcNow.ToString('o')
  if($row.Lambda -match '^[A-Za-z0-9_-]+$'){Set-Content (Join-Path $dir ($row.Lambda+'.log')) ($row | ConvertTo-Json -Depth 10)}
  Write-Host "$($row.Status): $($row.Lambda) $($row.Detail)"
  $audit.Results=@($rows.ToArray());Write-Json $auditPath $audit
  Save-Results $rows $script:RunDir "Deploy $ReleaseId -> $AliasName"
 }
 Save-Results $group.Group $dir ($group.Name+' alias summary') -Print
}
$bad=@($rows | Where-Object Status -ne Succeeded).Count
$audit.Status=if($preflightFailed){'Blocked'}elseif($bad){'Partial'}else{'Succeeded'};$audit.Results=@($rows.ToArray())
Write-Json $auditPath $audit
Put-ReleaseObject ($auditKey+'/result.json') $auditPath | Out-Null
Write-Host "Deployment $($audit.Status). Exact release: $ReleaseId; alias: $AliasName. Logs: $($script:RunDir)"
if($bad){exit 1};exit 0