#!/usr/bin/env bash
# Packs the built PortForge Engine (already installed by cmake --install) into
# engine/package, which PortForge ships as its Engine folder.
set -euo pipefail
root="$(cd "$(dirname "$0")" && pwd)"
pkg="${1:-$root/package}"
pkg="$(realpath -m "$pkg")"
if [[ "$pkg" != "$root/package" && "$pkg" != "$root"/package-* ]]; then
  echo "Package output must be engine/package or engine/package-*" >&2
  exit 1
fi
# The engine is already installed to engine/package by cmake --install in build-engine.sh
# We just need to add the ENGINE.txt and licenses
base=$(cat "$root/BASE" 2>/dev/null || git -C "$root/src" log -1 --format=%h)
version=$(cat "$root/VERSION" 2>/dev/null || echo 1.0)
shaders=$("$pkg/scarletnx.exe" --shader-abi | tr -d '\r\n')
test -n "$shaders"
printf 'version=%s\nbase=%s\nshaders=%s\nfeatures=%s\n' "$version" "$base" "$shaders" "$(cat "$root/FEATURES" 2>/dev/null || echo 'Smart async shaders')" > "$pkg/ENGINE.txt"
mkdir -p "$pkg/licenses"
cp "$root/src/LICENSE" "$pkg/licenses/ScarletNX-LICENSE.txt"
cp "$root/src/src/nx/gpu/vk/fsr1/LICENSE.txt" "$pkg/licenses/AMD-FidelityFX-FSR1-LICENSE.txt"
cp "$root/src/src/nx/gpu/vk/fsr1/UPSTREAM.txt" "$pkg/licenses/AMD-FidelityFX-FSR1-UPSTREAM.txt"
cp "$root/src/src/nx/gpu/vk/ffx/LICENSE.txt" "$pkg/licenses/AMD-FidelityFX-SDK-1.1.4-LICENSE.txt"
cp "$root/src/src/nx/gpu/vk/ffx/UPSTREAM.txt" "$pkg/licenses/AMD-FidelityFX-SDK-1.1.4-UPSTREAM.txt"
echo "PACKAGE_OK $pkg"; ls "$pkg"