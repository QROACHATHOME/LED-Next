<#
.SYNOPSIS
  Finds the LaunchBox install folder (dot-sourced by install.ps1, install-launchbox-plugin.ps1 and
  build-plugin-release.ps1). Read-only: it only looks.

.DESCRIPTION
  A LaunchBox folder is one with Core\Unbroken.LaunchBox.Plugins.dll. Tried in order:
  1. LaunchBox / BigBox shortcuts on the Desktop and in the Start menu (their target's folder);
  2. "LaunchBox" at the root of every fixed drive, then one folder down (E:\Games\LaunchBox);
  3. "LaunchBox" in the user's profile and Documents.
  Returns the first folder found, or $null.
#>
function Test-LaunchBoxFolder([string]$Folder) {
    $Folder -and (Test-Path -LiteralPath (Join-Path $Folder "Core\Unbroken.LaunchBox.Plugins.dll"))
}

function Find-LaunchBox {
    # 1. Shortcuts point at LaunchBox.exe / BigBox.exe in the LaunchBox folder (or its Core folder).
    $shortcutFolders = @(
        [Environment]::GetFolderPath("Desktop"), [Environment]::GetFolderPath("CommonDesktopDirectory"),
        [Environment]::GetFolderPath("Programs"), [Environment]::GetFolderPath("CommonPrograms")
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
    try {
        $shell = New-Object -ComObject WScript.Shell
        foreach ($dir in $shortcutFolders) {
            foreach ($lnk in Get-ChildItem -LiteralPath $dir -Filter *.lnk -Recurse -Depth 2 -ErrorAction SilentlyContinue) {
                $target = $shell.CreateShortcut($lnk.FullName).TargetPath
                if ($target -notmatch '(?i)\\(LaunchBox|BigBox)\.exe$') { continue }
                $folder = Split-Path -Parent $target
                if (Test-LaunchBoxFolder $folder) { return $folder }
                $parent = Split-Path -Parent $folder
                if (Test-LaunchBoxFolder $parent) { return $parent }
            }
        }
    } catch { }

    # 2. X:\LaunchBox, then X:\<any>\LaunchBox, on every fixed drive.
    $drives = [IO.DriveInfo]::GetDrives() | Where-Object { $_.DriveType -eq 'Fixed' -and $_.IsReady } | ForEach-Object { $_.RootDirectory.FullName }
    foreach ($root in $drives) {
        $folder = Join-Path $root "LaunchBox"
        if (Test-LaunchBoxFolder $folder) { return $folder }
    }
    foreach ($root in $drives) {
        foreach ($dir in Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue) {
            $folder = Join-Path $dir.FullName "LaunchBox"
            if (Test-LaunchBoxFolder $folder) { return $folder }
        }
    }

    # 3. The user's own folders.
    foreach ($base in @($env:USERPROFILE, [Environment]::GetFolderPath("MyDocuments"))) {
        if (-not $base) { continue }
        $folder = Join-Path $base "LaunchBox"
        if (Test-LaunchBoxFolder $folder) { return $folder }
    }
    return $null
}
