$ErrorActionPreference='Stop'
. "$PSScriptRoot/../release-lib.ps1"
$checks=0
foreach($key in @($null,'not-base64',[Convert]::ToBase64String([byte[]]::new(31)))) {
 $rejected=$false
 try { Assert-RequiredFunctionEnvironment ([pscustomobject]@{FunctionName='scanpay_get_social_post_analytics';Environment=@{Variables=@{SOCIAL_ANALYTICS_CURSOR_KEY=$key}}}) }
 catch { $rejected=$true }
 if(!$rejected){throw 'Invalid key was accepted'}
 $checks++
}
Assert-RequiredFunctionEnvironment ([pscustomobject]@{FunctionName='scanpay_get_social_post_analytics';Environment=@{Variables=@{SOCIAL_ANALYTICS_CURSOR_KEY=[Convert]::ToBase64String([byte[]]::new(32))}}})
$checks++
Assert-RequiredFunctionEnvironment ([pscustomobject]@{FunctionName='unrelated_function'})
$checks++
$defaults=Get-Content "$PSScriptRoot/../SocialPostAPIGetSocialPostAnalytics/aws-lambda-tools-defaults.json" -Raw | ConvertFrom-Json
if($defaults.PSObject.Properties.Name -contains 'environment-variables'){throw 'Defaults would replace the environment'}
if(!$defaults.'append-environment-variables'){throw 'Expected append-only defaults'}
$checks++
function CursorConfig([byte]$value) {
 $bytes=[byte[]]::new(32)
 for($i=0;$i -lt $bytes.Length;$i++){$bytes[$i]=$value}
 return [pscustomobject]@{FunctionName='scanpay_get_social_post_analytics';Environment=@{Variables=@{SOCIAL_ANALYTICS_CURSOR_KEY=[Convert]::ToBase64String($bytes)}}}
}
$prod=CursorConfig 1
$dev=CursorConfig 2
Assert-AnalyticsPromotionEnvironment $prod $prod $dev
$checks++
foreach($case in @(@($dev,$prod,$dev),@((CursorConfig 3),$prod,$dev))){
 $rejected=$false
 try {Assert-AnalyticsPromotionEnvironment $case[0] $case[1] $case[2]} catch {$rejected=$true}
 if(!$rejected){throw 'Cross-environment key reuse or unintended rotation was accepted'}
 $checks++
}
Assert-AnalyticsPromotionEnvironment $prod $null $dev
$checks++
Write-Output "Passed $checks cursor deployment guard checks."
