#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/src"
cmake --build build/default --target snx-test-runtime-present snx-test-frame-policy snx-test-pipeline-file --parallel 8
ctest --test-dir build/default -R '^(runtime_present|frame_policy|pipeline_file)$' --output-on-failure
