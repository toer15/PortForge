#!/usr/bin/env bash
# Packs the built PortForge Engine with the MSYS2 DLLs it imports (followed recursively, as ScarletNX's CI does) into
# engine/package, which PortForge ships as its Engine folder.
set -euo pipefail
root="$(cd "$(dirname "$0")" && pwd)"
pkg="${1:-$root/package}"
pkg="$(realpath -m "$pkg")"
if [[ "$pkg" != "$root/package" && "$pkg" != "$root"/package-* ]]; then
  echo "Package output must be engine/package or engine/package-*" >&2
  exit 1
fi
rm -rf "$pkg"; mkdir -p "$pkg"
cp "$root/src/build/default/scarletnx.exe" "$pkg/"
bins=("$pkg/scarletnx.exe")
for ((i = 0; i < ${#bins[@]}; ++i)); do
  while IFS= read -r dll; do
    src="$MINGW_PREFIX/bin/$dll"; dst="$pkg/$dll"
    if [[ -f "$src" && ! -f "$dst" ]]; then cp "$src" "$dst"; bins+=("$dst"); fi
  done < <(llvm-objdump -p "${bins[i]}" | awk '/DLL Name:/ { sub(/\r$/, "", $3); print $3 }')
done
base=$(cat "$root/BASE" 2>/dev/null || git -C "$root/src" log -1 --format=%h)
version=$(cat "$root/VERSION" 2>/dev/null || echo 1.0)
printf 'version=%s\nbase=%s\nfeatures=%s\n' "$version" "$base" "$(cat "$root/FEATURES" 2>/dev/null || echo 'Smart async shaders')" > "$pkg/ENGINE.txt"
mkdir -p "$pkg/licenses"; cp "$root/src/LICENSE" "$pkg/licenses/ScarletNX-LICENSE.txt"
if [[ -f "$root/src/src/nx/gpu/vk/fsr1/LICENSE.txt" ]]; then
    cp "$root/src/src/nx/gpu/vk/fsr1/LICENSE.txt" "$pkg/licenses/AMD-FidelityFX-FSR1-LICENSE.txt"
fi
if [[ -f "$root/src/src/nx/gpu/vk/fsr1/UPSTREAM.txt" ]]; then
    cp "$root/src/src/nx/gpu/vk/fsr1/UPSTREAM.txt" "$pkg/licenses/AMD-FidelityFX-FSR1-UPSTREAM.txt"
fi
echo "PACKAGE_OK $pkg"; ls "$pkg"
