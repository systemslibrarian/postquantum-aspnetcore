#!/usr/bin/env bash
# Asserts the package version is identical across:
#   - src/PostQuantum.AspNetCore/PostQuantum.AspNetCore.csproj (<Version>)
#   - README.md          (the badged version in `dotnet add package …` examples)
#   - CHANGELOG.md       (the most recent non-Unreleased section header)
#
# Fails the build if any of these drift. A version-mismatched preview is a
# trust signal a crypto-adjacent package should never send.

set -euo pipefail

repo_root=$(git rev-parse --show-toplevel)
csproj="$repo_root/src/PostQuantum.AspNetCore/PostQuantum.AspNetCore.csproj"
readme="$repo_root/README.md"
changelog="$repo_root/CHANGELOG.md"

csproj_version=$(grep -oP '(?<=<Version>)[^<]+' "$csproj" | head -1)
readme_version=$(grep -oP 'PostQuantum\.AspNetCore --version \K[0-9A-Za-z.+-]+' "$readme" | head -1)
changelog_version=$(grep -oP '^## \[\K[0-9A-Za-z.+-]+(?=\])' "$changelog" \
                    | grep -v '^Unreleased$' \
                    | head -1)

fail=0
echo "csproj:    $csproj_version"
echo "README:    $readme_version"
echo "CHANGELOG: $changelog_version"

if [[ -z "$csproj_version" ]]; then
    echo "::error::csproj <Version> not found in $csproj"
    fail=1
fi

if [[ -z "$readme_version" ]]; then
    echo "::error::README install-version not found in $readme (expected 'PostQuantum.AspNetCore --version X.Y.Z')"
    fail=1
elif [[ "$readme_version" != "$csproj_version" ]]; then
    echo "::error::README version ($readme_version) != csproj version ($csproj_version)"
    fail=1
fi

if [[ -z "$changelog_version" ]]; then
    echo "::error::CHANGELOG most-recent release section not found in $changelog"
    fail=1
elif [[ "$changelog_version" != "$csproj_version" ]]; then
    echo "::error::CHANGELOG version ($changelog_version) != csproj version ($csproj_version)"
    fail=1
fi

if [[ $fail -ne 0 ]]; then
    exit 1
fi

echo "Version sync: OK ($csproj_version)"
