param(
    [Parameter(Mandatory)][string]$CoreAssembly,
    [Parameter(Mandatory)][string]$ExpectedCoreHash,
    [Parameter(Mandatory)][string]$ChartRoot,
    [Parameter(Mandatory)][string]$OutputPath,
    [string[]]$Levels = @('14', '14+')
)
$ErrorActionPreference = 'Stop'
$corePath = (Resolve-Path -LiteralPath $CoreAssembly).Path
if ((Get-FileHash -LiteralPath $corePath).Hash -ne $ExpectedCoreHash) { throw 'Core fingerprint mismatch' }
$loadedLevelCore = [System.Reflection.Assembly]::LoadFrom($corePath)
if ((Get-FileHash -LiteralPath $loadedLevelCore.Location).Hash -ne $ExpectedCoreHash) { throw 'A different Core is already loaded; run this script in a fresh PowerShell process' }
$chartDirectory = (Resolve-Path -LiteralPath $ChartRoot).Path
$catalogFiles = @(Get-ChildItem -LiteralPath $chartDirectory -Recurse -Filter maidata.txt -File | Sort-Object FullName)
$catalogRows = [System.Collections.Generic.List[object]]::new()
$catalogErrors = [System.Collections.Generic.List[object]]::new()
foreach ($chartFile in $catalogFiles) {
    $relative = [System.IO.Path]::GetRelativePath($chartDirectory, $chartFile.FullName).Replace('\', '/')
    try {
        $song = [MaiPlay.Core.SimaiParser]::LoadSong($chartFile.FullName)
        foreach ($chart in $song.Charts) {
            $level = $chart.Level.Trim()
            $display = $null
            if ($level.EndsWith('+')) {
                $display = $level
            } else {
                $constant = [decimal]0
                if ([decimal]::TryParse($level, [System.Globalization.NumberStyles]::AllowDecimalPoint, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$constant)) {
                    $whole = [decimal]::Floor($constant)
                    $display = $whole.ToString([System.Globalization.CultureInfo]::InvariantCulture)
                    if ($constant - $whole -ge [decimal]0.6) { $display += '+' }
                }
            }
            if ($display -notin $Levels) { continue }
            $identity = [System.Text.Encoding]::UTF8.GetBytes($relative + '#' + $chart.Difficulty)
            $catalogRows.Add([ordered]@{
                key = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($identity)).Substring(0, 16).ToLowerInvariant()
                path = $relative
                chartSha256 = (Get-FileHash -LiteralPath $chartFile.FullName).Hash
                title = $chart.Title
                difficulty = $chart.Difficulty
                difficultyName = $chart.DifficultyName
                level = $chart.Level
                displayLevel = $display
                notes = $chart.Notes.Count
                duration = $chart.Duration
            })
        }
    } catch {
        $catalogErrors.Add([ordered]@{ path = $relative; error = $_.Exception.Message })
    }
}
$manifest = [ordered]@{
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    coreSha256 = $ExpectedCoreHash
    coreAssembly = $corePath
    chartRoot = $chartDirectory
    files = $catalogFiles.Count
    levels = $Levels
    displayLevelRule = 'decimal floor; fractional part >= 0.6 is plus; explicit plus accepted'
    parseErrors = @($catalogErrors.ToArray())
    charts = @($catalogRows.ToArray())
}
$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
if ((Get-FileHash -LiteralPath $corePath).Hash -ne $ExpectedCoreHash) { throw 'Core fingerprint changed' }
$null = [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($outputFullPath))
[System.IO.File]::WriteAllText($outputFullPath, ($manifest | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
$catalogRows | ForEach-Object { [pscustomobject]$_ } | Group-Object difficulty, displayLevel | Select-Object Name, Count
Write-Output "CHARTS=$($catalogRows.Count); FILES=$($catalogFiles.Count); PARSE_ERRORS=$($catalogErrors.Count)"
