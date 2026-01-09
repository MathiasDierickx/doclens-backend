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

# Check if frontend folder exists and run codegen
FRONTEND_DIR="$PROJECT_DIR/../doclens-app"
if [ -d "$FRONTEND_DIR" ]; then
    echo ""
    echo "Frontend folder found at: $FRONTEND_DIR"
    echo "Copying OpenAPI spec and running codegen..."

    # Copy OpenAPI spec to frontend
    cp "$OUTPUT_FILE" "$FRONTEND_DIR/openapi.json"
    echo "Copied $OUTPUT_FILE to $FRONTEND_DIR/openapi.json"

    # Run codegen in frontend
    cd "$FRONTEND_DIR"
    if [ -f "package.json" ] && grep -q '"codegen"' package.json; then
        echo "Running npm run codegen..."
        npm run codegen
        echo ""
        echo "Frontend API codegen completed!"
    else
        echo "Warning: No codegen script found in frontend package.json"
    fi
else
    echo ""
    echo "Note: Frontend folder not found at $FRONTEND_DIR"
    echo "Skipping frontend codegen."
fi
