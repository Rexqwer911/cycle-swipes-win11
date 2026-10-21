# Short commands for building CycleSwipes. Recipes run in Windows PowerShell, so no Unix tools are needed.
SHELL := powershell.exe
.SHELLFLAGS := -NoProfile -ExecutionPolicy Bypass -Command

PROJECT := CycleSwipes.csproj
BIN     := bin/Release/net9.0-windows
PUBLISH := publish

.PHONY: build run debug release choco clean

## build: Release build into $(BIN) (needs the .NET 9 SDK)
build:
	dotnet build $(PROJECT) -c Release -nologo

## run: build and start the app
run: build
	Start-Process $(BIN)/CycleSwipes.exe

## debug: build and start the app with --debug (developer menu, detailed log)
debug: build
	Start-Process $(BIN)/CycleSwipes.exe -ArgumentList "--debug"

## release: self-contained single-file build, zipped into dist/CycleSwipes-<version>-win-x64.zip
release:
	if (Test-Path $(PUBLISH)) { Remove-Item $(PUBLISH) -Recurse -Force }
	dotnet publish $(PROJECT) -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o $(PUBLISH) -nologo
	Copy-Item LICENSE, README.md, lib/VirtualDesktopAccessor-LICENSE.txt $(PUBLISH)
	$$v = ([xml](Get-Content $(PROJECT))).Project.PropertyGroup.Version; New-Item -ItemType Directory -Force dist | Out-Null; $$zip = "dist/CycleSwipes-$$v-win-x64.zip"; Compress-Archive $(PUBLISH)/* $$zip -Force; "Created $$zip"

## choco: Chocolatey package for the already published GitHub release of the current version, into dist/
choco:
	$$v = ([xml](Get-Content $(PROJECT))).Project.PropertyGroup.Version; $$rel = Invoke-RestMethod "https://api.github.com/repos/Rexqwer911/cycle-swipes-win11/releases/tags/v$$v" -Headers @{ 'User-Agent' = 'make' }; $$asset = $$rel.assets | Where-Object name -eq "CycleSwipes-$$v-win-x64.zip"; if (-not $$asset) { throw "Release v$$v has no CycleSwipes-$$v-win-x64.zip" }; $$sum = $$asset.digest -replace '^sha256:', ''; if (Test-Path choco-build) { Remove-Item choco-build -Recurse -Force }; Copy-Item choco choco-build -Recurse; Remove-Item choco-build/icon.png; Get-ChildItem choco-build -Recurse -File | ForEach-Object { (Get-Content $$_.FullName -Raw).Replace('{{VERSION}}', $$v).Replace('{{CHECKSUM}}', $$sum) | Set-Content $$_.FullName -NoNewline }; New-Item -ItemType Directory -Force dist | Out-Null; choco pack choco-build/cycleswipes.nuspec --outputdirectory dist

## clean: remove build output
clean:
	foreach ($$d in "bin", "obj", "$(PUBLISH)", "dist", "choco-build") { if (Test-Path $$d) { Remove-Item $$d -Recurse -Force } }
