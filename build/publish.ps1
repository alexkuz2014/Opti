# Сборка релиза в Windows: powershell -ExecutionPolicy Bypass -File build\publish.ps1 [-Version 1.0.0]
param([string]$Version)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
if (-not $Version) { $Version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version }
$name = "Opti-$Version-win-x64"
$out = "artifacts\$name"
Remove-Item artifacts -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $out | Out-Null
foreach ($p in 'src\Opti.App', 'src\Opti.Cli', 'src\Opti.Bench') {
  dotnet publish $p -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:Version=$Version -p:DebugType=none -p:DebugSymbols=false -o $out --nologo -v quiet
  if ($LASTEXITCODE) { throw "publish $p failed" }
}
Copy-Item README.md, CHANGELOG.md, THIRD-PARTY-NOTICES.md $out
if (Test-Path tools\PresentMon*.exe) { Copy-Item tools\PresentMon*.exe $out }
Compress-Archive -Path $out -DestinationPath "artifacts\$name.zip"
$nsis = Get-Command makensis -ErrorAction SilentlyContinue
if (-not $nsis -and (Test-Path "${env:ProgramFiles(x86)}\NSIS\makensis.exe")) { $nsis = "${env:ProgramFiles(x86)}\NSIS\makensis.exe" }
if ($nsis) {
  & $nsis -V2 "-DVERSION=$Version" "-DNUMVER=$($Version.Split('-')[0])" "-DSRCDIR=$PWD\$out" "-DOUTFILE=$PWD\artifacts\Opti-$Version-Setup.exe" "-DICON=$PWD\src\Opti.App\Assets\opti.ico" installer\opti.nsi
  if ($LASTEXITCODE) { throw 'makensis failed' }
}
Get-ChildItem artifacts\*.zip, artifacts\*.exe | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLower())  $(Split-Path $_.Path -Leaf)" } | Set-Content artifacts\SHA256SUMS.txt
Get-ChildItem artifacts
