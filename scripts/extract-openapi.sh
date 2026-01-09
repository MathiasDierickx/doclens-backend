#!/bin/bash
# Extract OpenAPI spec from running Azure Functions
# Usage: ./scripts/extract-openapi.sh [output-file]
#
# This script starts the Functions app, waits for it to be ready,
# fetches the OpenAPI spec, and saves it to a file.

set -e

OUTPUT_FILE="${1:-openapi.json}"
API_URL="http://localhost:7071/api/openapi/v3.json"
SWAGGER_URL="http://localhost:7071/api/swagger/ui"
MAX_WAIT=60
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"

echo "DocLens OpenAPI Spec Extractor"
echo "=============================="
echo ""

# Check if func is running already
if curl -s "$API_URL" > /dev/null 2>&1; then
    echo "Functions app is already running!"
    echo "Fetching OpenAPI spec..."
    curl -s "$API_URL" | jq '.' > "$OUTPUT_FILE"
    echo "Saved to: $OUTPUT_FILE"
    exit 0
fi

# Start Functions in background
echo "Starting Azure Functions..."
cd "$PROJECT_DIR/src/DocLens.Api"

# Kill any existing func processes
pkill -f "func host start" 2>/dev/null || true

# Start func in background
func start > /tmp/func-output.log 2>&1 &
FUNC_PID=$!

# Wait for startup
echo "Waiting for Functions to start (max ${MAX_WAIT}s)..."
WAITED=0
while ! curl -s "http://localhost:7071/api/health" > /dev/null 2>&1; do
    sleep 2
    WAITED=$((WAITED + 2))
    if [ $WAITED -ge $MAX_WAIT ]; then
        echo "ERROR: Functions did not start within ${MAX_WAIT} seconds"
        echo "Check /tmp/func-output.log for details"
        kill $FUNC_PID 2>/dev/null || true
        exit 1
    fi
    echo "  Waiting... (${WAITED}s)"
done

echo "Functions started!"
echo ""

# Fetch OpenAPI spec
echo "Fetching OpenAPI spec from $API_URL..."
if curl -s "$API_URL" | jq '.' > "$OUTPUT_FILE" 2>/dev/null; then
    echo "OpenAPI spec saved to: $OUTPUT_FILE"
    echo ""
    echo "Endpoints found:"
    jq -r '.paths | keys[]' "$OUTPUT_FILE" 2>/dev/null || echo "  (could not parse paths)"
else
    echo "ERROR: Failed to fetch OpenAPI spec"
    echo "Try visiting $SWAGGER_URL in your browser"
fi

# Cleanup
echo ""
echo "Stopping Functions..."
kill $FUNC_PID 2>/dev/null || true
pkill -f "func host start" 2>/dev/null || true

echo "Done!"
