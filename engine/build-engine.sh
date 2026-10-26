#!/usr/bin/env bash
# Builds the PortForge Engine (a ScarletNX fork) inside MSYS2 CLANG64, as ScarletNX's own Windows CI does.
set -euo pipefail
cd "$(dirname "$0")/src"
export CCACHE_DIR="$PWD/.cache/ccache"
# ccache needs USERPROFILE, which a login shell started from another MSYS may not pass on
export USERPROFILE="${USERPROFILE:-$(cygpath -w "$HOME")}"
export CCACHE_BASEDIR="$PWD"
export CCACHE_NOHASHDIR=true
cmake --preset default \
  -DSNX_LLVM_CONFIG="$(cygpath -m "$(command -v llvm-config)")" \
  -DSNX_FFMPEG_SH="$(cygpath -m /usr/bin/sh.exe)" \
  -DSNX_FFMPEG_MAKE="$(cygpath -m /usr/bin/make.exe)" \
  -DPERL_EXECUTABLE="$(cygpath -m /usr/bin/perl.exe)" \
  -DSNX_OPENSSL_MAKE="$(cygpath -m /usr/bin/make.exe)" \
  -DCMAKE_DISABLE_FIND_PACKAGE_SDL3=ON -DSNX_DEPLOY=OFF \
  -DCMAKE_INSTALL_BINDIR=. \
  -DCMAKE_C_COMPILER_LAUNCHER=ccache -DCMAKE_CXX_COMPILER_LAUNCHER=ccache
cmake --build --preset default --parallel "$(getconf _NPROCESSORS_ONLN)"
cmake --install build/default --prefix ../package
echo ENGINE_BUILD_OK