<#
.SYNOPSIS
  Builds LED-Next and installs it to its own folder (default D:\LED-Next), plus the LaunchBox/BigBox plugin.

.DESCRIPTION
  The installed folder is kept small:
      LED-Next.exe      the whole app in one file (Release, win-x64, debug info embedded)
      PacDrive64.dll    Ultimarc's SDK DLL (copied from your current setup; never shipped by LED-Next)
      config\           cabinet.json, games.db, window.json (your settings; never overwritten)
      logs\             led-next.log
  Running it again updates LED-Next.exe and keeps config\. Leftovers of an older multi-file install in the
  same folder (LED-Next.dll, *.pdb, runtimes\ ...) are removed.

  First install: if the install folder has no config yet, config\ is COPIED from the build folder the
  cabinet has been running (src\LedNext.App\bin\Debug\net8.0-windows), or -From. That folder is left as is.

  Also: a Start menu shortcut, and tools\install-launchbox-plugin.ps1 pointed at the installed exe
  (skipped with -NoPlugin, or when no LaunchBox is found). LED-Next, LaunchBox and BigBox must be closed.

  From a release zip (LED-Next.exe next to this tools\ folder) nothing is built: the release's exe, and its
  "LaunchBox plugin" folder if you unzipped the plugin zip into it, are installed. From the source code it
  needs the .NET 8 SDK. The installed exe needs the .NET 8 Desktop Runtime, unless it is the self-contained
  build (-SelfContained, or the release's "-self-contained" zip).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\install.ps1
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\install.ps1 -InstallDir E:\LED-Next -LaunchBox E:\LaunchBox
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\install.ps1 -Uninstall
#>
param(
    [string]$InstallDir = "",
    [string]$LaunchBox = "D:\LaunchBox",
    # Where to copy config\ and PacDrive64.dll from on a first install. Default: this repo's Debug build.
    [string]$From = "",
    [switch]$SelfContained,
    [switch]$NoPlugin,
    [switch]$NoShortcut,
    # With -Uninstall: also delete config\ and logs\ (your settings and game library).
    [switch]$RemoveSettings,
    [switch]$Uninstall
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if (-not $InstallDir) { $InstallDir = if (Test-Path "D:\") { "D:\LED-Next" } else { "C:\LED-Next" } }
if (-not $From) { $From = Join-Path $repo "src\LedNext.App\bin\Debug\net8.0-windows" }
# Run from an unzipped release (LED-Next.exe next to tools\, no source code) or from the source code.
$fromRelease = (Test-Path (Join-Path $repo "LED-Next.exe")) -and -not (Test-Path (Join-Path $repo "src"))
$releaseVersion = (Get-Content (Join-Path $repo "VERSION.txt") -ErrorAction SilentlyContinue | Select-Object -First 1)
$exe = Join-Path $InstallDir "LED-Next.exe"
$shortcut = Join-Path ([Environment]::GetFolderPath("Programs")) "LED-Next.lnk"
$pluginScript = Join-Path $PSScriptRoot "install-launchbox-plugin.ps1"
$hasLaunchBox = Test-Path (Join-Path $LaunchBox "Core\Unbroken.LaunchBox.Plugins.dll")

$running = Get-Process -Name LED-Next -ErrorAction SilentlyContinue
if ($running) { throw "Close LED-Next first (right-click its tray icon > Exit)." }
if (-not $NoPlugin -and $hasLaunchBox) {
    $lb = Get-Process -Name LaunchBox, BigBox -ErrorAction SilentlyContinue
    if ($lb) { throw "Close LaunchBox and BigBox first (running: $(($lb.Name | Sort-Object -Unique) -join ', ')), or pass -NoPlugin." }
}

# Files an older, multi-file LED-Next install leaves behind. PacDrive*.dll is kept.
function Remove-OldBuildFiles([string]$dir) {
    $old = @(Get-ChildItem $dir -File -ErrorAction SilentlyContinue | Where-Object {
        ($_.Extension -in ".dll", ".pdb" -and $_.Name -notlike "PacDrive*.dll") -or
        $_.Name -like "LED-Next.deps.json" -or $_.Name -like "LED-Next.runtimeconfig.json"
    })
    foreach ($f in $old) { Remove-Item -LiteralPath $f.FullName -Force; "  removed old $($f.Name)" }
    $rt = Join-Path $dir "runtimes"
    if (Test-Path $rt) { Remove-Item -Recurse -Force $rt; "  removed old runtimes\" }
}

if ($Uninstall) {
    if (-not $NoPlugin -and $hasLaunchBox) { & $pluginScript -LaunchBox $LaunchBox -Uninstall }
    if (Test-Path $shortcut) { Remove-Item -Force $shortcut; "Removed $shortcut" }
    if (Test-Path $exe) {
        Remove-Item -Force $exe; "Removed $exe"
        Remove-OldBuildFiles $InstallDir
        Get-ChildItem $InstallDir -Filter "PacDrive*.dll" -File | ForEach-Object { Remove-Item -Force $_.FullName; "Removed $($_.Name)" }
    } else { "LED-Next is not installed in $InstallDir." }
    if ($RemoveSettings) {
        foreach ($d in "config", "logs") {
            $p = Join-Path $InstallDir $d
            if (Test-Path $p) { Remove-Item -Recurse -Force $p; "Removed $p" }
        }
    } elseif (Test-Path (Join-Path $InstallDir "config")) {
        "Kept your settings in $InstallDir\config (add -RemoveSettings to delete them)."
    }
    if ((Test-Path $InstallDir) -and -not (Get-ChildItem $InstallDir -Force)) { Remove-Item $InstallDir; "Removed empty $InstallDir" }
    return
}

# 1. A release zip already has the exe next to this script's folder; from the source code, build it into
#    the repo's publish\ folder (git-ignored).
if ($fromRelease) {
    $stage = $repo
    "Installing LED-Next $(if ($releaseVersion) { "$releaseVersion " })from this release folder."
    if ($SelfContained) { "(-SelfContained only applies when building from the source code; this release's exe is used as it is.)" }
} else {
    $stage = Join-Path $repo "publish\LED-Next"
    if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
    $sc = if ($SelfContained) { "true" } else { "false" }
    "Building LED-Next (Release, win-x64, single file$(if ($SelfContained) { ', self-contained' }))..."
    & dotnet publish (Join-Path $repo "src\LedNext.App\LedNext.App.csproj") -c Release -r win-x64 --self-contained $sc `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=$sc `
        -p:DebugType=embedded -p:SatelliteResourceLanguages=en -o $stage -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
    $built = Get-ChildItem $stage -File
    if ($built.Count -ne 1 -or $built[0].Name -ne "LED-Next.exe") { throw "Expected only LED-Next.exe in $stage, got: $($built.Name -join ', ')" }
}
if ((Resolve-Path $InstallDir -ErrorAction SilentlyContinue).Path -eq (Resolve-Path $stage).Path) { throw "Unzip the release somewhere else than the install folder ($InstallDir)." }

# 2. Install / update the exe and clean the folder.
$firstInstall = -not (Test-Path (Join-Path $InstallDir "config\cabinet.json"))
New-Item -ItemType Directory -Force $InstallDir | Out-Null
Remove-OldBuildFiles $InstallDir
Copy-Item (Join-Path $stage "LED-Next.exe") $exe -Force
"Installed $exe ($([math]::Round((Get-Item $exe).Length / 1MB, 1)) MB)"

# 3. PacDrive64.dll (Ultimarc's; LED-Next never ships it).
$pac = Join-Path $InstallDir "PacDrive64.dll"
if (-not (Test-Path $pac)) {
    $src = @((Join-Path $From "PacDrive64.dll"), (Join-Path $repo "src\LedNext.App\native\PacDrive64.dll")) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($src) { Copy-Item $src $pac; "Copied PacDrive64.dll from $src" }
    else { Write-Warning "No PacDrive64.dll found. Hardware mode needs it in $InstallDir (Simulator mode works without it)." }
}

# 4. Settings: copied once from the old build folder, never overwritten.
if ($firstInstall) {
    $oldConfig = Join-Path $From "config"
    if (Test-Path (Join-Path $oldConfig "cabinet.json")) {
        Copy-Item $oldConfig $InstallDir -Recurse -Force
        "Copied your settings and game library from $oldConfig (the original is left in place)."
        $cfg = Get-Content (Join-Path $InstallDir "config\cabinet.json") -Raw
        if ($cfg -match '"pacDriveDllPath"\s*:\s*"([^"]+)"') { Write-Warning "cabinet.json sets pacDriveDllPath = $($Matches[1]); clear it to use $pac." }
    } else { "No earlier settings found; LED-Next will create a default panel on first start." }
} else { "Kept your settings in $InstallDir\config." }

# 5. Start menu shortcut.
if (-not $NoShortcut) {
    $ws = New-Object -ComObject WScript.Shell
    $lnk = $ws.CreateShortcut($shortcut)
    $lnk.TargetPath = $exe
    $lnk.WorkingDirectory = $InstallDir
    $lnk.Description = "LED-Next arcade lighting"
    $lnk.Save()
    "Start menu shortcut: $shortcut"
}

# 6. LaunchBox/BigBox plugin, pointed at the installed exe.
if ($NoPlugin) { "Plugin skipped (-NoPlugin)." }
elseif (-not $hasLaunchBox) { "No LaunchBox at $LaunchBox; plugin skipped (pass -LaunchBox <folder> to install it)." }
elseif ($fromRelease) {
    $prebuilt = Join-Path $repo "LaunchBox plugin"
    if (Test-Path (Join-Path $prebuilt "LedNext.LaunchBox.dll")) { & $pluginScript -LaunchBox $LaunchBox -LedNextExe $exe -Prebuilt $prebuilt }
    else { Write-Warning "This release folder has no 'LaunchBox plugin' folder. Download LED-Next-LaunchBox-plugin-*.zip from the same release, unzip it into this folder and run the installer again." }
}
else { & $pluginScript -LaunchBox $LaunchBox -LedNextExe $exe }

""
"Done. Start LED-Next from the Start menu, or let LaunchBox/BigBox start it."
