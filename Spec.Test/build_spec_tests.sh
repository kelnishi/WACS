#!/bin/bash

# Default values for base_dir and out_dir
base_dir="./spec/test/core"
out_dir="./generated-json"
generate_wat=false

# Parse command-line options
while getopts "w:b:o:" opt; do
  case ${opt} in
    w )
      generate_wat=true
      ;;
    b )
      base_dir="$OPTARG"
      ;;
    o )
      out_dir="$OPTARG"
      ;;
    \? )
      echo "Usage: $0 [-w] [-b base_directory] [-o output_directory]"
      echo "  -w                 Generate .wat files"
      echo "  -b base_directory  Specify base directory (default: ./spec/test/core)"
      echo "  -o output_directory Specify output directory (default: ./generated-json)"
      exit 1
      ;;
  esac
done

# Clean up the output directory before processing
rm -rf "$out_dir"
mkdir -p "$out_dir"

# Use find to locate all .wast files in the base directory and its subdirectories
find "$base_dir" -type f -name "*.wast" | while read -r wast_file; do
  # Check if the file exists (optional since find guarantees it)
  if [[ -f $wast_file ]]; then
    # Extract the filename without the path and extension
    filename=$(basename "$wast_file" .wast)
    
    # Create a new directory named after the filename (retain any path)
    # Get the relative path from the base_dir to the wast_file
    relative_path="${wast_file#$base_dir/}"
    # Extract the directory part of the relative path
    output_subdir=$(dirname "$relative_path")
    
    # Construct the output directory, including the subdirectories
    output_dir="$out_dir/$output_subdir/${filename}.wast"
    mkdir -p "$output_dir"

    # Construct the output JSON file path
    json_output="$output_dir/$filename.json"

    # Call wast2json to convert the .wast file to JSON format
    wasm-tools json-from-wast --wasm-dir $output_dir -o "$json_output" "$wast_file"
    node format-wast-json.js $json_output
    
    # Call wasm2wat on any $wast_file.#.wasm files that get created
    # Search for all corresponding .wasm files in the output directory
    find "$output_dir" -type f -name "${filename}.*.wasm" | while read -r wasm_file; do
      # Construct the output wat file path
      wat_output="${wasm_file%.wasm}.wat"
      
      # Call wasm2wat to convert .wasm to .wat if the option was set
      if $generate_wat; then
        wasm-tools print "$wasm_file" -o "$wat_output" --no-check
        echo "Converted $wasm_file to $wat_output"
      fi
    done

    echo "Converted $wast_file to $json_output"
    
  fi
done

# Get the current git tag or commit hash
git_info=$(git -C "./spec" describe --tags --always)

# Create a record file in the output directory
echo "$git_info" > "$out_dir/git_info.txt"
