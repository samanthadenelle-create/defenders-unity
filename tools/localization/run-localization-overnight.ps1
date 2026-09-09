[CmdletBinding()]
param(
    [switch]$SkipFullRegression
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$localizationRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))
$unityRunner = Join-Path $localizationRoot "run-unity-method.ps1"
$manifestBuilder = Join-Path $PSScriptRoot "build-string-manifest.ps1"
$fontTrackingCheck = Join-Path $PSScriptRoot "check-localization-font-assets.ps1"

function Invoke-LocalizationGate {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$LogName,
        [Parameter(Mandatory = $true)][string]$Marker
    )

    & $unityRunner -Method $Method -LogName $LogName -ExpectMarker $Marker -TimeoutMin 90
    if ($LASTEXITCODE -ne 0) {
        throw "Localization gate failed: $Method (exit $LASTEXITCODE)"
    }
}

Push-Location $localizationRoot
try {
    & $fontTrackingCheck -RepoRoot $localizationRoot
    if ($LASTEXITCODE -ne 0) { throw "Localization font tracking check failed." }

    & $manifestBuilder -RepoRoot $localizationRoot -Check
    if ($LASTEXITCODE -ne 0) { throw "Localization manifest is stale." }

    Invoke-LocalizationGate `
        -Method "DeNelle.Editor.Regression.LocalizationRegressionRunner.RunAll" `
        -LogName "localization-regression-overnight.log" `
        -Marker "LOCALIZATION_REGRESSION_OK"

    if (-not $SkipFullRegression) {
        Invoke-LocalizationGate `
            -Method "DeNelle.Editor.DataRegression.RunAll" `
            -LogName "localization-data-regression-overnight.log" `
            -Marker "REGRESSION_OK"
    }

    Invoke-LocalizationGate `
        -Method "DeNelle.Editor.Localization.LocaleSmokeCapture.Run" `
        -LogName "localization-smoke-overnight.log" `
        -Marker "LOCALIZATION_SMOKE_OK"

    Write-Output "LOCALIZATION_OVERNIGHT_OK"
}
finally {
    Pop-Location
}
