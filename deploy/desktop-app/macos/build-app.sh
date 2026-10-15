#!/usr/bin/env bash
# Assembles PersonalFinance.app and a .dmg from a published launcher (ADR-0010). Run on macOS by
# .github/workflows/desktop.yml:  build-app.sh <published dir> <web build> <pgsql dir> <version> <arch> <out dir>
# The app is signed ad hoc (no Apple Developer ID yet): the first open needs right-click > Open, see docs/desktop.md.
set -euo pipefail

published=$1 web=$2 pgsql=$3 version=$4 arch=$5 out=$6
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../../.." && pwd)
app="$out/Personal Finance.app"

rm -rf "$app" && mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$published/." "$app/Contents/MacOS/"
cp -R "$web" "$app/Contents/Resources/web"
cp -R "$pgsql" "$app/Contents/Resources/pgsql"
cp "$repo/web/public/icon-512.png" "$app/Contents/Resources/personal-finance.png"
sed "s/__VERSION__/$version/g" "$here/Info.plist" > "$app/Contents/Info.plist"

# Icon set from the web app's 512 px icon.
iconset=$(mktemp -d)/personal-finance.iconset
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
  sips -z $size $size "$repo/web/public/icon-512.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  [ $double -le 512 ] && sips -z $double $double "$repo/web/public/icon-512.png" \
    --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/personal-finance.icns"

xattr -cr "$app"
codesign --force --deep --sign - "$app"

staging=$(mktemp -d)
cp -R "$app" "$staging/"
ln -s /Applications "$staging/Applications"
hdiutil create -volname "Personal Finance" -srcfolder "$staging" -ov -format UDZO \
  "$out/PersonalFinance-$version-macos-$arch.dmg"
