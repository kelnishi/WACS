#!/bin/bash
# build_spec_tests.sh — generate a wast2json bundle directory for every
# `.wast` under <base_dir>. Each input produces a per-wast subdir
# containing one `.json` + side-car `.wasm` files.
#
# The Spec.Test runner reads `.wast` directly when `WastDirectory` is set
# in `testsettings.json` (the current default), so this script is for
# the legacy JSON-on-disk pipeline. Useful when comparing emission
# against `wabt`'s wast2json bundles or when running an external runner
# that only consumes the canonical JSON format.
#
# Pipeline: invokes `wacs wast2json` (this repo's own emitter) per
# .wast — no external `wasm-tools` / `wast2json` / node dependency.
# Run from the `Spec.Test/` directory.
#
# Usage:
#   build_spec_tests.sh [-w] [-b base_dir] [-o out_dir]
#     -w           also dump .wat next to each generated .wasm
#                  (uses `wacs inspect --dump-wat`)
#     -b base_dir  directory tree of .wast files (default: ./spec/test/core)
#     -o out_dir   bundle output root (default: ./generated-json)

set -euo pipefail

base_dir="./spec/test/core"
out_dir="./generated-json"
generate_wat=false

while getopts "wb:o:" opt; do
  case ${opt} in
    w) generate_wat=true ;;
    b) base_dir="$OPTARG" ;;
    o) out_dir="$OPTARG" ;;
    \?)
      echo "Usage: $0 [-w] [-b base_directory] [-o output_directory]"
      echo "  -w                 also dump .wat next to each generated .wasm"
      echo "  -b base_directory  default: ./spec/test/core"
      echo "  -o output_directory default: ./generated-json"
      exit 1
      ;;
  esac
done

# Always invoke the in-repo CLI build via `dotnet run` rather than a
# globally-installed `wacs`. A stale global install ships verbs that
# don't match HEAD, and this script lives next to the project anyway.
# `--no-build` keeps iteration fast once the consumer has built the
# CLI once; drop it (or run `dotnet build` ahead of time) on a fresh
# checkout.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CLI_PROJECT="${SCRIPT_DIR}/../Wacs.Console/Wacs.Console"
WACS=(dotnet run --project "$CLI_PROJECT" --no-build --)

# Clean output directory.
rm -rf "$out_dir"
mkdir -p "$out_dir"

# Convert every .wast.
find "$base_dir" -type f -name "*.wast" | while read -r wast_file; do
  filename=$(basename "$wast_file" .wast)
  relative_path="${wast_file#$base_dir/}"
  output_subdir=$(dirname "$relative_path")
  output_dir="$out_dir/$output_subdir/${filename}.wast"
  mkdir -p "$output_dir"

  # `wacs wast2json` writes <output_dir>/<basename>.json plus
  # <output_dir>/<basename>.<n>.wasm — matches what
  # `wasm-tools json-from-wast --wasm-dir <dir>` produced.
  "${WACS[@]}" wast2json "$wast_file" \
      --output-dir "$output_dir" --base-name "$filename"

  # Optional: render each side-car .wasm as .wat for diff-friendly
  # inspection. Uses `wacs inspect --dump-wat` (the WACS-native
  # equivalent of `wasm-tools print`).
  if $generate_wat; then
    find "$output_dir" -type f -name "${filename}.*.wasm" | while read -r wasm_file; do
      wat_output="${wasm_file%.wasm}.wat"
      "${WACS[@]}" inspect "$wasm_file" --dump-wat \
          --output-dir "$(dirname "$wat_output")"
      echo "Converted $wasm_file to $wat_output"
    done
  fi

  echo "Converted $wast_file to $output_dir/$filename.json"
done

# Record the spec submodule's git revision for traceability.
git_info=$(git -C "./spec" describe --tags --always)
echo "$git_info" > "$out_dir/git_info.txt"
