#!/usr/bin/env bash
# Сборка релиза для Windows x64 (работает в Linux и Windows/Git Bash):
#   build/publish.sh [версия]
# Результат в artifacts/: портативный zip, установщик (если есть makensis) и SHA256SUMS.txt
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION="${1:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)}"
NAME="Opti-$VERSION-win-x64"
OUT="artifacts/$NAME"
DOTNET="${DOTNET:-dotnet}"

rm -rf artifacts
mkdir -p "$OUT"

# Все три программы публикуются в одну папку и делят один самодостаточный рантайм .NET 8.
for proj in src/Opti.App src/Opti.Cli src/Opti.Bench; do
  "$DOTNET" publish "$proj" -c Release -r win-x64 --self-contained true \
    -p:PublishSingleFile=false -p:Version="$VERSION" -p:DebugType=none -p:DebugSymbols=false \
    -o "$OUT" --nologo -v quiet
done

cp README.md "$OUT/README.md"
cp CHANGELOG.md "$OUT/CHANGELOG.md"
cp THIRD-PARTY-NOTICES.md "$OUT/THIRD-PARTY-NOTICES.md"
[ -f tools/PresentMon.exe ] && cp tools/PresentMon*.exe "$OUT/" || true

(cd artifacts && zip -qr "$NAME.zip" "$NAME")

if command -v makensis >/dev/null 2>&1; then
  makensis -V2 -DVERSION="$VERSION" -DNUMVER="${VERSION%%-*}" -DSRCDIR="$PWD/$OUT" -DOUTFILE="$PWD/artifacts/Opti-$VERSION-Setup.exe" \
    -DICON="$PWD/src/Opti.App/Assets/opti.ico" installer/opti.nsi
else
  echo "makensis не найден — установщик не собран (только zip)." >&2
fi

(cd artifacts && sha256sum *.zip *.exe 2>/dev/null > SHA256SUMS.txt || true)
ls -la artifacts
