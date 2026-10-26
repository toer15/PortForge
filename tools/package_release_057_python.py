#!/usr/bin/env python3
"""Package the authorized PortForge 0.57 / engine 1.40 release without PowerShell.

Preparation only (read-only; safe before the frontend finishes):
  python tools/package_release_057_python.py --plan

ONLY after the owner/parent explicitly confirms frontend tests and docs READY:
  python tools/package_release_057_python.py --package --ready-frontend-and-docs

Additional new project source files must be explicitly selected, for example:
  ... --include-source .github/workflows/new-recipe.yml

This helper never builds the frontend/engine, runs a game, invokes a .ps1,
changes policy, writes Git, uploads, or overwrites an existing release folder.
It uses current tracked working files, not committed blobs, plus explicit new
files. The separately tracked engine/src checkout is included in full subject
to junk/private-file exclusions. ZIP entry ordering/timestamps are fixed.
Installer compilation follows build-installer.ps1 using native .NET csc.exe.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import subprocess
import tempfile
import zipfile

VERSION = "0.57"
ENGINE_VERSION = "1.40"
ZIP_TIME = (2026, 10, 26, 0, 0, 0)
EXCLUDED_DIRS = {".git", ".svn", ".hg", ".cache", ".claude", "__pycache__",
                 "build", "obj", "bin", "node_modules", "package", "package-candidate"}
PRIVATE_NAMES = {"backend-location.txt", "prod.keys", "title.keys", "console.keys",
                 "keys.txt", ".gitmodules", ".git", ".mcp.json", ".env"}
PRIVATE_EXTENSIONS = {".nsp", ".nsz", ".xci", ".xcz", ".nca", ".nro", ".keys",
                      ".key", ".pem", ".pfx", ".p12", ".log", ".trace", ".etl",
                      ".dmp", ".bak", ".old", ".orig", ".rej", ".tmp", ".pyc"}
SOURCE_BINARY_EXTENSIONS = {".exe", ".dll", ".pdb", ".obj", ".o", ".a", ".lib"}
EXPLICIT_NEW = {
    "VALIDATION-0.57.md", "tests/fg_motion_deferral_test.c",
    "tests/run_fg_motion_deferral_tests.py", "tools/package_release_057_python.py",
    "tests/ProcessLookupTests.cs", "tests/run_process_lookup_tests.py",
}
REQUIRED_SOURCE = {
    "README.md", "VALIDATION-0.57.md", "engine/VERSION", "engine/portforge-engine.patch",
    "engine/build-engine.sh", "engine/src/LICENSE", "engine/src/CMakeLists.txt",
    "engine/src/src/nx/gpu/vk/fg.c", "engine/src/src/nx/gpu/vk/ffx_fg.c",
    "engine/src/src/nx/runtime/present.c", "engine/src/tests/runtime_present.c",
    "tests/fg_motion_deferral_test.c", "tests/run_fg_motion_deferral_tests.py",
    "tools/package_release_057_python.py", "build-native.ps1", "build-release.ps1",
    "build-installer.ps1", "installer/Setup.cs", "installer/Uninstall.cs",
    "installer/setup.manifest", "assets/logo.png", "assets/portforge.ico", "app.config",
}
APP_FILES = {
    "PortForge.exe": "build/PortForgeNative/PortForge.exe",
    "PortForge.exe.config": "app.config",
    "README.md": "USERGUIDE.md", "DEVELOPING.md": "NATIVE.md",
    "NativeTest/original-test.nro": "build/PortForgeNative/NativeTest/original-test.nro",
}


def fail(message: str) -> None:
    raise RuntimeError(message)


def run(command: list[str], cwd: Path | None = None) -> bytes:
    result = subprocess.run(command, cwd=cwd, capture_output=True, timeout=180)
    if result.returncode:
        fail(f"Command failed ({result.returncode}): {command!r}\n"
             + result.stdout.decode("utf-8", "replace") + result.stderr.decode("utf-8", "replace"))
    return result.stdout


def git_files(folder: Path, untracked: bool = False) -> list[str]:
    args = ["git", "-C", str(folder), "ls-files", "-z"]
    if untracked:
        args += ["--others", "--exclude-standard"]
    return [p.decode("utf-8") for p in run(args).split(b"\0") if p]


def clean_relative(name: str) -> str:
    normalized = name.replace("\\", "/")
    path = PurePosixPath(normalized)
    if path.is_absolute() or any(p in {"", ".", ".."} or ":" in p for p in path.parts):
        fail(f"Unsafe relative path: {name!r}")
    return path.as_posix()


def excluded(name: str, source: bool = True) -> bool:
    parts = PurePosixPath(name).parts
    lower = [p.lower() for p in parts]
    if any(p in EXCLUDED_DIRS or p.startswith("package-") for p in lower[:-1]):
        return True
    base = lower[-1]
    suffix = PurePosixPath(base).suffix
    return (base in PRIVATE_NAMES or base.startswith(".env.") or base.endswith("~")
            or suffix in PRIVATE_EXTENSIONS
            or (source and (suffix in SOURCE_BINARY_EXTENSIONS or base.endswith((".exe.config", ".dll.config")))))


def safe_file(root: Path, name: str) -> Path:
    name = clean_relative(name)
    candidate = root / name
    if not candidate.is_file():
        fail(f"Required regular file missing: {name}")
    resolved = candidate.resolve()
    if not resolved.is_relative_to(root.resolve()):
        fail(f"File resolves outside selected root: {name}")
    current = candidate
    while current != root:
        if current.is_symlink() or getattr(current, "is_junction", lambda: False)():
            fail(f"Symlink/junction not accepted: {name}")
        current = current.parent
    if not stat.S_ISREG(candidate.stat().st_mode):
        fail(f"Not a regular file: {name}")
    return candidate


def digest(path: Path) -> str:
    sha = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            sha.update(chunk)
    return sha.hexdigest()


def source_manifest(root: Path, extra: list[str]) -> tuple[dict[str, Path], list[str]]:
    selected: dict[str, Path] = {}
    explicit = EXPLICIT_NEW | {clean_relative(p) for p in extra}
    # Root and nested engine checkout each have their own tracked manifest.
    for checkout, prefix in ((root, ""), (root / "engine/src", "engine/src/")):
        actual_top = Path(run(["git", "-C", str(checkout), "rev-parse", "--show-toplevel"]).decode().strip())
        if actual_top.resolve() != checkout.resolve():
            fail(f"Expected a distinct Git checkout at {checkout}")
        for tracked in git_files(checkout):
            relative = clean_relative(prefix + tracked)
            if excluded(relative):
                continue
            if not (root / relative).exists():
                continue  # A tracked working-tree deletion is intentionally absent.
            selected[relative] = safe_file(root, relative)
    for relative in explicit:
        if excluded(relative):
            fail(f"Explicit source inclusion violates private/build exclusions: {relative}")
        if (root / relative).exists():
            selected[relative] = safe_file(root, relative)
        elif relative not in EXPLICIT_NEW:
            fail(f"Explicit additional source file missing: {relative}")
    unknown = []
    for checkout, prefix in ((root, ""), (root / "engine/src", "engine/src/")):
        unknown += [prefix + p for p in git_files(checkout, untracked=True)
                    if prefix + p not in selected and not excluded(prefix + p)]
    return dict(sorted(selected.items())), sorted(unknown)


def runtime_manifest(root: Path) -> dict[str, Path]:
    files = {name: safe_file(root, relative) for name, relative in APP_FILES.items()}
    package = root / "engine/package"
    for entry in sorted(package.rglob("*")):
        if not entry.is_file():
            continue
        rel = entry.relative_to(package).as_posix()
        # Tight allowlist, not a recursive copy of the contaminated frontend output.
        allowed = (rel in {"scarletnx.exe", "ENGINE.txt", "libLLVM-23.dll", "libc++.dll",
                           "libffi-8.dll", "libiconv-2.dll", "libxml2-16.dll", "libzstd.dll", "zlib1.dll"}
                   or (rel.startswith("licenses/") and entry.suffix.lower() == ".txt"))
        if not allowed or excluded("Engine/" + rel, source=False):
            fail(f"Unexpected file in engine/package; review rather than ship: {rel}")
        files["Engine/" + rel] = safe_file(root, "engine/package/" + rel)
    for required in ("Engine/scarletnx.exe", "Engine/ENGINE.txt",
                     "Engine/licenses/ScarletNX-LICENSE.txt",
                     "Engine/licenses/AMD-FidelityFX-SDK-1.1.4-LICENSE.txt"):
        if required not in files:
            fail(f"Required runtime/license missing: {required}")
    return dict(sorted(files.items()))


def compiler_path(requested: str | None) -> Path:
    compiler = Path(requested) if requested else Path(os.environ.get("WINDIR", "C:/Windows")) / "Microsoft.NET/Framework64/v4.0.30319/csc.exe"
    if not compiler.is_file():
        fail(f"Native csc.exe missing: {compiler}; no download or policy change attempted")
    return compiler


def check_versions(root: Path) -> None:
    if (root / "engine/VERSION").read_text(encoding="utf-8-sig").strip() != ENGINE_VERSION:
        fail("engine/VERSION is not the authorized 1.40")
    if not re.search(r"^version=1\.40\s*$", (root / "engine/package/ENGINE.txt").read_text(encoding="utf-8-sig"), re.M):
        fail("engine/package/ENGINE.txt is not version 1.40")
    assembly = (root / "src/AssemblyInfo.cs").read_text(encoding="utf-8-sig")
    if 'AssemblyVersion("0.57.0.0")' not in assembly or 'AssemblyFileVersion("0.57.0.0")' not in assembly:
        fail("Source frontend assembly version is not 0.57.0.0")


def copy_verified(source: Path, destination: Path, expected_hash: str) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    with source.open("rb") as src, destination.open("xb") as dst:
        shutil.copyfileobj(src, dst, 1024 * 1024)
    if digest(destination) != expected_hash or digest(source) != expected_hash:
        fail(f"Input changed during staging: {destination.name}; release left for inspection")


def zip_files(destination: Path, files: dict[str, Path]) -> None:
    with zipfile.ZipFile(destination, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=9, allowZip64=True) as archive:
        for name, path in sorted(files.items()):
            info = zipfile.ZipInfo(clean_relative(name), ZIP_TIME)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.create_system = 3
            info.external_attr = (stat.S_IFREG | 0o644) << 16
            info._compresslevel = 9
            with path.open("rb") as src, archive.open(info, "w", force_zip64=True) as dst:
                shutil.copyfileobj(src, dst, 1024 * 1024)
    with zipfile.ZipFile(destination) as archive:
        bad = archive.testzip()
        if bad:
            fail(f"ZIP CRC failed: {bad}")
        if set(archive.namelist()) != set(files) or len(archive.namelist()) != len(files):
            fail("ZIP manifest mismatch")


def compile_installer(root: Path, app: Path, out: Path, runtime: dict[str, Path], compiler: Path) -> Path:
    setup = out / f"PortForge-Setup-{VERSION}.exe"
    with tempfile.TemporaryDirectory(prefix="installer-work-", dir=out) as directory:
        work = Path(directory)
        payload = work / "payload.zip"
        zip_files(payload, {name: app / name for name in runtime})  # Source intentionally absent.
        uninstall = work / "Uninstall.exe"
        common = [str(compiler), "/nologo", "/optimize+", "/target:winexe", "/platform:x64",
                  "/r:System.Windows.Forms.dll", "/r:System.Drawing.dll", "/r:System.Core.dll",
                  f"/win32icon:{root / 'assets/portforge.ico'}"]
        run(common + [f"/out:{uninstall}", str(root / "installer/Uninstall.cs")])
        run(common + ["/r:System.IO.Compression.dll", f"/win32manifest:{root / 'installer/setup.manifest'}",
                      f"/resource:{payload},payload.zip", f"/resource:{uninstall},uninstall.exe",
                      f"/resource:{root / 'assets/logo.png'},logo.png", f"/out:{setup}", str(root / "installer/Setup.cs")])
    return setup


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--plan", action="store_true", help="Read-only input inspection (default)")
    mode.add_argument("--package", action="store_true", help="Stage/package only after explicit READY")
    parser.add_argument("--ready-frontend-and-docs", action="store_true", help="Operator confirms parent gave READY")
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--include-source", action="append", default=[], metavar="RELATIVE_FILE")
    parser.add_argument("--csc", help="Existing native .NET Framework C# compiler")
    args = parser.parse_args()
    root = args.root.resolve()
    out = root / "build" / f"release-{VERSION}"
    source, unknown = source_manifest(root, args.include_source)
    runtime = runtime_manifest(root)
    check_versions(root)
    compiler = compiler_path(args.csc)
    missing = sorted(REQUIRED_SOURCE - set(source))
    existing = sorted(p.name for p in out.iterdir()) if out.exists() else None
    excluded_native = sorted(p.name for p in (root / "build/PortForgeNative").iterdir()
                             if p.name not in {"PortForge.exe", "PortForge.exe.config", "README.md", "DEVELOPING.md", "Engine", "NativeTest"})
    plan = {"release": VERSION, "engine": ENGINE_VERSION, "mode": "PACKAGE" if args.package else "PLAN ONLY; no staging",
            "output": str(out), "existing_output_entries": existing, "source_file_count": len(source),
            "runtime_file_count": len(runtime), "missing_required_source": missing,
            "unselected_untracked_source_candidates": unknown, "excluded_native_output_items": excluded_native,
            "native_csc": str(compiler), "runtime_files": list(runtime),
            "root_source_files": [p for p in source if not p.startswith("engine/src/")]}
    print(json.dumps(plan, indent=2), flush=True)
    if not args.package:
        return
    if not args.ready_frontend_and_docs:
        fail("No packaging: --ready-frontend-and-docs is required AFTER explicit parent READY")
    if missing:
        fail(f"Required rebuild/release source missing: {missing}")
    if unknown:
        fail(f"Review new source candidates and select intended files with --include-source: {unknown}")
    if existing is not None:
        fail("Release folder already exists; inspect it, never overwrite/delete automatically")
    # Freeze hashes before staging to detect a still-changing frontend or source tree.
    source_hashes = {name: digest(path) for name, path in source.items()}
    runtime_hashes = {name: digest(path) for name, path in runtime.items()}
    out.mkdir(parents=True, exist_ok=False)
    app = out / "PortForge"
    app.mkdir()
    source_only = out / "source-only"
    source_only.mkdir()
    for name, path in runtime.items():
        copy_verified(path, app / name, runtime_hashes[name])
    for name, path in source.items():
        copy_verified(path, app / "Source" / name, source_hashes[name])
        copy_verified(path, source_only / name, source_hashes[name])
    windows_zip = out / f"PortForge-{VERSION}-Windows-and-Source.zip"
    source_zip = out / f"PortForge-{VERSION}-source.zip"
    zip_files(windows_zip, {**{name: app / name for name in runtime},
                            **{"Source/" + name: app / "Source" / name for name in source}})
    zip_files(source_zip, {name: source_only / name for name in source})
    setup = compile_installer(root, app, out, runtime, compiler)
    # Final check: every selected input still matches what was packaged.
    for collection, hashes in ((source, source_hashes), (runtime, runtime_hashes)):
        for name, path in collection.items():
            if digest(path) != hashes[name]:
                fail(f"Input changed before package completion: {name}; artifacts require inspection")
    hashes = {path.name: digest(path) for path in (windows_zip, source_zip, setup)}
    checksum_file = out / f"PortForge-{VERSION}-SHA256.txt"
    with checksum_file.open("x", encoding="ascii", newline="\n") as stream:
        stream.write("".join(f"{sha}  {name}\n" for name, sha in hashes.items()))
    manifest = {"release": VERSION, "engine": ENGINE_VERSION, "source_sha256": source_hashes,
                "runtime_sha256": runtime_hashes, "artifacts_sha256": hashes,
                "unselected_untracked_source_candidates": unknown,
                "installer_resources": ["payload.zip", "uninstall.exe", "logo.png"],
                "installer_payload_contains_source": False}
    with (out / "PACKAGING-MANIFEST.json").open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(manifest, stream, indent=2, sort_keys=True)
        stream.write("\n")
    print("Packaging complete; no upload/publish performed.", flush=True)
    print(checksum_file.read_text(encoding="ascii"), end="", flush=True)


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, OSError, subprocess.TimeoutExpired) as error:
        raise SystemExit(str(error))
