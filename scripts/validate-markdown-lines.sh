#!/usr/bin/env bash
set -u

MAX_LEN=150
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CHECKED=0
VIOLATIONS=0

check_file() {
  local file="$1" rel line len
  rel="${file#"$ROOT"/}"
  CHECKED=$((CHECKED + 1))
  while IFS=: read -r line len; do
    printf '%s:%s: line is %s characters (max %s)\n' "$rel" "$line" "$len" "$MAX_LEN"
    VIOLATIONS=$((VIOLATIONS + 1))
  done < <(awk -v max="$MAX_LEN" 'length($0) > max { print NR ":" length($0) }' "$file")
}

if [ "$#" -gt 0 ]; then
  FILES=()
  for arg in "$@"; do
    if [[ "$arg" == /* ]]; then
      path="$arg"
    else
      path="$ROOT/$arg"
    fi
    if [ -d "$path" ]; then
      while IFS= read -r -d '' f; do FILES+=("$f"); done \
        < <(find "$path" -type f -name '*.md' -print0)
    elif [ -f "$path" ]; then
      FILES+=("$path")
    else
      printf 'ERROR: not found: %s\n' "$arg" >&2
      exit 2
    fi
  done
else
  FILES=("$ROOT/AGENTS.md")
  while IFS= read -r -d '' f; do FILES+=("$f"); done \
    < <(find "$ROOT/agents-doc" -type f -name '*.md' -print0)
fi

for file in "${FILES[@]}"; do
  check_file "$file"
done

if [ "$VIOLATIONS" -gt 0 ]; then
  printf 'FAIL: %s line(s) too long across %s file(s). Wrap them and re-run.\n' \
    "$VIOLATIONS" "$CHECKED" >&2
  exit 1
fi
printf 'OK: %s markdown file(s) checked, every line under %s characters.\n' "$CHECKED" "$MAX_LEN"