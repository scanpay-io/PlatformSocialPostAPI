param(
    [Parameter(Mandatory=$true)][string]$Profile,
    [string]$Region = 'us-east-1',
    [string]$TableName = 'TransactionAmount'
)
$ErrorActionPreference = 'Stop'
$description = aws dynamodb describe-table --profile $Profile --region $Region --table-name $TableName --output json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Unable to describe transaction table.' }
if ($description.Table.GlobalSecondaryIndexes.IndexName -contains 'SocialPostID-index') {
    Write-Output 'SocialPostID-index already exists. Verify ACTIVE status before deploying analytics.'
    return
}
if ($description.Table.BillingModeSummary.BillingMode -ne 'PAY_PER_REQUEST') {
    throw 'This migration requires an on-demand table. Review provisioned capacity before creating the index.'
}
$specification = @{
    TableName = $TableName
    AttributeDefinitions = @(@{ AttributeName = 'SocialPostID'; AttributeType = 'S' })
    GlobalSecondaryIndexUpdates = @(@{ Create = @{
        IndexName = 'SocialPostID-index'
        KeySchema = @(@{ AttributeName = 'SocialPostID'; KeyType = 'HASH' })
        Projection = @{ ProjectionType = 'ALL' }
    } })
} | ConvertTo-Json -Depth 8
$specificationPath = Join-Path ([System.IO.Path]::GetTempPath()) ('social-attribution-' + [Guid]::NewGuid() + '.json')
try {
    [System.IO.File]::WriteAllText($specificationPath, $specification)
    aws dynamodb update-table --profile $Profile --region $Region --cli-input-json ('file://' + $specificationPath)
    if ($LASTEXITCODE -ne 0) { throw 'Index creation failed.' }
} finally {
    Remove-Item -LiteralPath $specificationPath -ErrorAction SilentlyContinue
}
