<#
.SYNOPSIS
  Builds the LED-Next LaunchBox/BigBox plugin against LaunchBox's real plugin API and installs it.

.DESCRIPTION
  Writes ONLY to <LaunchBox>\Plugins\LED-Next\ (the plugin DLLs and LedNext.LaunchBox.json). Nothing
  else in the LaunchBox install is touched. -Uninstall removes that one folder.
  LaunchBox and BigBox must be closed (they lock plugin DLLs while running).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\install-launchbox-plugin.ps1
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\install-launchbox-plugin.ps1 -LaunchBox E:\LaunchBox -LedNextExe C:\LED-Next\LED-Next.exe
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\install-launchbox-plugin.ps1 -Uninstall
#>
param(
    [string]$LaunchBox = "D:\LaunchBox",
    # The LED-Next the plugin starts (minimized) when LaunchBox/BigBox start. Default: this repo's build.
    [string]$LedNextExe = "",
    [switch]$NoAutoStart,
    [switch]$Uninstall
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$target = Join-Path $LaunchBox "Plugins\LED-Next"

if (-not (Test-Path (Join-Path $LaunchBox "Core\Unbroken.LaunchBox.Plugins.dll"))) {
    throw "No LaunchBox at $LaunchBox (Core\Unbroken.LaunchBox.Plugins.dll not found). Pass -LaunchBox <folder>."
}
$running = Get-Process -Name LaunchBox, BigBox -ErrorAction SilentlyContinue
if ($running) { throw "Close LaunchBox and BigBox first (running: $(($running.Name | Sort-Object -Unique) -join ', '))." }

if ($Uninstall) {
    if (Test-Path $target) { Remove-Item -Recurse -Force $target; "Removed $target" } else { "Not installed ($target does not exist)." }
    return
}

if (-not $LedNextExe) { $LedNextExe = Join-Path $repo "src\LedNext.App\bin\Debug\net8.0-windows\LED-Next.exe" }
if (-not (Test-Path $LedNextExe)) { Write-Warning "LED-Next.exe not found at $LedNextExe (build the app first, or pass -LedNextExe). Installing anyway." }

"Building the plugin against $LaunchBox\Core\Unbroken.LaunchBox.Plugins.dll ..."
$project = Join-Path $repo "src\LedNext.LaunchBox\LedNext.LaunchBox.csproj"
& dotnet build $project -c Release -nologo -v q "-p:LaunchBoxDir=$LaunchBox"
if ($LASTEXITCODE -ne 0) { throw "Build failed. If the errors are about LaunchBox types, the real plugin API differs from tools\LaunchBoxPluginStubs: fix the plugin (and the stand-in) to match." }

$out = Join-Path $repo "src\LedNext.LaunchBox\bin\Release\net8.0-windows"
$info = Get-Content (Join-Path $out "LedNext.LaunchBox.buildinfo") -ErrorAction SilentlyContinue
if ($info -ne "api=true") { throw "The build used the compile-only stand-in, not LaunchBox's API ($info). Not installing." }
if (Test-Path (Join-Path $out "Unbroken.LaunchBox.Plugins.dll")) { throw "Unbroken.LaunchBox.Plugins.dll ended up in the output; it must come from LaunchBox. Not installing." }

New-Item -ItemType Directory -Force $target | Out-Null
foreach ($f in "LedNext.LaunchBox.dll", "LedNext.LaunchBox.pdb", "LedNext.FrontEnd.dll", "LedNext.FrontEnd.pdb") {
    Copy-Item (Join-Path $out $f) $target -Force
}
$settings = [ordered]@{
    ledNextExe     = (Resolve-Path -LiteralPath $LedNextExe -ErrorAction SilentlyContinue).Path
    startLedNext   = -not $NoAutoStart
    watchSelection = $true
    watchIntervalMs = 150
}
if (-not $settings.ledNextExe) { $settings.ledNextExe = $LedNextExe }
$json = $settings | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $target "LedNext.LaunchBox.json"), $json, (New-Object Text.UTF8Encoding $false))

"Installed to $target"
Get-ChildItem $target | ForEach-Object { "  $($_.Name)" }
"Settings: $json"
"Start LaunchBox or BigBox; the plugin logs to $target\LedNext.LaunchBox.log"
