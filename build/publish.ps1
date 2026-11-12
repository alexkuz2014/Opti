# Портативная сборка в Windows: powershell -ExecutionPolicy Bypass -File build\publish.ps1 [-Version 1.1.0]
param([string]$Version)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
if (-not $Version) { $Version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version }
Remove-Item artifacts -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory artifacts | Out-Null
dotnet publish src\Opti.App -c Release -r win-x64 -p:Version=$Version -p:DebugType=none -p:DebugSymbols=false -o artifacts\app --nologo -v quiet
if ($LASTEXITCODE) { throw 'publish Opti.App failed' }
dotnet publish src\Opti.Cli -c Release -r win-x64 -p:Version=$Version -p:DebugType=none -p:DebugSymbols=false -o artifacts\cli --nologo -v quiet
if ($LASTEXITCODE) { throw 'publish Opti.Cli failed' }
Move-Item artifacts\app\Opti.exe artifacts\Opti.exe
if ($env:DEV -eq '1') {
    dotnet publish src\Opti.App -c Release -r win-x64 -p:Version=$Version -p:DebugType=none -p:DebugSymbols=false -p:DevBuild=true -p:IntermediateOutputPath=obj/dev/ -o artifacts\dev --nologo -v quiet
    if ($LASTEXITCODE) { throw 'publish Opti.App (dev) failed' }
    Move-Item artifacts\dev\Opti.exe artifacts\Opti.Dev.exe
    Remove-Item artifacts\dev -Recurse -Force
}
Move-Item artifacts\cli\opti-cli.exe artifacts\opti-cli.exe
Remove-Item artifacts\app, artifacts\cli -Recurse -Force
Get-ChildItem artifacts\*.exe | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLower())  $(Split-Path $_.Path -Leaf)" } | Set-Content artifacts\SHA256SUMS.txt
Get-ChildItem artifacts
