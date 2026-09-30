[CmdletBinding()]
param(
    [string]$RequestPath = 'D:\OBS-Live\OBS-Live-Bot\data\runtime\socialstream\browser-open.request'
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $RequestPath -PathType Leaf)) {
    throw 'The Social Stream OAuth browser request is not available.'
}

$raw = [System.IO.File]::ReadAllText($RequestPath).Trim()
$uri = $null
if (-not [Uri]::TryCreate($raw, [UriKind]::Absolute, [ref]$uri)) {
    throw 'The Social Stream OAuth browser request is invalid.'
}

$allowedHosts = @(
    'sso.socialstream.ninja',
    'ytauth.socialstream.ninja',
    'accounts.google.com'
)
if ($uri.Scheme -ne 'https' -or $uri.Host -notin $allowedHosts) {
    throw 'The Social Stream OAuth browser request is not allowlisted.'
}

Remove-Item -LiteralPath $RequestPath -Force
Start-Process -FilePath $uri.AbsoluteUri
Write-Output 'SOCIALSTREAM_OAUTH_BROWSER=OPENED'
