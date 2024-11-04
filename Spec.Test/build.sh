#!/bin/zsh

#!/bin/zsh

# Define the base directory containing the .wast files
base_dir="./spec/test/core"

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
    output_dir="./json/$output_subdir/${filename}"
    mkdir -p "$output_dir"

    # Construct the output JSON file path
    json_output="$output_dir/$filename.json"

    # Call wast2json to convert the .wast file to JSON format
    wast2json -o "$json_output" "$wast_file"

    echo "Converted $wast_file to $json_output"
  fi
done

