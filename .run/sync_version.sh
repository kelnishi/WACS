#!/bin/bash

# --------------------------------------------
# Script to Sync .NET Assembly Version to package.json
# --------------------------------------------

# Exit immediately if a command exits with a non-zero status
set -e

# Function to display usage
usage() {
  echo "Usage: $0 [path/to/project.csproj] [path/to/package.json]"
  echo "If no arguments are provided, it searches for the first .csproj file and assumes package.json is in the current directory."
  exit 1
}

# Check for help flag
if [[ "$1" == "--help" || "$1" == "-h" ]]; then
  usage
fi

# Assign arguments or set defaults
CSPROJ_FILE="${1:-}"
PACKAGE_JSON_FILE="${2:-package.json}"

# Function to find the first .csproj file if not provided
find_csproj() {
  local csproj
  csproj=$(find . -maxdepth 1 -name "*.csproj" | head -n 1)
  echo "$csproj"
}

# Determine the .csproj file
if [[ -z "$CSPROJ_FILE" ]]; then
  CSPROJ_FILE=$(find_csproj)
  if [[ -z "$CSPROJ_FILE" ]]; then
    echo "Error: No .csproj file found in the current directory."
    exit 1
  fi
fi

# Check if the .csproj file exists
if [[ ! -f "$CSPROJ_FILE" ]]; then
  echo "Error: .csproj file '$CSPROJ_FILE' does not exist."
  exit 1
fi

# Check if package.json exists
if [[ ! -f "$PACKAGE_JSON_FILE" ]]; then
  echo "Error: package.json file '$PACKAGE_JSON_FILE' does not exist."
  exit 1
fi

# Extract the version from the .csproj file using sed
# This sed command captures the content between <Version> and </Version>
VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$CSPROJ_FILE")

# If VERSION is empty, try to extract from AssemblyInfo.cs
if [[ -z "$VERSION" ]]; then
  # Assuming AssemblyInfo.cs is located in Properties directory
  ASSEMBLY_INFO=$(find . -path "*/Properties/AssemblyInfo.cs" | head -n 1)
  if [[ -n "$ASSEMBLY_INFO" ]]; then
    VERSION=$(sed -n 's/.*AssemblyVersion("\([^"]*\)").*/\1/p' "$ASSEMBLY_INFO")
  fi
fi

# If VERSION is still empty, exit with error
if [[ -z "$VERSION" ]]; then
  echo "Error: Could not find the version in $CSPROJ_FILE or AssemblyInfo.cs."
  exit 1
fi

echo "Detected .NET project version: $VERSION"

# Function to update package.json using jq
update_package_json_jq() {
  local ver="$1"
  local pkg="$2"
  jq --arg ver "$ver" '.version = $ver' "$pkg" > "${pkg}.tmp" && mv "${pkg}.tmp" "$pkg"
}

# Function to update package.json using sed
update_package_json_sed() {
  local ver="$1"
  local pkg="$2"
  # This regex matches the "version": "x.y.z" pattern and replaces it
  sed -i.bak -E 's/("version"\s*:\s*")[^"]+(")/\1'"$ver"'\2/' "$pkg"
}

# Update package.json
if command -v jq > /dev/null 2>&1; then
  echo "Updating package.json using jq..."
  update_package_json_jq "$VERSION" "$PACKAGE_JSON_FILE"
  echo "Successfully updated 'version' in $PACKAGE_JSON_FILE to $VERSION."
else
  echo "jq is not installed. Falling back to sed."
  update_package_json_sed "$VERSION" "$PACKAGE_JSON_FILE"
  echo "Successfully updated 'version' in $PACKAGE_JSON_FILE to $VERSION."
  echo "A backup of the original package.json is saved as package.json.bak."
fi

exit 0