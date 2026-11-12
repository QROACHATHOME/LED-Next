<#
.SYNOPSIS
  On a PC with LaunchBox: builds the LED-Next LaunchBox/BigBox plugin against LaunchBox's own
  Unbroken.LaunchBox.Plugins.dll and zips it for a GitHub release: dist\LED-Next-LaunchBox-plugin-<version>.zip.

.DESCRIPTION
  GitHub can't build the plugin (LaunchBox's DLL isn't ours to put there), so it is built here and attached to
  the release by hand ("Edit release" > drag the zip in). The zip holds one folder, "LaunchBox plugin\";
  unzipped into an unzipped LED-Next release, "Install LED-Next.cmd" installs it with the app.
  Builds only; writes nothing to LaunchBox. Build the commit the release was tagged from.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\build-plugin-release.ps1
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\build-plugin-release.ps1 -LaunchBox E:\LaunchBox -Version v0.2.0
#>
param(
    [string]$LaunchBox = "D:\LaunchBox",
    [string]$Version = ""
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $LaunchBox "Core\Unbroken.LaunchBox.Plugins.dll"))) {
    throw "No LaunchBox at $LaunchBox (Core\Unbroken.LaunchBox.Plugins.dll not found). Pass -LaunchBox <folder>."
}
if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $repo 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
$Version = $Version.TrimStart('v')

$project = Join-Path $repo "src\LedNext.LaunchBox\LedNext.LaunchBox.csproj"
& dotnet build $project -c Release -nologo -v q "-p:LaunchBoxDir=$LaunchBox" "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
$out = Join-Path $repo "src\LedNext.LaunchBox\bin\Release\net10.0-windows"
if ((Get-Content (Join-Path $out "LedNext.LaunchBox.buildinfo") -ErrorAction SilentlyContinue) -ne "api=true") { throw "Not built against LaunchBox's API." }
if (Test-Path (Join-Path $out "Unbroken.LaunchBox.Plugins.dll")) { throw "Unbroken.LaunchBox.Plugins.dll is in the output; it must not be shipped." }

$stage = Join-Path $repo "out\plugin\LaunchBox plugin"
Remove-Item (Split-Path $stage) -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage | Out-Null
foreach ($f in "LedNext.LaunchBox.dll", "LedNext.LaunchBox.pdb", "LedNext.FrontEnd.dll", "LedNext.FrontEnd.pdb", "LedNext.LaunchBox.buildinfo") {
    Copy-Item (Join-Path $out $f) $stage
}
$lbVersion = (Get-Item (Join-Path $LaunchBox "Core\Unbroken.LaunchBox.Plugins.dll")).VersionInfo.FileVersion
[IO.File]::WriteAllText((Join-Path $stage "BUILT-WITH.txt"), "LED-Next $Version plugin, built against LaunchBox $lbVersion`r`n", (New-Object Text.UTF8Encoding $false))

$dist = Join-Path $repo "dist"
New-Item -ItemType Directory -Force $dist | Out-Null
$zip = Join-Path $dist "LED-Next-LaunchBox-plugin-$Version.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path $stage -DestinationPath $zip
"Built $zip (against LaunchBox $lbVersion):"
Get-ChildItem $stage | ForEach-Object { "  LaunchBox plugin\$($_.Name)" }
