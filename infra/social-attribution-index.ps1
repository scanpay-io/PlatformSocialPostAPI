param(
    [Parameter(Mandatory=$true)][string]$Profile,
    [string]$Region = 'us-east-1',
    [string]$TableName = 'TransactionAmount',
    [ValidateRange(0, 2147483647)][int]$ReadCapacityUnits = 0,
    [ValidateRange(0, 2147483647)][int]$WriteCapacityUnits = 0
)
$ErrorActionPreference = 'Stop'
$description = aws dynamodb describe-table --profile $Profile --region $Region --table-name $TableName --output json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Unable to describe transaction table.' }
if ($description.Table.GlobalSecondaryIndexes.IndexName -contains 'SocialPostID-index') {
    Write-Output 'SocialPostID-index already exists. Verify ACTIVE status before deploying analytics.'
    return
}
$index = @{
    IndexName = 'SocialPostID-index'
    KeySchema = @(@{ AttributeName = 'SocialPostID'; KeyType = 'HASH' })
    Projection = @{ ProjectionType = 'ALL' }
}
if ($description.Table.BillingModeSummary.BillingMode -ne 'PAY_PER_REQUEST') {
    if ($ReadCapacityUnits -lt 1 -or $WriteCapacityUnits -lt 1) {
        throw 'For a provisioned table, specify reviewed ReadCapacityUnits and WriteCapacityUnits for the new index.'
    }
    $index.ProvisionedThroughput = @{ ReadCapacityUnits = $ReadCapacityUnits; WriteCapacityUnits = $WriteCapacityUnits }
}
$specification = @{
    TableName = $TableName
    AttributeDefinitions = @(@{ AttributeName = 'SocialPostID'; AttributeType = 'S' })
    GlobalSecondaryIndexUpdates = @(@{ Create = $index })
} | ConvertTo-Json -Depth 8
$specificationPath = Join-Path ([System.IO.Path]::GetTempPath()) ('social-attribution-' + [Guid]::NewGuid() + '.json')
try {
    [System.IO.File]::WriteAllText($specificationPath, $specification)
    aws dynamodb update-table --profile $Profile --region $Region --cli-input-json ('file://' + $specificationPath)
    if ($LASTEXITCODE -ne 0) { throw 'Index creation failed.' }
} finally {
    Remove-Item -LiteralPath $specificationPath -ErrorAction SilentlyContinue
}
