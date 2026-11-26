param(
    [Parameter(Mandatory = $true)][string]$ApiExport,
    [Parameter(Mandatory = $true)][string]$OutputFile
)
$ErrorActionPreference = 'Stop'
$api = Get-Content -LiteralPath $ApiExport -Raw | ConvertFrom-Json
$integration = $api.paths.'/organization/{organizationid}/social/posts'.post.'x-amazon-apigateway-integration'
$template = $integration.requestTemplates.'application/json'
if (-not $template -or $integration.type -ne 'aws') {
    throw 'Expected the existing non-proxy create-social-post integration.'
}
# Preserve the deployed authorization context and every existing field verbatim.
foreach ($field in @('text_social', 'tiktok')) {
    if ($template.Contains("`"$field`"")) { continue }
    $closingBrace = $template.LastIndexOf('}')
    if ($closingBrace -lt 0) { throw 'Mapping has no closing object brace.' }
    $addition = @'
#if($input.path('$').containsKey('FIELD'))
  ,"FIELD": $input.json('$.FIELD')
#end

'@.Replace('FIELD', $field)
    $template = $template.Insert($closingBrace, $addition)
}
$patch = @(@{
    op = 'replace'
    path = '/requestTemplates/application~1json'
    value = $template
})
ConvertTo-Json -InputObject $patch -Depth 10 | Set-Content -LiteralPath $OutputFile -Encoding utf8
Write-Output "Prepared create-post mapping patch: $OutputFile"
