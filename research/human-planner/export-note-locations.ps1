param(
    [Parameter(Mandatory)][string]$ManifestPath,
    [Parameter(Mandatory)][string]$OutputRoot
)
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -Raw -LiteralPath $ManifestPath | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $manifest.coreAssembly).Hash -ne $manifest.coreSha256) { throw 'Core fingerprint mismatch' }
$loadedLocationCore = [System.Reflection.Assembly]::LoadFrom($manifest.coreAssembly)
if ((Get-FileHash -LiteralPath $loadedLocationCore.Location).Hash -ne $manifest.coreSha256) { throw 'A different Core is already loaded; run this script in a fresh PowerShell process' }
$written = 0
foreach ($entry in $manifest.charts) {
    $chartPath = Join-Path $manifest.chartRoot $entry.path
    if ((Get-FileHash -LiteralPath $chartPath).Hash -ne $entry.chartSha256) { throw "Chart fingerprint mismatch: $($entry.path)" }
    $chart = [MaiPlay.Core.SimaiParser]::LoadSong($chartPath).Charts | Where-Object Difficulty -eq $entry.difficulty
    if (@($chart).Count -ne 1) { throw "Chart difficulty missing or duplicated: $($entry.path)" }
    $locations = @($chart.Notes | ForEach-Object {
        [ordered]@{
            id = $_.Id
            kind = $_.Kind.ToString()
            time = $_.Time
            endTime = $_.EndTime
            slideStartTime = $_.SlideStartTime
            startPosition = $_.StartPosition
            endPosition = $_.EndPosition
            touchArea = $_.TouchArea
            isBreak = $_.IsBreak
            isEx = $_.IsEx
            isStar = $_.IsStar
        }
    })
    $record = [ordered]@{ coreSha256 = $manifest.coreSha256; chartSha256 = $entry.chartSha256; key = $entry.key; notes = $locations }
    $directory = Join-Path $OutputRoot "charts/$($entry.key)"
    $null = [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetFullPath($directory))
    [System.IO.File]::WriteAllText((Join-Path ([System.IO.Path]::GetFullPath($directory)) 'note-locations.json'), ($record | ConvertTo-Json -Depth 6), [System.Text.UTF8Encoding]::new($false))
    $written++
}
if ((Get-FileHash -LiteralPath $manifest.coreAssembly).Hash -ne $manifest.coreSha256) { throw 'Core fingerprint changed' }
Write-Output "Exported note locations for $written charts."
