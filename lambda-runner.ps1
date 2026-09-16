param(
 [ValidateSet('Build')][string]$Mode='Build',
 [string]$Root=$PSScriptRoot, [switch]$Master,
 [string]$Configuration='Release',
 [switch]$List
)
$ErrorActionPreference='Stop'
$Root=[IO.Path]::GetFullPath($Root)
$runId=(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8)
$runDir=Join-Path $Root ('artifacts\lambda-runs\'+$runId+'-'+$Mode)
$results=New-Object 'System.Collections.Generic.List[object]'
function Invoke-Logged([string]$Exe,[string[]]$Arguments,[string]$Log) {
 Get-Command $Exe -ErrorAction Stop | Out-Null
 $old=$ErrorActionPreference
 try {
  $ErrorActionPreference='Continue'
  $global:LASTEXITCODE=0
  & $Exe @Arguments 2>&1 | ForEach-Object { $line=$_.ToString(); Write-Host $line; Add-Content -LiteralPath $Log -Value $line -Encoding UTF8 }
  $code=$LASTEXITCODE
 } finally {$ErrorActionPreference=$old}
 if($code -ne 0){throw "$Exe exited with code $code"}
}
function Save-Summary($Rows,[string]$Directory,[string]$Title){
 $sorted=@($Rows | Sort-Object @{Expression={if($_.Status -eq 'Succeeded'){0}else{1}}},Lambda)
 $sorted | Export-Csv (Join-Path $Directory 'results.csv') -NoTypeInformation -Encoding UTF8
 $text=@("",$Title)
 foreach($row in $sorted){$text+=('{0,-9} {1} [{2}] {3}' -f $row.Status,$row.Lambda,$row.FunctionName,$row.Detail)}
 $ok=@($sorted | Where-Object Status -eq Succeeded).Count
 $bad=@($sorted | Where-Object Status -eq Failed).Count
 $text+="Succeeded: $ok; Failed: $bad"
 $text | Set-Content (Join-Path $Directory 'summary.txt') -Encoding UTF8
 $text | ForEach-Object {Write-Host $_}
}
$platforms=if($Master){@(Get-ChildItem -LiteralPath $Root -Directory -Filter 'Platform*API' | Sort-Object Name)}else{@(Get-Item -LiteralPath $Root)}
if(!$platforms.Count){throw 'No PlatformAPIs found'}
if(!$List){New-Item $runDir -ItemType Directory -Force | Out-Null;Write-Host "Logs: $runDir"}
foreach($platform in $platforms){
 $rows=New-Object 'System.Collections.Generic.List[object]'
 $platformDir=Join-Path $runDir $platform.Name
 if(!$List){New-Item $platformDir -ItemType Directory -Force | Out-Null}
 try {
  $config=Get-Content (Join-Path $platform.FullName 'lambda-runner.json') -Raw | ConvertFrom-Json
  if(!$config.Lambdas.Count){throw 'Empty Lambda inventory'}
 } catch {
  if($List){throw}
  $row=[pscustomobject]@{Platform=$platform.Name;Lambda='(platform setup)';FunctionName='';Mode=$Mode;Status='Failed';Detail=$_.Exception.Message;Log='';StartedUtc=[DateTime]::UtcNow.ToString('o');FinishedUtc=[DateTime]::UtcNow.ToString('o')}
  $rows.Add($row);$results.Add($row);Save-Summary $rows $platformDir $platform.Name;continue
 }
 if($List){Write-Host "$($platform.Name): $($config.Lambdas.Count) Lambdas"; $config.Lambdas | ForEach-Object {Write-Host "  $_"};continue}
 foreach($lambda in ($config.Lambdas | Sort-Object)){
  $log=Join-Path $platformDir ($lambda+'.log')
  $started=[DateTime]::UtcNow.ToString('o');$status='Failed';$detail='';$functionName=''
  Set-Content $log "$Mode $($platform.Name)/$lambda - $started" -Encoding UTF8
  try {
   $projectDir=Join-Path $platform.FullName $lambda
   $defaults=Get-Content (Join-Path $projectDir 'aws-lambda-tools-defaults.json') -Raw | ConvertFrom-Json
   $functionName=$defaults.'function-name'
   $projects=@(Get-ChildItem -LiteralPath $projectDir -Filter '*.csproj')
   if($projects.Count -ne 1){throw 'Expected exactly one Lambda project'}
   Write-Host "`n$Mode $($platform.Name)/$lambda [$functionName]"
   Push-Location $projectDir
   try {
    Invoke-Logged 'dotnet' @('build',$projects[0].FullName,'-c',$Configuration,'-m:1') $log
    $detail='Built'
   } finally {Pop-Location}
   $status='Succeeded'
  } catch {$detail=$_.Exception.Message;Add-Content $log $detail;Write-Host "FAILED: $lambda - $detail"}
  $row=[pscustomobject]@{Platform=$platform.Name;Lambda=$lambda;FunctionName=$functionName;Mode=$Mode;Status=$status;Detail=$detail;Log=$log;StartedUtc=$started;FinishedUtc=[DateTime]::UtcNow.ToString('o')}
  $rows.Add($row);$results.Add($row)
  # Flush after each Lambda so interrupted runs retain completed results.
  $rows | Export-Csv (Join-Path $platformDir 'results.csv') -NoTypeInformation -Encoding UTF8
  $results | Export-Csv (Join-Path $runDir 'results.csv') -NoTypeInformation -Encoding UTF8
  Write-Host "$status`: $lambda ($detail)"
 }
 Save-Summary $rows $platformDir ($platform.Name+' '+$Mode+' summary')
}
if($List){exit 0}
$failed=@($results | Where-Object Status -eq Failed).Count
Write-Host "`nFinished: $($results.Count-$failed) succeeded; $failed failed. Logs: $runDir"
if($failed){exit 1}
exit 0