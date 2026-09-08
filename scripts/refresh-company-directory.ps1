# Refreshes backend/StockTimeMachine/Domain/companies.json from the
# daily-updated SEC mirror (jadchaar/sec-cik-mapper), overlaid with the
# curated sector/industry/display-name rows in companies.curated.json.
#
# Why this source: SEC company_tickers.json is the canonical ticker->CIK
# mapping, but sec.gov rate-limits automated pulls by IP. The sec-cik-mapper
# mirror republishes the same SEC data daily on GitHub/jsDelivr with
# ticker->name and ticker->exchange included, so one pull yields the full
# row the directory needs (CIK for EDGAR, name for search/relevance,
# exchange for display). No API key, no quota, no rate limits.
#
# PS 5.1-safe. Run quarterly, or whenever coverage looks stale.
# The loader (JsonCompanyDirectory) reads companies.json unchanged, and
# STM_DIRECTORY_PATH still overrides the path for testing.

$ErrorActionPreference = "Stop"
$base = "https://raw.githubusercontent.com/jadchaar/sec-cik-mapper/main/mappings/stocks"
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) "stm-company-refresh"
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

$headers = @{ "User-Agent" = "StockTimeMachine/1.0 (company-directory refresh)" }
$t2c = Invoke-RestMethod -Uri "$base/ticker_to_cik.json" -Headers $headers -TimeoutSec 120
$t2n = Invoke-RestMethod -Uri "$base/ticker_to_company_name.json" -Headers $headers -TimeoutSec 120
$t2e = Invoke-RestMethod -Uri "$base/ticker_to_exchange.json" -Headers $headers -TimeoutSec 120

$domain = Join-Path $PSScriptRoot "..\backend\StockTimeMachine\Domain"
$curated = Get-Content (Join-Path $domain "companies.curated.json") -Raw | ConvertFrom-Json -AsHashtable

$out = [ordered]@{}
foreach ($prop in $t2c.PSObject.Properties)
{
    $sym = $prop.Name.ToUpperInvariant()
    $cik = "$($prop.Value)".PadLeft(10, "0")
    $name = $t2n.($prop.Name)
    if ([string]::IsNullOrWhiteSpace($name)) { $name = $sym }
    $ex = $t2e.($prop.Name)
    if ($null -eq $ex) { $ex = "" }
    $row = [ordered]@{
        name = "$name"; cik = $cik; exchange = "$ex"; sector = ""; industry = ""
    }
    if ($curated.ContainsKey($sym))
    {
        $c = $curated[$sym]
        if (-not [string]::IsNullOrWhiteSpace($c["name"])) { $row.name = $c["name"] }
        if (-not [string]::IsNullOrWhiteSpace($c["exchange"])) { $row.exchange = $c["exchange"] }
        $row.sector = "$($c["sector"])"
        $row.industry = "$($c["industry"])"
    }
    $out[$sym] = $row
}
foreach ($sym in $curated.Keys)
{
    $upper = "$sym".ToUpperInvariant()
    if (-not $out.Contains($upper))
    {
        $c = $curated[$sym]
        $out[$upper] = [ordered]@{
            name = "$($c["name"])"; cik = "$($c["cik"])"
            exchange = "$($c["exchange"])"
            sector = "$($c["sector"])"
            industry = "$($c["industry"])"
        }
    }
}

$dest = Join-Path $domain "companies.json"
Copy-Item $dest (Join-Path $tmp "companies.json.bak") -Force
$out | ConvertTo-Json -Depth 4 | Set-Content $dest -Encoding UTF8
"Refreshed $dest : $($out.Count) companies (backup in $tmp)."
