#!/usr/bin/env bash
# Packs this checkout as the m2w .NET tool and installs it for the current user, replacing any installed m2w.
# Run again after changing the code. Remove with: dotnet tool uninstall -g MassingToWind.Tool
set -euo pipefail
cd "$(dirname "$0")/.."

# A new version each time, so NuGet never reuses a cached older build.
suffix="local.$(date -u +%Y%m%d%H%M%S)"
rm -rf out/pkg
dotnet pack src/M2W.Cli -c Release -nologo -v q -o out/pkg --version-suffix "$suffix"
pkg=$(basename out/pkg/MassingToWind.Tool.*.nupkg .nupkg)
version=${pkg#MassingToWind.Tool.}

if dotnet tool list -g | grep -qi '^massingtowind\.tool '; then dotnet tool uninstall -g MassingToWind.Tool; fi
dotnet tool install -g MassingToWind.Tool --version "$version" --add-source out/pkg
echo "Try: m2w --help   (if m2w is not found, open a new terminal)"
