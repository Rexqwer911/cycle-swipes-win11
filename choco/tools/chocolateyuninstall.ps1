$ErrorActionPreference = 'Stop'

$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

$shortcut = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'CycleSwipes.lnk'
if (Test-Path $shortcut) { Remove-Item $shortcut -Force }

# The "Start with Windows" entry of the user running the uninstall, if it points at this package.
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$command = (Get-ItemProperty -Path $runKey -Name 'CycleSwipes' -ErrorAction SilentlyContinue).CycleSwipes
if ($command -and $command -like "*$toolsDir*") {
  Remove-ItemProperty -Path $runKey -Name 'CycleSwipes'
}
