[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Manifest,
    [string]$Profile = 'default',
    [string]$Region = 'us-east-1',
    [switch]$Apply
)
$ErrorActionPreference = 'Stop'
# Deliberately no inferred environment and no scan/backfill of unproven history.
# Each manifest row must be reviewed against deployment/audit evidence.
$entries = @(Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json)
function Invoke-Dynamo([string]$Operation, [hashtable]$Payload) {
    $file = [IO.Path]::GetTempFileName()
    try {
        $Payload | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $file -Encoding utf8
        $result = & aws dynamodb $Operation --cli-input-json "file://$file" --profile $Profile --region $Region --output json
        if ($LASTEXITCODE -ne 0) { throw "DynamoDB $Operation failed." }
        return $result | ConvertFrom-Json
    } finally { Remove-Item -LiteralPath $file }
}
$plan = @()
foreach ($entry in $entries) {
    $keyName = switch ($entry.table) {
        'SocialPost' { 'SocialPostID' }
        'SocialPostDelivery' { 'DeliveryID' }
        'SocialPostClick' { 'ClickID' }
        'TransactionAmount' { 'TransactionID' }
        default { throw "Unsupported table: $($entry.table)" }
    }
    foreach ($field in @('id', 'organization_id', 'app', 'runway')) {
        if ([string]::IsNullOrWhiteSpace($entry.$field)) { throw "Manifest requires $field on every row." }
    }
    if ($entry.app.Contains(':') -or $entry.runway.Contains(':')) { throw 'Invalid scope components.' }
    $key = @{ $keyName = @{ S = [string]$entry.id } }
    $item = (Invoke-Dynamo 'get-item' @{ TableName = $entry.table; Key = $key; ConsistentRead = $true }).Item
    $ownerField = if ($entry.table -eq 'TransactionAmount') { 'PayeeAccountID' } else { 'OrganizationID' }
    if (!$item -or $item.$ownerField.S -cne $entry.organization_id) { throw "Missing row or ownership mismatch: $($entry.table)/$($entry.id)" }
    $scope = "$($entry.app):$($entry.runway):$($entry.organization_id)"
    if ($entry.table -eq 'TransactionAmount') {
        if ($item.Runway.S -cne $entry.runway -or [string]::IsNullOrWhiteSpace($item.SocialPostID.S) -or ![string]::IsNullOrWhiteSpace($item.SmsMessageID.S)) {
            throw "Transaction environment or attribution mismatch: $($entry.id)"
        }
        if (![string]::IsNullOrWhiteSpace($item.SocialAccountScopeKey.S) -and $item.SocialAccountScopeKey.S -cne $scope) { throw 'Refusing to reassign transaction scope.' }
        $plan += @{
            TableName = $entry.table; Key = $key
            UpdateExpression = 'SET SocialAccountScopeKey = :scope'
            ConditionExpression = 'PayeeAccountID = :org AND Runway = :runway AND SocialPostID = :post AND (attribute_not_exists(SocialAccountScopeKey) OR SocialAccountScopeKey = :empty OR SocialAccountScopeKey = :scope)'
            ExpressionAttributeValues = @{ ':scope' = @{ S = $scope }; ':org' = @{ S = [string]$entry.organization_id }; ':runway' = @{ S = [string]$entry.runway }; ':post' = @{ S = $item.SocialPostID.S }; ':empty' = @{ S = '' } }
        }
        continue
    }
    foreach ($pair in @(@('AppName', $entry.app), @('Runway', $entry.runway), @('AccountScopeKey', $scope))) {
        $saved = $item.($pair[0]).S
        if (![string]::IsNullOrWhiteSpace($saved) -and $saved -cne $pair[1]) { throw "Refusing to reassign scoped row: $($entry.id)" }
    }
    $values = @{ ':app' = @{ S = [string]$entry.app }; ':runway' = @{ S = [string]$entry.runway }; ':scope' = @{ S = $scope }; ':org' = @{ S = [string]$entry.organization_id }; ':empty' = @{ S = '' } }
    $condition = 'OrganizationID = :org AND (attribute_not_exists(AppName) OR AppName = :empty OR AppName = :app) AND (attribute_not_exists(Runway) OR Runway = :empty OR Runway = :runway) AND (attribute_not_exists(AccountScopeKey) OR AccountScopeKey = :empty OR AccountScopeKey = :scope)'
    $expression = 'SET AppName = :app, Runway = :runway, AccountScopeKey = :scope'
    if ($entry.table -eq 'SocialPostDelivery') {
        $published = $item.PublishedDateUtc.S
        $indexDate = 'unpublished'
        if ($published) {
            $date = [DateTimeOffset]::Parse($published, [Globalization.CultureInfo]::InvariantCulture)
            $indexDate = $date.UtcDateTime.ToString('o')
            $values[':published'] = @{ S = $published }
            $condition += ' AND PublishedDateUtc = :published'
        } else { $condition += ' AND (attribute_not_exists(PublishedDateUtc) OR attribute_type(PublishedDateUtc, :nullType))'; $values[':nullType'] = @{ S = 'NULL' } }
        $expression += ', PublishedAtKey = :indexDate'
        $values[':indexDate'] = @{ S = $indexDate }
    }
    $plan += @{ TableName = $entry.table; Key = $key; UpdateExpression = $expression; ConditionExpression = $condition; ExpressionAttributeValues = $values }
}
# Validate all rows before writing any. Conditional writes stop if a concurrent writer changes scope/publication.
if (!$Apply) { $plan | ConvertTo-Json -Depth 30; return }
foreach ($update in $plan) { $null = Invoke-Dynamo 'update-item' $update }
Write-Output "Updated $($plan.Count) explicitly reviewed rows. No historical tracking URLs or donations were fabricated."
