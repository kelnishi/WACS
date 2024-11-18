#!/bin/bash

# Set the parent directory
PARENT_DIR="./wasm-feature-detect/src/detectors"

OUTPUT_DIR="./generated-wasm"

mkdir -p "$OUTPUT_DIR"

# Iterate over all directories in the parent directory
for dir in "$PARENT_DIR"/*/; do
    # Check if it is indeed a directory
    if [ -d "$dir" ]; then
        echo "Processing directory: $dir"
        # Run the node command with the directory as an argument
        node convert_detector.mjs "$dir" "$OUTPUT_DIR"
    fi
done

#run detection
dotnet test --logger "trx;LogFileName=TestResults.trx"

#publish support matrix
node trx-to-markdown.js TestResults/TestResults.trx ../features.md
