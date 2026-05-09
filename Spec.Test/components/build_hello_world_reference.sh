#!/bin/bash
# build_hello_world_reference.sh — regenerate the pinned
# wit-bindgen-csharp reference output for the hello-world fixture.
#
# The hello-world fixture under
# `Spec.Test/components/fixtures/hello-world/reference/` is the
# byte-for-byte ground truth that `Wacs.ComponentModel`'s
# `CSharpEmitter` reproduces. It's the only fixture that relies on an
# external code generator (`wit-bindgen c-sharp -r native-aot`); the
# other fixtures are wasm binaries handled by `build_fixtures.sh`.
#
# Usage:
#   build_hello_world_reference.sh           — regenerate reference/* in place
#   build_hello_world_reference.sh --check   — verify committed reference matches regen, exit 1 on drift
#
# Requirements:
#   - `wit-bindgen` 0.30.0 on PATH (`cargo install wit-bindgen-cli --version 0.30.0 --locked`)
#     The version pin is intentional — `EmitOptions.PinnedWitBindgenCSharpVersion`
#     in `Wacs.ComponentModel` is locked to 0.30.0 and must move
#     in lockstep with this script.
#   - Submodule `Spec.Test/components/wasi-cli` checked out (any
#     version — the script reads its `wit/` for the deps tree).

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
FIXTURE="${SCRIPT_DIR}/fixtures/hello-world"
WASI_CLI="${SCRIPT_DIR}/wasi-cli"
PINNED_VERSION="0.30.0"

CHECK_MODE=0
for arg in "$@"; do
    case "$arg" in
        --check) CHECK_MODE=1 ;;
        --help|-h)
            grep -E '^# ' "$0" | sed 's/^# \?//'
            exit 0
            ;;
        *)
            echo "error: unexpected argument '$arg'" >&2
            exit 2
            ;;
    esac
done

if ! command -v wit-bindgen >/dev/null 2>&1; then
    echo "error: wit-bindgen not found on PATH" >&2
    echo "install: cargo install wit-bindgen-cli --version $PINNED_VERSION --locked" >&2
    exit 2
fi
have_version=$(wit-bindgen --version 2>/dev/null | awk '{print $NF}')
if [ "$have_version" != "$PINNED_VERSION" ]; then
    echo "error: wit-bindgen version mismatch (have $have_version, need $PINNED_VERSION)" >&2
    echo "the pinned version is locked to match" >&2
    echo "Wacs.ComponentModel/Wacs.ComponentModel/CSharpEmit/EmitOptions.cs" >&2
    echo "PinnedWitBindgenCSharpVersion. To bump, update both in lockstep." >&2
    exit 2
fi

if ! [ -d "$WASI_CLI/wit" ]; then
    echo "error: wasi-cli submodule not present at $WASI_CLI/wit" >&2
    echo "  run: git submodule update --init Spec.Test/components/wasi-cli" >&2
    exit 2
fi

# Compose a temp WIT dir holding hello's WIT + the wasi-cli deps
# tree the world imports. The fixture's `wit/` only contains
# `hello.wit` — `deps/` isn't committed because it just mirrors
# the wasi-cli submodule's tree.
TMP=$(mktemp -d -t wacs-hello-bindgen.XXXXXX)
trap 'rm -rf "$TMP"' EXIT

cp "$FIXTURE/wit/hello.wit" "$TMP/"
mkdir -p "$TMP/deps/cli"
cp "$WASI_CLI"/wit/*.wit "$TMP/deps/cli/"
cp -R "$WASI_CLI"/wit/deps/* "$TMP/deps/"

if [ "$CHECK_MODE" = "1" ]; then
    # Regenerate to a temp dir and compare against the committed
    # reference. Drift means either wit-bindgen output changed
    # (unlikely for a pinned tool version) or the wasi-cli
    # submodule moved without the reference being refreshed.
    out="$TMP/regen"
    mkdir -p "$out"
    if ! wit-bindgen c-sharp -r native-aot -w hello --out-dir "$out" "$TMP" \
            >/dev/null 2>"$TMP/wb.err"; then
        echo "error: wit-bindgen failed" >&2
        sed 's/^/  /' "$TMP/wb.err" >&2
        exit 1
    fi
    if diff -rq "$FIXTURE/reference" "$out" >/dev/null 2>&1; then
        echo "match: $FIXTURE/reference is in sync"
        exit 0
    fi
    echo "drift: $FIXTURE/reference differs from regen" >&2
    diff -rq "$FIXTURE/reference" "$out" >&2 | sed 's/^/  /' >&2 || true
    exit 1
else
    # In-place regen.
    rm -rf "$FIXTURE/reference"
    if ! wit-bindgen c-sharp -r native-aot -w hello \
            --out-dir "$FIXTURE/reference" "$TMP" \
            >/dev/null 2>"$TMP/wb.err"; then
        echo "error: wit-bindgen failed" >&2
        sed 's/^/  /' "$TMP/wb.err" >&2
        exit 1
    fi
    file_count=$(ls "$FIXTURE/reference" | wc -l | tr -d ' ')
    echo "regenerated: $file_count files in $FIXTURE/reference"
fi
exit 0
