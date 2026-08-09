#!/usr/bin/env bash
set -euo pipefail

TARGETS_FILE="Directory.Build.targets"

# --- Prechecks ---
current_branch=$(git branch --show-current)
if [[ "$current_branch" != "develop" ]]; then
  echo "Error: must be on 'develop' branch (currently on '$current_branch')" >&2
  exit 1
fi

if ! git diff --quiet || ! git diff --cached --quiet; then
  echo "Error: working tree has uncommitted changes" >&2
  exit 1
fi

# --- Read current version ---
current_display=$(grep -oP '<ApplicationDisplayVersion>\K[^<]+' "$TARGETS_FILE")
current_int=$(grep -oP '<ApplicationVersion>\K[^<]+' "$TARGETS_FILE")

IFS='.' read -r major minor patch <<< "$current_display"
patch=$((patch + 1))
new_display="$major.$minor.$patch"
new_int=$((current_int + 1))

echo "Bumping version: $current_display ($current_int) -> $new_display ($new_int)"

# --- Write new version ---
sed -i "s|<ApplicationDisplayVersion>.*</ApplicationDisplayVersion>|<ApplicationDisplayVersion>$new_display</ApplicationDisplayVersion>|" "$TARGETS_FILE"
sed -i "s|<ApplicationVersion>.*</ApplicationVersion>|<ApplicationVersion>$new_int</ApplicationVersion>|" "$TARGETS_FILE"

# --- Commit version bump ---
git add "$TARGETS_FILE"
git commit -m "v$new_display"

# --- Merge to main ---
echo "Switching to main..."
git checkout main

echo "Resetting main to develop..."
git reset --hard develop

echo "Pushing main..."
git push origin main || echo "Warning: push failed (no internet?), continuing..."

echo "Switching back to develop..."
git checkout develop

echo "Done — main is now at v$new_display"
