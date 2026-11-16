<#
.SYNOPSIS
  Builds the LED-Next release downloads into dist\:
    LED-Next-<version>-win-x64.zip                 LED-Next.exe (3 MB, needs the .NET 8 Desktop Runtime)
    LED-Next-<version>-win-x64-self-contained.zip  LED-Next.exe (about 65 MB, no .NET needed)
  Each zip holds LED-Next.exe, LedNextCmd.exe and front-ends\ (the other front ends' helpers, see
  docs\FRONT_ENDS.md), "Install LED-Next.cmd", tools\install.ps1, tools\install-launchbox-plugin.ps1,
  README.md and VERSION.txt. Unzip, close LED-Next/LaunchBox/BigBox, double-click "Install LED-Next.cmd".

  The LaunchBox plugin is NOT built here: it must be built against LaunchBox's own DLL, on a PC with LaunchBox
  (tools\build-plugin-release.ps1 makes LED-Next-LaunchBox-plugin-<version>.zip, attached to the same release).

.EXAMPLE
  .\build\Build-Release.ps1                  # version from Directory.Build.props
  .\build\Build-Release.ps1 -Version v0.2.0
#>
param([string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
$Version = $Version.TrimStart('v')   # accepts a tag name like v0.2.0
Write-Host "LED-Next $Version"

$out  = Join-Path $root 'out'
$dist = Join-Path $root 'dist'
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $out, $dist | Out-Null

# The front-end helpers (.NET Framework 4.x: part of Windows, so the same files go in both zips):
# LedNextCmd.exe (the LEDBlinky-style command tool), front-ends\PinballX\ and front-ends\Playnite\.
$helpers = Join-Path $out 'helpers'
foreach ($p in 'LedNext.Cmd', 'LedNext.PinballX', 'LedNext.Playnite') {
    dotnet build "src/$p/$p.csproj" -c Release "-p:Version=$Version" -o (Join-Path $helpers $p) -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet build $p failed ($LASTEXITCODE)" }
}
function Copy-FrontEndHelpers([string]$dir) {
    Copy-Item (Join-Path $helpers 'LedNext.Cmd\LedNextCmd.exe') $dir
    $pbx = New-Item -ItemType Directory -Force (Join-Path $dir 'front-ends\PinballX')
    Copy-Item (Join-Path $helpers 'LedNext.PinballX\LED-Next PinballX Plugin.dll') $pbx
    $pn = New-Item -ItemType Directory -Force (Join-Path $dir 'front-ends\Playnite')
    Copy-Item (Join-Path $helpers 'LedNext.Playnite\LedNext.Playnite.dll'), (Join-Path $helpers 'LedNext.Playnite\icon.png') $pn
    # The extension's version follows the release.
    $yaml = (Get-Content (Join-Path $helpers 'LedNext.Playnite\extension.yaml') -Raw) -replace '(?m)^Version: .*$', "Version: $Version"
    [IO.File]::WriteAllText((Join-Path $pn 'extension.yaml'), $yaml, (New-Object Text.UTF8Encoding $false))
}

function Build-Zip([string]$name, [bool]$selfContained) {
    $dir = Join-Path $out $name
    $sc = if ($selfContained) { 'true' } else { 'false' }
    dotnet publish src/LedNext.App/LedNext.App.csproj -c Release -r win-x64 --self-contained $sc "-p:Version=$Version" `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true "-p:EnableCompressionInSingleFile=$sc" `
        -p:DebugType=embedded -p:SatelliteResourceLanguages=en -o $dir -nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
    $files = @(Get-ChildItem $dir -File)
    if ($files.Count -ne 1 -or $files[0].Name -ne 'LED-Next.exe') { throw "Expected only LED-Next.exe, got: $($files.Name -join ', ')" }

    Copy-FrontEndHelpers $dir
    New-Item -ItemType Directory -Force (Join-Path $dir 'tools') | Out-Null
    Copy-Item 'tools/install.ps1', 'tools/install-launchbox-plugin.ps1', 'tools/Find-LaunchBox.ps1' (Join-Path $dir 'tools')
    Copy-Item 'Install LED-Next.cmd', 'README.md' $dir
    [IO.File]::WriteAllText((Join-Path $dir 'VERSION.txt'), "$Version`r`n", (New-Object Text.UTF8Encoding $false))

    $zip = Join-Path $dist "$name.zip"
    Remove-Item $zip -ErrorAction SilentlyContinue
    Compress-Archive -Path "$dir\*" -DestinationPath $zip
    Write-Host ("{0}  ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
}

Build-Zip "LED-Next-$Version-win-x64" $false
Build-Zip "LED-Next-$Version-win-x64-self-contained" $true
