[CmdletBinding()]
param([string]$RepoRoot)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
}

$required = @(
    'Assets/Localization/Fonts.meta',
    'Assets/Localization/Fonts/Source.meta',
    'Assets/Localization/Fonts/Source/LiberationSans.ttf',
    'Assets/Localization/Fonts/Source/LiberationSans.ttf.meta',
    'Assets/Localization/Fonts/Source/LiberationSans-OFL.txt',
    'Assets/Localization/Fonts/Source/LiberationSans-OFL.txt.meta',
    'Assets/Resources/Localization.meta',
    'Assets/Resources/Localization/Fonts.meta',
    'Assets/Resources/Localization/Fonts/ElarionLocaleFallback.asset',
    'Assets/Resources/Localization/Fonts/ElarionLocaleFallback.asset.meta'
)

$missing = New-Object 'System.Collections.Generic.List[string]'
foreach ($path in $required) {
    & git -C $RepoRoot ls-files --error-unmatch -- $path 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { $missing.Add($path) }
}
if ($missing.Count -gt 0) {
    throw 'Localization font assets are not tracked: ' + ($missing -join ', ')
}

Write-Output ('LOCALIZATION_FONT_TRACKING_OK files=' + $required.Count)
