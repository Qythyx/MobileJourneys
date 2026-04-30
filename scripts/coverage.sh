#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

rm -rf TestResults coverage

dotnet tool restore

dotnet test \
	--configuration Release \
	--results-directory TestResults \
	--coverage \
	--coverage-output-format cobertura

dotnet reportgenerator \
	"-reports:TestResults/**/*.cobertura.xml" \
	"-targetdir:coverage" \
	"-reporttypes:Html;Cobertura;MarkdownSummaryGithub"

echo
echo "Coverage report: $(pwd)/coverage/index.html"

if [[ "${OSTYPE:-}" == "darwin"* ]] && [[ -z "${CI:-}" ]]; then
	open coverage/index.html
fi
