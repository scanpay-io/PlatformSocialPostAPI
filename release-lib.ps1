# Shared release functions. Windows PowerShell 5.1 compatible.
function Write-Json($Path,$Value) {
 [IO.File]::WriteAllText($Path,($Value | ConvertTo-Json -Depth 60),[Text.UTF8Encoding]::new($false))
}
function Invoke-Tool([string]$Exe,[string[]]$Arguments,[switch]$Show,[string]$InputText) {
 Get-Command $Exe -ErrorAction Stop | Out-Null
 $old=$ErrorActionPreference
 try {
  $ErrorActionPreference='Continue';$global:LASTEXITCODE=0
  if($PSBoundParameters.ContainsKey('InputText')){$lines=@($InputText | & $Exe @Arguments 2>&1)}
  else {$lines=@(& $Exe @Arguments 2>&1)}
  $code=$LASTEXITCODE
 } finally {$ErrorActionPreference=$old}
 $text=($lines | ForEach-Object {$_.ToString()}) -join "`n"
 if($Show){Write-Host $text;if($script:Log){Add-Content -LiteralPath $script:Log -Value $text}}
 if($code -ne 0){throw "$Exe failed ($code): $text"}
 return $text
}
function Invoke-Aws([string[]]$Arguments,[string]$Region=$script:Settings.Region,$Payload) {
 $a=@($Arguments)+@('--profile',$script:AwsProfile,'--region',$Region,'--output','json','--no-cli-pager')
 $file=$null
 try {
  if($null -ne $Payload){$file=Join-Path $script:RunDir ('request-'+[guid]::NewGuid().ToString('N')+'.json');Write-Json $file $Payload;$a+=@('--cli-input-json',('file://'+$file))}
  $text=Invoke-Tool 'aws' $a
  if($text.Trim()){return ($text | ConvertFrom-Json)}
 } finally {if($file -and (Test-Path -LiteralPath $file)){Remove-Item -LiteralPath $file -Force}}
}
function Put-ReleaseObject([string]$Key,[string]$Path) {
 # Conditional create prevents a release/artifact/audit record from being overwritten.
 return Invoke-Aws @('s3api','put-object','--bucket',$script:Settings.Bucket,'--key',$Key,'--body',$Path,'--if-none-match','*','--expected-bucket-owner',$script:Settings.BucketOwner)
}
function Get-ReleaseObject([string]$Key,[string]$Path) {
 Invoke-Aws @('s3api','get-object','--bucket',$script:Settings.Bucket,'--key',$Key,'--expected-bucket-owner',$script:Settings.BucketOwner,$Path) | Out-Null
 return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}
function Hash-File([string]$Path) {
 $sha=[Security.Cryptography.SHA256]::Create();$stream=[IO.File]::OpenRead($Path)
 try {return [Convert]::ToBase64String($sha.ComputeHash($stream))}finally{$stream.Dispose();$sha.Dispose()}
}
function Canonical($Value) {
 if($null -eq $Value){return 'null'}
 if($Value -is [System.Collections.IDictionary]){$parts=foreach($k in ($Value.Keys | Sort-Object)){(ConvertTo-Json ([string]$k) -Compress)+':'+(Canonical $Value[$k])};return '{'+($parts -join ',')+'}'}
 if($Value -is [pscustomobject]){$map=@{};foreach($p in $Value.PSObject.Properties){$map[$p.Name]=$p.Value};return Canonical $map}
 if($Value -is [array]){$parts=foreach($item in $Value){Canonical $item};return '['+($parts -join ',')+']'}
 return ConvertTo-Json $Value -Compress
}
function Config-Hash($Config) {
 if($Config.Environment.Error -or $Config.ImageConfigResponse.Error){throw 'AWS configuration could not be fully read'}
 # Code hash is recorded separately. Do not put environment values in the manifest.
 $map=@{}
 foreach($name in @('Runtime','Role','Handler','Timeout','MemorySize','Environment','VpcConfig','DeadLetterConfig','KMSKeyArn','TracingConfig','Layers','FileSystemConfigs','PackageType','Architectures','EphemeralStorage','LoggingConfig','ImageConfigResponse')){
  if($Config.PSObject.Properties[$name]){$map[$name]=$Config.$name}
 }
 if($Config.SnapStart){$map.SnapStartApplyOn=$Config.SnapStart.ApplyOn}
 # Normalize layers to their immutable ARNs (other fields can differ by API response).
 if($map.ContainsKey('Layers')){$map.Layers=@($Config.Layers | ForEach-Object {$_.Arn})}
 if($map.ContainsKey('Environment')){$map.Environment=$Config.Environment.Variables}
 if($map.ContainsKey('ImageConfigResponse')){$map.ImageConfigResponse=$Config.ImageConfigResponse.ImageConfig}
 $sha=[Security.Cryptography.SHA256]::Create()
 try {return [Convert]::ToBase64String($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes((Canonical $map))))}finally{$sha.Dispose()}
}
function Source-Record([string]$Path) {
 # Best-effort revision metadata; artifact checksums identify the actual packaged bytes.
 try {$head=Invoke-Tool 'git' @('-C',$Path,'rev-parse','HEAD');$dirty=Invoke-Tool 'git' @('-C',$Path,'status','--porcelain');return @{Repository=(Split-Path $Path -Leaf);Commit=$head.Trim();Dirty=[bool]$dirty.Trim()}}
 catch {return @{Repository=(Split-Path $Path -Leaf);Commit=$null;Dirty=$true;Note='No readable commit; artifact checksum is authoritative'}}
}
function New-Result([string]$Platform,[string]$Lambda) {
 return [pscustomobject][ordered]@{Platform=$Platform;Lambda=$Lambda;FunctionName='';Region='';FunctionArn='';Version='';CodeSha256='';ConfigSha256='';Artifact=$null;Status='Failed';Detail='';PreviousVersion='';StartedUtc=[DateTime]::UtcNow.ToString('o');FinishedUtc=''}
}
function Save-Results($Rows,[string]$Directory,[string]$Title,[switch]$Print) {
 New-Item $Directory -ItemType Directory -Force | Out-Null
 $sorted=@($Rows | Sort-Object @{Expression={if($_.Status -eq 'Succeeded'){0}else{1}}},Lambda)
 $sorted | Select-Object Platform,Lambda,FunctionName,Region,Version,Status,Detail,PreviousVersion,StartedUtc,FinishedUtc | Export-Csv (Join-Path $Directory 'results.csv') -NoTypeInformation -Encoding UTF8
 $lines=@($Title)+@($sorted | ForEach-Object {'{0,-9} {1} [{2}:{3}] {4}' -f $_.Status,$_.Lambda,$_.FunctionName,$_.Version,$_.Detail})
 $lines+='Succeeded: '+@($sorted | Where-Object Status -eq Succeeded).Count+'; Failed: '+@($sorted | Where-Object Status -ne Succeeded).Count
 $lines | Set-Content (Join-Path $Directory 'summary.txt') -Encoding UTF8
 if($Print){$lines | ForEach-Object {Write-Host $_}}
}
function Find-PublishedVersion($Current,[string]$Region) {
 $versions=Invoke-Aws @('lambda','list-versions-by-function','--function-name',$Current.FunctionArn) $Region
 $target=Config-Hash $Current
 foreach($v in @($versions.Versions | Where-Object {$_.Version -match '^[1-9][0-9]*$' -and $_.CodeSha256 -eq $Current.CodeSha256} | Sort-Object {[long]$_.Version} -Descending)){
  $full=Invoke-Aws @('lambda','get-function-configuration','--function-name',$Current.FunctionArn,'--qualifier',$v.Version) $Region
  if((Config-Hash $full) -eq $target){return $full}
 }
 return $null
}
function Publish-One($Entry,[string]$PlatformPath,$PlatformConfig,[string]$Directory) {
 $projectDir=Join-Path $PlatformPath $Entry.Lambda
 $defaults=Get-Content (Join-Path $projectDir 'aws-lambda-tools-defaults.json') -Raw | ConvertFrom-Json
 $Entry.FunctionName=$defaults.'function-name';$Entry.Region=$defaults.region
 if(!$Entry.Region){$Entry.Region=$script:Settings.Region}
 if($Entry.FunctionName -notmatch '^[A-Za-z0-9_-]+$'){throw 'Expected an unqualified Lambda function name in defaults'}
 if($Entry.Region -ne $script:Settings.Region){throw 'Release bucket must be in the same region as this Lambda; publish a separate regional release'}
 $live=Invoke-Aws @('lambda','get-function','--function-name',$Entry.FunctionName) $Entry.Region
 $current=$live.Configuration
 if($current.FunctionArn -notmatch (':'+$script:AccountId+':function:')){throw 'Function account mismatch'}
 $Entry.FunctionArn=$current.FunctionArn
 if($current.State -ne 'Active' -or $current.LastUpdateStatus -eq 'InProgress'){throw 'Function is not ready for a code update'}
 $projects=@(Get-ChildItem $projectDir -Filter '*.csproj')
 if($projects.Count -ne 1){throw 'Expected exactly one Lambda project'}
 $arch=$defaults.'function-architecture';if(!$arch){$arch='x86_64'}
 if($current.Architectures[0] -ne $arch){throw 'Architecture differs from live function; update infrastructure first'}
 $type=$defaults.'package-type';if(!$type){$type='Zip'}
 if($current.PackageType -ne $type){throw 'Package type differs from live function'}
 if($type -eq 'Zip' -and ($current.Runtime -ne $defaults.'function-runtime' -or $current.Handler -ne $defaults.'function-handler')){throw 'Runtime/handler differs from live function; update infrastructure first'}
 $beforeConfig=Config-Hash $current
 $payload=@{FunctionName=$Entry.FunctionArn;RevisionId=$current.RevisionId;Publish=$false}
 Push-Location $projectDir
 try {
  if($type -eq 'Image'){
   $dockerfile=$PlatformConfig.ImageDockerfiles.($Entry.Lambda)
   if(!$dockerfile){throw 'No release Dockerfile configured for image Lambda'}
   $context=Join-Path $Directory ($Entry.Lambda+'-image');$out=Join-Path $context 'publish';New-Item $out -ItemType Directory -Force | Out-Null
   $rid='linux-x64';$dockerArch='linux/amd64';if($arch -eq 'arm64'){$rid='linux-arm64';$dockerArch='linux/arm64'}
   Invoke-Tool 'dotnet' @('publish',$projects[0].FullName,'-c',$script:Configuration,'-r',$rid,'--self-contained','false','-o',$out,'-m:1') -Show | Out-Null
   $repoUri=($live.Code.ResolvedImageUri -split '@')[0]
   if($repoUri -notmatch ('^'+$script:AccountId+'\.dkr\.ecr\.'+[regex]::Escape($Entry.Region)+'\.amazonaws\.com/')){throw 'Expected an existing ECR image in the release account/region'}
   $hostName=($repoUri -split '/')[0];$repoName=$repoUri.Substring($hostName.Length+1);$tag='release-'+$script:ReleaseId
   $password=Invoke-Tool 'aws' @('ecr','get-login-password','--region',$Entry.Region,'--profile',$script:AwsProfile)
   Invoke-Tool 'docker' @('login','--username','AWS','--password-stdin',$hostName) -InputText $password | Out-Null
   $password=$null
   Invoke-Tool 'docker' @('build','--platform',$dockerArch,'-f',(Join-Path $projectDir $dockerfile),'-t',($repoUri+':'+$tag),$context) -Show | Out-Null
   Invoke-Tool 'docker' @('push',($repoUri+':'+$tag)) -Show | Out-Null
   $image=Invoke-Aws @('ecr','describe-images','--repository-name',$repoName,'--image-ids',('imageTag='+$tag)) $Entry.Region
   $digest=$image.imageDetails[0].imageDigest;if($digest -notmatch '^sha256:[0-9a-f]{64}$'){throw 'No immutable ECR image digest returned'}
   $payload.ImageUri=$repoUri+'@'+$digest
   $Entry.Artifact=@{Type='Image';ImageUri=$payload.ImageUri;Digest=$digest}
  } else {
   $zip=Join-Path $Directory ($Entry.Lambda+'.zip')
   Invoke-Tool 'dotnet' @('lambda','package','--configuration',$script:Configuration,'--output-package',$zip,'--profile',$script:AwsProfile,'--function-architecture',$arch,'--disable-interactive','true') -Show | Out-Null
   if(!(Test-Path -LiteralPath $zip)){throw 'Packaging produced no ZIP'}
   $hash=Hash-File $zip;$key=$script:ReleaseKey+'/packages/'+$Entry.Platform+'/'+$Entry.Lambda+'.zip'
   $stored=Put-ReleaseObject $key $zip
   $Entry.Artifact=@{Type='Zip';Bucket=$script:Settings.Bucket;Key=$key;ObjectVersion=$stored.VersionId;Sha256=$hash}
   $payload.S3Bucket=$script:Settings.Bucket;$payload.S3Key=$key
   if($stored.VersionId){$payload.S3ObjectVersion=$stored.VersionId}
   # Reuse an exact existing code/configuration snapshot instead of forcing version numbers.
   if($current.CodeSha256 -eq $hash){$existing=Find-PublishedVersion $current $Entry.Region;if($existing){$Entry.Version=$existing.Version;$Entry.CodeSha256=$existing.CodeSha256;$Entry.ConfigSha256=Config-Hash $existing;$Entry.Detail='Reused exact published snapshot';return}}
  }
 } finally {Pop-Location}
 $updated=Invoke-Aws @('lambda','update-function-code') $Entry.Region $payload
 Invoke-Aws @('lambda','wait','function-updated-v2','--function-name',$Entry.FunctionArn) $Entry.Region | Out-Null
 $latest=Invoke-Aws @('lambda','get-function-configuration','--function-name',$Entry.FunctionArn) $Entry.Region
 # Validate the completed update; PublishVersion below uses the freshly read RevisionId.
 if($latest.State -ne 'Active' -or $latest.LastUpdateStatus -ne 'Successful' -or $latest.CodeSha256 -ne $updated.CodeSha256){throw 'Function is not active, update failed, or uploaded code changed'}
 if($type -eq 'Zip' -and $latest.CodeSha256 -ne $Entry.Artifact.Sha256){throw 'Uploaded code checksum does not match package'}
 if((Config-Hash $latest) -ne $beforeConfig){throw 'Function configuration changed during publication'}
 $existing=Find-PublishedVersion $latest $Entry.Region
 if($existing){$version=$existing;$Entry.Detail='Reused exact published snapshot'}else{
  $version=Invoke-Aws @('lambda','publish-version') $Entry.Region @{FunctionName=$Entry.FunctionArn;CodeSha256=$latest.CodeSha256;RevisionId=$latest.RevisionId;Description=('Release '+$script:ReleaseId)}
  $Entry.Detail='Published immutable version'
 }
 if($version.Version -notmatch '^[1-9][0-9]*$' -or $version.CodeSha256 -ne $latest.CodeSha256){throw 'Published version did not match expected code'}
 $Entry.Version=$version.Version;$Entry.CodeSha256=$version.CodeSha256;$Entry.ConfigSha256=Config-Hash $version
}