#!/usr/bin/env bash
# Builds the release assets into artifacts/release/: one zip per platform, each holding the
# self-contained single-file executable, the scorefixes folder, LICENSE and THIRD-PARTY-NOTICES.md, plus SHA256SUMS.txt.
# Cross-publishing works from any OS, so this runs on Linux, macOS or Windows (Git Bash).
#   ./scripts/publish-release.sh [--skip-tests] [runtime ...]     (default: win-x64 linux-x64 osx-arm64)
set -euo pipefail

cd "$(dirname "$0")/.."
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -n 1)
[ -n "$version" ] || { echo "Could not read Version from Directory.Build.props" >&2; exit 1; }

skip_tests=0
runtimes=()
for arg in "$@"; do
  if [ "$arg" = "--skip-tests" ]; then skip_tests=1; else runtimes+=("$arg"); fi
done
[ ${#runtimes[@]} -gt 0 ] || runtimes=(win-x64 linux-x64 osx-arm64)

if [ $skip_tests -eq 0 ]; then
  dotnet restore Degausser.sln
  dotnet build Degausser.sln -c Release --no-restore
  dotnet test tests/Degausser.Core.Tests/Degausser.Core.Tests.csproj -c Release --no-build
fi

root=artifacts/release
rm -rf "$root"
mkdir -p "$root"

for rid in "${runtimes[@]}"; do
  publish="$root/publish-$rid"
  bundle="$root/bundle-$rid"
  mkdir -p "$publish" "$bundle"

  dotnet publish src/Degausser.Avalonia/Degausser.Avalonia.csproj \
    -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -p:PublishTrimmed=false \
    -p:DebugType=None -p:DebugSymbols=false \
    -o "$publish"

  exe=Degausser; [ "${rid%%-*}" = "win" ] && exe=Degausser.exe
  cp "$publish/$exe" "$bundle/$exe"
  chmod +x "$bundle/$exe"
  cp LICENSE THIRD-PARTY-NOTICES.md "$bundle/"
  # Only the scorefix files: the app reads every .txt in a "scorefixes" folder beside the executable.
  (cd scorefixes && find . -name '*.txt' -print0 | while IFS= read -r -d '' f; do
    mkdir -p "../$bundle/scorefixes/$(dirname "$f")"; cp "$f" "../$bundle/scorefixes/$f"
  done)

  # zip -X keeps the executable bit, so Linux and macOS users can run the file straight after unzipping.
  zip="$PWD/$root/Degausser-$version-$rid.zip"
  (cd "$bundle" && zip -q -r -X "$zip" .)
  rm -rf "$publish" "$bundle"
done

# sha256sum is GNU coreutils; macOS ships shasum instead.
if command -v sha256sum >/dev/null 2>&1; then sum_cmd=(sha256sum); else sum_cmd=(shasum -a 256); fi
(cd "$root" && "${sum_cmd[@]}" ./*.zip | sed 's: \./: :' > SHA256SUMS.txt)
echo "Release assets in $root:"
ls -l "$root"
