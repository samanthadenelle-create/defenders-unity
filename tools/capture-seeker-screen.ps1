param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\Builds\seeker-current.png'),
    [string]$Serial = ''
)

$ErrorActionPreference = 'Stop'
$adb = (Get-Command adb.exe -ErrorAction Stop).Source
$deviceArgs = @()
if ($Serial.Trim()) { $deviceArgs += @('-s', $Serial.Trim()) }

$lines = & $adb @deviceArgs devices | Select-Object -Skip 1 | Where-Object { $_ -match '\S+\s+device\b' }
if (@($lines).Count -ne 1) {
    throw "Expected exactly one online Android device; found $(@($lines).Count). Run 'adb devices -l' and reconnect the Seeker."
}

$parent = Split-Path -Parent $OutputPath
if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
# Use exec-out plus cmd redirection so Windows does not rewrite the PNG bytes.
$escapedOutput = $OutputPath.Replace('"', '""')
$capture = '"' + $adb + '"'
if ($Serial.Trim()) { $capture += ' -s ' + $Serial.Trim() }
$capture += ' exec-out screencap -p > "' + $escapedOutput + '"'
& cmd.exe /d /s /c $capture | Out-Null
if (-not (Test-Path $OutputPath) -or (Get-Item $OutputPath).Length -lt 100) {
    throw "ADB returned an empty or invalid screenshot: $OutputPath"
}
Write-Host "SEEKER_SCREEN_OK $OutputPath $((Get-Item $OutputPath).Length) bytes"
