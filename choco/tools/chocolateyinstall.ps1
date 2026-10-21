$ErrorActionPreference = 'Stop'

$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

# {{VERSION}} and {{CHECKSUM}} are filled in by `make choco` from the GitHub release.
$packageArgs = @{
  packageName    = $env:ChocolateyPackageName
  unzipLocation  = $toolsDir
  url64bit       = 'https://github.com/Rexqwer911/cycle-swipes-win11/releases/download/v{{VERSION}}/CycleSwipes-{{VERSION}}-win-x64.zip'
  checksum64     = '{{CHECKSUM}}'
  checksumType64 = 'sha256'
}
Install-ChocolateyZipPackage @packageArgs

# GUI app: the shim must not wait for it to exit.
New-Item -ItemType File -Force -Path (Join-Path $toolsDir 'CycleSwipes.exe.gui') | Out-Null

$shortcut = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'CycleSwipes.lnk'
Install-ChocolateyShortcut -ShortcutFilePath $shortcut -TargetPath (Join-Path $toolsDir 'CycleSwipes.exe') `
  -WorkingDirectory $toolsDir -Description 'Wrap-around four-finger desktop swipes'

Write-Host 'CycleSwipes installed. Start it from the Start menu and turn on "Start with Windows" in the tray menu.'
