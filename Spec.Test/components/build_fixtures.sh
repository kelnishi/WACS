#!/bin/bash
# build_fixtures.sh — regenerate `Spec.Test/components/fixtures/*/wasm/*.component.wasm`
# from the matching `.wat` source + WIT tree.
#
# Each fixture under `Spec.Test/components/fixtures/<name>/` has the
# layout:
#   wit/<base>.wit       — world definition (always present when there's a wat)
#   wit/<base>.wat       — wasm-text-format core module source
#   deps/<pkg>/*.wit     — optional vendored WASI deps the world imports
#   wasm/<base>.component.wasm — committed compiled output (this script regens)
#
# The pipeline mirrors what `wasm-tools` expects:
#   1. Lay out a WIT tree: world WIT + deps siblings under one root.
#   2. `wasm-tools component embed --world <w> <root> <wat> -o <embed>.wasm`
#   3. `wasm-tools component new <embed>.wasm -o <fixture>/wasm/<base>.component.wasm`
#
# Fixtures without a `.wat` are skipped — they're either WIT-only (drive
# the C# emitter reference outputs under `reference/`) or don't have
# committed wasm at all (`aggregates/`, `interop-*/`, etc.).
#
# Usage:
#   build_fixtures.sh                       — regenerate all fixtures in-place
#   build_fixtures.sh --check               — verify committed wasm matches regen, exit 1 on drift
#   build_fixtures.sh <fixture-dir>...      — regenerate only the listed fixtures
#
# Requirements: `wasm-tools` 1.221+ on PATH (`cargo install wasm-tools`).

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
FIXTURES_DIR="${SCRIPT_DIR}/fixtures"
TMP_ROOT=$(mktemp -d -t wacs-fixture-build.XXXXXX)
trap 'rm -rf "$TMP_ROOT"' EXIT

CHECK_MODE=0
EXPLICIT=()
for arg in "$@"; do
    case "$arg" in
        --check) CHECK_MODE=1 ;;
        --help|-h)
            grep -E '^# ' "$0" | sed 's/^# \?//'
            exit 0
            ;;
        *) EXPLICIT+=("$arg") ;;
    esac
done

if ! command -v wasm-tools >/dev/null 2>&1; then
    echo "error: wasm-tools not found on PATH" >&2
    echo "install: cargo install wasm-tools" >&2
    exit 2
fi

# Iterate the fixture set: explicit dirs from argv, or every dir under
# `fixtures/` that has at least one `.wat`.
fixtures=()
if [ "${#EXPLICIT[@]}" -gt 0 ]; then
    fixtures=("${EXPLICIT[@]}")
else
    for fx in "$FIXTURES_DIR"/*/; do
        if compgen -G "${fx}wit/*.wat" >/dev/null; then
            fixtures+=("${fx%/}")
        fi
    done
fi

regen_count=0
matched_count=0
skip_count=0
drift_count=0
fail_count=0
drift_paths=()

# Ascertain the world name for a fixture: grep `^world <name>` from the
# matching `.wit` file. Most fixtures have one .wit + one .wat with the
# same basename; some have a non-matching world name (e.g.
# utf16-string-component has wat `u16` but world `utf16`).
extract_world() {
    local wit="$1"
    grep -E '^[[:space:]]*world[[:space:]]+' "$wit" \
        | head -1 \
        | sed -E 's/.*world[[:space:]]+([a-zA-Z0-9_-]+).*/\1/'
}

for fx in "${fixtures[@]}"; do
    fxname=$(basename "$fx")
    # Each fixture should have one .wat. Skip ones without (WIT-only).
    wats=("$fx"/wit/*.wat)
    if ! [ -e "${wats[0]}" ]; then
        skip_count=$((skip_count + 1))
        continue
    fi
    for wat in "${wats[@]}"; do
        base=$(basename "$wat" .wat)
        wit="$fx/wit/${base}.wit"
        if ! [ -e "$wit" ]; then
            # Fall back to any .wit in the dir.
            wit=$(ls "$fx"/wit/*.wit 2>/dev/null | head -1 || true)
        fi
        if [ -z "$wit" ] || ! [ -e "$wit" ]; then
            echo "WARN  $fxname: no .wit for $base.wat — skipping" >&2
            fail_count=$((fail_count + 1))
            continue
        fi
        world=$(extract_world "$wit")
        if [ -z "$world" ]; then
            echo "WARN  $fxname: no `world` declaration in $(basename "$wit") — skipping" >&2
            fail_count=$((fail_count + 1))
            continue
        fi

        # Compose the WIT tree wasm-tools expects: <root>/<world>.wit
        # + <root>/deps/<pkg>/*.wit. The fixture's deps/ tree (when
        # present) gets copied verbatim.
        witdir="$TMP_ROOT/${fxname}-${base}"
        rm -rf "$witdir"
        mkdir -p "$witdir"
        cp "$wit" "$witdir/"
        if [ -d "$fx/deps" ]; then
            cp -R "$fx/deps" "$witdir/deps"
        fi

        # Optional per-fixture string-encoding override. A
        # `wit/.encoding` file holding `utf8` / `utf16` /
        # `compact-utf16` flips `wasm-tools component embed`'s
        # default (utf8) for fixtures that exercise the non-default
        # canon-lift encodings — three today: utf16-string,
        # utf16-string-param, compact-utf16.
        encoding_args=()
        if [ -f "$fx/wit/.encoding" ]; then
            enc=$(tr -d '[:space:]' <"$fx/wit/.encoding")
            if [ -n "$enc" ]; then
                encoding_args=(--encoding "$enc")
            fi
        fi

        embed="$TMP_ROOT/${fxname}-${base}.embed.wasm"
        out="$fx/wasm/${base}.component.wasm"

        if ! wasm-tools component embed --world "$world" \
                "${encoding_args[@]}" \
                "$witdir" "$wat" \
                -o "$embed" >/dev/null 2>"$TMP_ROOT/embed.err"; then
            echo "FAIL  $fxname: embed failed for $base" >&2
            sed 's/^/  /' "$TMP_ROOT/embed.err" >&2
            fail_count=$((fail_count + 1))
            continue
        fi

        if [ "$CHECK_MODE" = "1" ]; then
            # Regenerate to a temp file and compare against the
            # committed binary. Drift means the committed binary
            # doesn't match what the .wat + .wit + deps would produce.
            check_out="$TMP_ROOT/${fxname}-${base}.check.wasm"
            if ! wasm-tools component new "$embed" -o "$check_out" \
                    >/dev/null 2>"$TMP_ROOT/new.err"; then
                echo "FAIL  $fxname: component new failed for $base" >&2
                fail_count=$((fail_count + 1))
                continue
            fi
            if ! cmp -s "$out" "$check_out"; then
                echo "DRIFT $fxname/wasm/$base.component.wasm" >&2
                drift_paths+=("$out")
                drift_count=$((drift_count + 1))
            else
                matched_count=$((matched_count + 1))
            fi
        else
            mkdir -p "$fx/wasm"
            if ! wasm-tools component new "$embed" -o "$out" \
                    >/dev/null 2>"$TMP_ROOT/new.err"; then
                echo "FAIL  $fxname: component new failed for $base" >&2
                sed 's/^/  /' "$TMP_ROOT/new.err" >&2
                fail_count=$((fail_count + 1))
                continue
            fi
            regen_count=$((regen_count + 1))
        fi
    done
done

echo
if [ "$CHECK_MODE" = "1" ]; then
    echo "checked: $((matched_count + drift_count + fail_count)) fixtures — $matched_count match, $drift_count drift, $fail_count fail, $skip_count skip"
    if [ "$drift_count" -gt 0 ] || [ "$fail_count" -gt 0 ]; then
        exit 1
    fi
else
    echo "regenerated: $regen_count fixtures, $fail_count failed, $skip_count skipped"
    if [ "$fail_count" -gt 0 ]; then
        exit 1
    fi
fi
exit 0
