set -e
pacman -Syu --noconfirm || true
pacman -Syu --noconfirm || true
pacman -S --needed --noconfirm git make patch perl patchutils mingw-w64-clang-x86_64-glslang mingw-w64-clang-x86_64-clang mingw-w64-clang-x86_64-llvm mingw-w64-clang-x86_64-lld mingw-w64-clang-x86_64-cmake mingw-w64-clang-x86_64-ninja mingw-w64-clang-x86_64-nasm mingw-w64-clang-x86_64-pkgconf mingw-w64-clang-x86_64-python mingw-w64-clang-x86_64-spirv-tools mingw-w64-clang-x86_64-ccache
echo SETUP_DONE
