#!/usr/bin/env bash
# Портативная сборка для Windows x64 (работает в Linux и Windows/Git Bash):
#   build/publish.sh [версия]
# Результат в artifacts/: Opti.exe (интерфейс), opti-cli.exe (командная строка), SHA256SUMS.txt.
# Если задана переменная DEV=1 — дополнительно Opti.Dev.exe: та же программа, но рядом с exe ведёт dev.zip с диагностикой.
# Каждый файл самодостаточный: .NET устанавливать не нужно, установка не нужна.
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION="${1:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)}"
DOTNET="${DOTNET:-dotnet}"
rm -rf artifacts
mkdir -p artifacts

"$DOTNET" publish src/Opti.App -c Release -r win-x64 -p:Version="$VERSION" -p:DebugType=none -p:DebugSymbols=false -o artifacts/app --nologo -v quiet
"$DOTNET" publish src/Opti.Cli -c Release -r win-x64 -p:Version="$VERSION" -p:DebugType=none -p:DebugSymbols=false -o artifacts/cli --nologo -v quiet
mv artifacts/app/Opti.exe artifacts/Opti.exe
if [ "${DEV:-0}" = "1" ]; then
  "$DOTNET" publish src/Opti.App -c Release -r win-x64 -p:Version="$VERSION" -p:DebugType=none -p:DebugSymbols=false -p:DevBuild=true -p:IntermediateOutputPath=obj/dev/ -o artifacts/dev --nologo -v quiet
  mv artifacts/dev/Opti.exe artifacts/Opti.Dev.exe
  rm -rf artifacts/dev
fi
mv artifacts/cli/opti-cli.exe artifacts/opti-cli.exe
rm -rf artifacts/app artifacts/cli

(cd artifacts && sha256sum *.exe > SHA256SUMS.txt)
ls -la artifacts
