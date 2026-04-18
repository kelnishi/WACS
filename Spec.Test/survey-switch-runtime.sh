#!/bin/bash
# Survey switch-runtime coverage across the spec test suite. Runs each .wast
# through RunWastOnSwitch one-at-a-time via the `Single` knob — so a managed-
# stack overflow in one recursive test doesn't crash the rest of the suite.
#
# Overwrites bin/Debug/net8.0/testsettings.json per iteration to avoid a full
# rebuild between tests. Restores the original settings on completion.
#
# Run from repo root: ./Spec.Test/survey-switch-runtime.sh
# Output: /tmp/switch_survey.txt  (PASS/FAIL/CRASH/SKIP per .wast)

set -u
cd "$(dirname "$0")/.."
TESTS_DIR="Spec.Test/generated-json"
SETTINGS_SRC="Spec.Test/testsettings.json"
SETTINGS_OUT="Spec.Test/bin/Debug/net8.0/testsettings.json"
REPORT="/tmp/switch_survey.txt"
> "$REPORT"

# Ensure the test DLL exists before we start stamping testsettings into bin/.
dotnet build Spec.Test/Spec.Test.csproj -c Debug --verbosity quiet >/dev/null 2>&1

PASS=0; FAIL=0; CRASH=0; SKIP=0
TOTAL=$(ls "$TESTS_DIR" | wc -l | tr -d ' ')
i=0
for wast in $(ls "$TESTS_DIR"); do
  i=$((i+1))
  # Hard-skip bits of the spec suite that are known not to parse.
  case "$wast" in
    comments.wast|annotations.wast)
      echo "[$i/$TOTAL] $wast: SKIP"; SKIP=$((SKIP+1)); echo "SKIP $wast" >> "$REPORT"; continue;;
  esac
  # Subdirectories (simd/, gc/, relaxed-simd/, multi-memory/) and git_info.txt
  # pick up via `ls` — filter them out. The matching-Single won't find them, so
  # nothing would run, but avoid the noise in the report.
  if [ ! -f "$TESTS_DIR/$wast/$(ls "$TESTS_DIR/$wast" 2>/dev/null | head -1)" ] && [ ! -d "$TESTS_DIR/$wast" ]; then
    :
  fi
  # Each spec entry in generated-json is a directory named "<name>.wast"
  # containing .wasm + .json files. Skip anything that doesn't look like that.
  case "$wast" in
    *.wast) ;;
    *) echo "[$i/$TOTAL] $wast: SKIP (non-wast)"; SKIP=$((SKIP+1)); echo "SKIP $wast (non-wast)" >> "$REPORT"; continue;;
  esac
  if [ ! -d "$TESTS_DIR/$wast" ]; then
    echo "[$i/$TOTAL] $wast: SKIP (missing)"; SKIP=$((SKIP+1)); echo "SKIP $wast (missing)" >> "$REPORT"; continue
  fi

  cat > "$SETTINGS_OUT" <<EOF
{
  "JsonDirectory": "../../../generated-json",
  "RunTranspilerTests": false,
  "TraceExecution": false,
  "Single": "$wast",
  "SkipWasts": []
}
EOF
  OUT=$(dotnet test Spec.Test/Spec.Test.csproj -c Debug --verbosity quiet --filter "RunWastOnSwitch" --no-build 2>&1 | tail -3)
  if echo "$OUT" | grep -q "Passed!"; then
    echo "[$i/$TOTAL] $wast: PASS"; PASS=$((PASS+1)); echo "PASS $wast" >> "$REPORT"
  elif echo "$OUT" | grep -q "Failed!"; then
    echo "[$i/$TOTAL] $wast: FAIL"; FAIL=$((FAIL+1)); echo "FAIL $wast" >> "$REPORT"
  else
    echo "[$i/$TOTAL] $wast: CRASH"; CRASH=$((CRASH+1)); echo "CRASH $wast" >> "$REPORT"
  fi
done

echo
echo "=========================================="
echo "  $PASS pass / $FAIL fail / $CRASH crash / $SKIP skip   ($TOTAL total)"
echo "=========================================="

# Restore the default settings file so subsequent non-survey runs aren't stuck
# on the last Single.
cat > "$SETTINGS_OUT" <<EOF
{
  "JsonDirectory": "../../../generated-json",
  "RunTranspilerTests": true,
  "TraceExecution": false,
  "Single": null,
  "SkipWasts": [
    "comments.wast",
    "annotations.wast"
  ]
}
EOF
