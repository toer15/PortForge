#!/usr/bin/env python3
"""Isolated .NET 4.8 process lookup regressions; no PowerShell or frontend build.

Run: python tests/run_process_lookup_tests.py
Outputs/logs are retained under build/process-lookup-<timestamp>-<unique>/.
Only launches/stops owned TuningFixture.exe copies in that output directory.
"""
import argparse
from datetime import datetime
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, help='Override GameOptimization.cs for comparison only')
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    source = args.source or root / 'src/GameOptimization.cs'
    compiler = Path(os.environ.get('WINDIR', 'C:/Windows')) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    if not compiler.is_file():
        raise RuntimeError('Native framework compiler not found; nothing installed or policy changed')
    out = Path(tempfile.mkdtemp(prefix='process-lookup-' + datetime.now().strftime('%Y%m%d-%H%M%S') + '-', dir=root / 'build'))
    records = []
    def run(label, command):
        result = subprocess.run([str(c) for c in command], cwd=root, capture_output=True, timeout=45)
        (out / (label + '.stdout.txt')).write_bytes(result.stdout)
        (out / (label + '.stderr.txt')).write_bytes(result.stderr)
        records.append(dict(label=label, command=[str(c) for c in command], returncode=result.returncode))
        print(label + ': exit ' + str(result.returncode), flush=True)
        if result.stdout:
            print(result.stdout.decode('utf-8-sig', 'replace'), end='', flush=True)
        if result.stderr:
            print(result.stderr.decode('utf-8-sig', 'replace'), end='', flush=True)
        if result.returncode:
            raise RuntimeError(label + ' failed: see ' + str(out))
    summary = dict(source=str(source), source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(), output=str(out), commands=records)
    try:
        common = [compiler, '/nologo', '/optimize+', '/target:exe', '/platform:x64', '/r:System.Core.dll']
        fixture = out / 'TuningFixture.exe'
        run('compile-fixture', common + ['/out:' + str(fixture), root / 'tests/TuningFixture.cs'])
        test = out / 'ProcessLookupTests.exe'
        run('compile-real', common + ['/out:' + str(test), source, root / 'tests/ProcessLookupTests.cs'])
        run('run-real', [test, fixture, out / 'real'])
        text = source.read_text(encoding='utf-8-sig')
        substitutions = {
            'OpenProcess(ProcessQueryLimitedInformation, false, processId)': 'ProcessLookupNativeFaults.Open(ProcessQueryLimitedInformation, false, processId)',
            'QueryFullProcessImageName(handle, 0, path, ref size)': 'ProcessLookupNativeFaults.Query(handle, 0, path, ref size)',
            'GetExitCodeProcess(handle, out exitCode)': 'ProcessLookupNativeFaults.Status(handle, out exitCode)',
            'finally { CloseHandle(handle); }': 'finally { ProcessLookupNativeFaults.Close(handle); }',
        }
        for before, after in substitutions.items():
            if before not in text:
                raise RuntimeError('Expected fault injection call-site missing: ' + before)
            text = text.replace(before, after)
        injected = out / 'GameOptimization.fault-injected.cs'
        injected.write_text(text, encoding='utf-8')
        fault_test = out / 'ProcessLookupFaultTests.exe'
        run('compile-faults', common + ['/define:PROCESS_LOOKUP_NATIVE_FAULTS', '/out:' + str(fault_test), injected, root / 'tests/ProcessLookupTests.cs'])
        run('run-faults', [fault_test, fixture, out / 'faults'])
        summary['passed'] = True
    finally:
        (out / 'results.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
        print('Evidence: ' + str(out / 'results.json'), flush=True)


if __name__ == '__main__':
    main()
