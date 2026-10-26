#!/usr/bin/env python3
"""Run isolated fg.c control-flow tests; does not invoke any project build tooling.

Usage (from repository root):
  python tests/run_fg_motion_deferral_tests.py --check-sensitivity
  python tests/run_fg_motion_deferral_tests.py --cc C:/msys64/clang64/bin/clang.exe

Uses an already installed clang/gcc/cc and the Python standard library only.
The real vk_fg_interpolate_mids() is extracted into a temporary include, compiled
with mocked dependencies, and executed. Temporary files are removed on exit.
Optional mutations prove tests reject eager search, missing fallback searches,
and stale command-buffer use. They never modify production source.
This tests scheduling/control flow, not Vulkan execution, helper internals, or image quality.
"""
from __future__ import annotations

import argparse
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile


def extract_function(source: str) -> str:
    signature = "int vk_fg_interpolate_mids("
    if source.count(signature) != 1:
        raise ValueError("Expected exactly one production vk_fg_interpolate_mids definition")
    start = source.index(signature)
    # Count braces while skipping comments and string/character literals.
    token = re.compile(r'/\*.*?\*/|//[^\n]*|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|[{}]', re.S)
    depth = 0
    for match in token.finditer(source, start):
        if match.group() == "{":
            depth += 1
        elif match.group() == "}":
            depth -= 1
            if depth == 0:
                return source[start:match.end()] + "\n"
    raise ValueError("Could not find the end of vk_fg_interpolate_mids")


def replace_once(text: str, old: str, new: str) -> str:
    if text.count(old) != 1:
        raise ValueError(f"Mutation target must be unique: {old!r}")
    return text.replace(old, new, 1)


def find_compiler(requested: str | None) -> str:
    if requested:
        compiler = shutil.which(requested)
        if compiler:
            return compiler
        raise SystemExit(f"Compiler unavailable: {requested}; no installation attempted")
    for name in ("clang", "gcc", "cc"):
        compiler = shutil.which(name)
        if compiler:
            return compiler
    for path in ("C:/msys64/clang64/bin/clang.exe", "C:/msys64/ucrt64/bin/gcc.exe",
                 "C:/msys64/mingw64/bin/gcc.exe", "C:/Program Files/LLVM/bin/clang.exe"):
        if Path(path).is_file():
            return path
    raise SystemExit("No installed C compiler found. Supply --cc; no installation attempted.")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cc", help="Path to an installed clang/gcc-compatible C compiler")
    parser.add_argument("--source", type=Path, help="Alternate fg.c for reviewing a baseline")
    parser.add_argument("--check-sensitivity", action="store_true")
    args = parser.parse_args()
    tests = Path(__file__).resolve().parent
    source_path = args.source or tests.parent / "engine/src/src/nx/gpu/vk/fg.c"
    function = extract_function(source_path.read_text(encoding="utf-8"))
    compiler = find_compiler(args.cc)
    env = os.environ.copy()
    env["PATH"] = str(Path(compiler).parent) + os.pathsep + env.get("PATH", "")
    print(f"Compiler: {compiler}\nSource: {source_path}", flush=True)
    with tempfile.TemporaryDirectory(prefix="fg-motion-test-", dir=tests) as directory:
        scratch = Path(directory)
        include = scratch / "fg_interpolate_under_test.inc"
        executable = scratch / ("fg_motion_test.exe" if os.name == "nt" else "fg_motion_test")

        def compile_and_run(body: str) -> subprocess.CompletedProcess[str]:
            include.write_text(body, encoding="utf-8")
            command = [compiler, "-std=c11", "-Wall", "-Wextra", "-Werror", "-O0",
                       "-I", str(scratch), str(tests / "fg_motion_deferral_test.c"),
                       "-o", str(executable)]
            compiled = subprocess.run(command, capture_output=True, text=True, env=env, timeout=60)
            if compiled.returncode:
                raise RuntimeError("Harness compilation failed:\n" + compiled.stdout + compiled.stderr)
            return subprocess.run([str(executable)], capture_output=True, text=True, env=env, timeout=15)

        result = compile_and_run(function)
        print(result.stdout, end="", flush=True)
        if result.returncode:
            raise SystemExit(result.stderr or f"Harness exited {result.returncode}")

        if args.check_sensitivity:
            main_search = "    /* AMD does not use med[]; search only when the legacy warps need it. */\n    if (!fg_motion(cb)) goto failed;\n"
            head, skip = function.split("fsr_skip:", 1)
            no_skip_search = head + "fsr_skip:" + replace_once(skip, "    if (!fg_motion(cb)) goto failed;\n", "")
            no_skip_refresh = head + "fsr_skip:" + replace_once(skip, "    cb = vk_cmd();\n", "")
            mutations = {
                "eager legacy search before filtering/AMD": replace_once(
                    function, "    if (g_fg.up_w != dw || g_fg.up_h != dh) {",
                    "    if (!fg_motion(cb)) goto failed;\n    if (g_fg.up_w != dw || g_fg.up_h != dh) {"),
                "missing enlarged-fallback motion search": replace_once(function, main_search, ""),
                "missing fsr_skip motion search": no_skip_search,
                "stale buffer after AMD failure": replace_once(function, "    cb = vk_cmd();\n" + main_search, main_search),
                "stale buffer after fsr_skip blit failure": no_skip_refresh,
            }
            for name, mutant in mutations.items():
                result = compile_and_run(mutant)
                if result.returncode == 0:
                    raise SystemExit(f"FAIL regression sensitivity: mutant survived: {name}")
                if "FAIL " not in result.stderr:
                    raise SystemExit(f"Mutation failed unexpectedly: {name}\n{result.stderr}")
                print(f"PASS regression sensitivity: rejected {name}", flush=True)
            print(f"All {len(mutations)} control-flow mutants rejected.", flush=True)


if __name__ == "__main__":
    main()
