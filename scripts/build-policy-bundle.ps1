param(
  [string]$PoliciesPath = "policies",
  [string]$OutputDir = "artifacts",
  [string]$Version = "local"
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$bundlePath = Join-Path $OutputDir "policy-bundle-$Version.json"
$sigPath = "$bundlePath.sig"

$org = Get-Content -Raw (Join-Path $PoliciesPath "org.yml")
$models = Get-Content -Raw (Join-Path $PoliciesPath "models.yml")
$tools = Get-Content -Raw (Join-Path $PoliciesPath "tools.yml")
$workspaces = Get-ChildItem (Join-Path $PoliciesPath "workspaces") -Filter *.yml | ForEach-Object {
  [PSCustomObject]@{
    name = $_.Name
    content = Get-Content -Raw $_.FullName
  }
}

$bundle = [ordered]@{
  version = $Version
  createdAtUtc = (Get-Date).ToUniversalTime().ToString("o")
  author = "ci"
  files = [ordered]@{
    org = $org
    models = $models
    tools = $tools
    workspaces = $workspaces
  }
}

$bundle | ConvertTo-Json -Depth 20 | Set-Content -NoNewline -Encoding utf8 $bundlePath

if ($env:LEASEGATE_POLICY_SIGNING_KEY) {
  $keyFile = Join-Path $env:TEMP "leasegate-policy-signing.pem"
  Set-Content -Path $keyFile -Value $env:LEASEGATE_POLICY_SIGNING_KEY -Encoding ascii
  openssl dgst -sha256 -sign $keyFile -out $sigPath $bundlePath
} else {
  Set-Content -Path $sigPath -Value "unsigned-local" -Encoding ascii
}

Write-Host "Built policy bundle: $bundlePath"
Write-Host "Signature file: $sigPath"
