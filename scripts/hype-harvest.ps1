# Offline hype-case harvesting (Step 7 of docs/hype-intelligence-plan.md).
# Drives the operator-gated POST /api/timemachine/hype/harvest route across
# seed (symbol, date) pairs with a top-N move override, freezing every move
# as a HypeCase for signal mining. Product default (top-5) is untouched:
# the route 404s unless the backend runs with Hype__HarvestEnabled=true.
#
# Run from the repository root, backend running in Development:
#   $env:Hype__HarvestEnabled = "true"   # on the backend process
#   .\scripts\hype-harvest.ps1 -Symbols "MSFT,NFLX,AAPL" -Dates "2026-06-15,2026-08-28" -TopMoves 10
param(
    [string]$BaseUrl = "http://localhost:5251",
    [string]$Symbols = "MSFT,NFLX",
    [string]$Dates = "2026-06-15",
    [string]$NewsSource = "gdelt",
    [int]$TopMoves = 10
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$total = 0
$symbolList = ($Symbols -split ',') | ForEach-Object { $_.Trim() } | Where-Object { $_ }
$dateList = ($Dates -split ',') | ForEach-Object { $_.Trim() } | Where-Object { $_ }
foreach ($symbol in $symbolList) {
    foreach ($date in $dateList) {
        $body = @{
            symbol = $symbol
            date = $date
            newsSource = $NewsSource
            topMoves = $TopMoves
        } | ConvertTo-Json
        try {
            $resp = Invoke-RestMethod -Uri "$BaseUrl/api/timemachine/hype/harvest" `
                -Method Post -ContentType "application/json" -Body $body -TimeoutSec 600
            Write-Host "harvested $($resp.symbol) $($resp.asOfDate): $($resp.casesSaved) cases"
            $total += $resp.casesSaved
        }
        catch {
            Write-Warning "harvest failed for $symbol $date : $($_.Exception.Message)"
        }
    }
}
Write-Host "HYPE HARVEST DONE: $total cases"
