#!/bin/bash
# Extract OpenAPI spec using Docker
# Usage: ./scripts/extract-openapi-docker.sh [output-file]

set -e

OUTPUT_FILE="${1:-openapi.json}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
IMAGE_NAME="doclens-openapi-extractor"

echo "DocLens OpenAPI Spec Extractor (Docker)"
echo "========================================"
echo ""

cd "$PROJECT_DIR"

# Build the Docker image
echo "Building Docker image..."
docker build -f scripts/Dockerfile.openapi -t "$IMAGE_NAME" .

echo ""
echo "Extracting OpenAPI spec..."

# Run the container and extract the spec
docker run --rm -v "$(pwd):/output" "$IMAGE_NAME"

# Check if file was created
if [ -f "$OUTPUT_FILE" ]; then
    echo ""
    echo "Success! OpenAPI spec saved to: $OUTPUT_FILE"
    echo ""
    echo "Endpoints found:"
    jq -r '.paths | keys[]' "$OUTPUT_FILE" 2>/dev/null || cat "$OUTPUT_FILE"
else
    echo "ERROR: Failed to extract OpenAPI spec"
    exit 1
fi
