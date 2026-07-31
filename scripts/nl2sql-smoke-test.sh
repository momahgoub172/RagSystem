#!/usr/bin/env bash

# Exercises the NL2SQL endpoint with progressively more complex questions.
# Usage:
#   ./scripts/nl2sql-smoke-test.sh
#   API_BASE_URL=http://localhost:8080 ./scripts/nl2sql-smoke-test.sh

set -u

api_base_url="${API_BASE_URL:-${1:-http://localhost:5098}}"
api_base_url="${api_base_url%/}"
failures=0

if ! command -v curl >/dev/null; then
    echo "curl is required." >&2
    exit 2
fi

if ! command -v jq >/dev/null; then
    echo "jq is required to validate API responses." >&2
    exit 2
fi

run_query() {
    local name="$1"
    local question="$2"
    local response_with_status response status

    printf '\n== %s ==\n%s\n' "$name" "$question"

    if ! response_with_status=$(curl --silent --show-error \
        --connect-timeout 5 \
        --max-time 90 \
        --request POST "${api_base_url}/query" \
        --header 'Content-Type: application/json' \
        --data "$(printf '{\"question\":\"%s\"}' "$question")" \
        --write-out $'\n%{http_code}'); then
        echo "FAIL: could not reach ${api_base_url}/query" >&2
        failures=$((failures + 1))
        return
    fi

    status="${response_with_status##*$'\n'}"
    response="${response_with_status%$'\n'*}"

    if [[ ! "$status" =~ ^2[0-9][0-9]$ ]] || ! printf '%s' "$response" | jq -e \
        '.intent == "database" and (.answer | type == "string") and (.sql | type == "string")' \
        >/dev/null; then
        echo "FAIL: HTTP ${status}"
        printf '%s\n' "$response" | jq . 2>/dev/null || printf '%s\n' "$response"
        failures=$((failures + 1))
        return
    fi

    echo "PASS"
    printf '%s' "$response" | jq -r '"SQL:\n" + .sql + "\nAnswer:\n" + .answer'
}

run_query \
    "Above-average order value" \
    "Which customers have placed orders above the average order value?"

run_query \
    "Top orders per region" \
    "Show the top 3 highest-value orders per region"

run_query \
    "Month-over-month revenue" \
    "Compare total revenue this month versus last month"

run_query \
    "Regions with overdue-order threshold" \
    "Which regions have more than 50 overdue orders?"

run_query \
    "Overdue-order percentage by region" \
    "What percentage of orders are overdue, broken down by region?"

run_query \
    "Recent high-value orders" \
    "How many orders were placed in the last 90 days with a value over \$10,000?"

if (( failures > 0 )); then
    printf '\n%d query test(s) failed.\n' "$failures" >&2
    exit 1
fi

echo ""
echo "All six NL2SQL smoke tests passed."
