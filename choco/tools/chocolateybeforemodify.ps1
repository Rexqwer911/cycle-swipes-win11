# Runs before upgrade and uninstall: a running instance locks its files.
Get-Process -Name 'CycleSwipes' -ErrorAction SilentlyContinue | Stop-Process -Force
