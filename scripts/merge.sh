#!/usr/bin/env bash
set -euo pipefail

# Squash-merges the current feature branch into develop.
#
# Flow:
#   1. Prechecks: must be on a feature branch (not main/develop) with a clean
#      working tree.
#   2. Fetch origin/develop; fast-forward local develop to it only when safe.
#      Local develop is the base of truth (solo-dev: merge to your machine
#      first), so any local commits beyond origin/develop are kept and pushed
#      together with this merge.
#   3. Rebase the current branch onto local develop.
#   4. If the rebase hits conflicts, the branch is left mid-rebase for the user
#      to resolve (git rebase --continue / git rebase --abort). No merge happens.
#   5. If the rebase is clean, squash all the branch's commits into one commit on
#      develop and push it (a failed push is a warning, not an error; use
#      --no-push to merge locally only).

DEVELOP="develop"

PUSH=1
if [[ "${1:-}" == "--no-push" ]]; then
  PUSH=0
fi

# --- Prechecks ---
current_branch=$(git branch --show-current)
if [[ -z "$current_branch" ]]; then
  echo "Error: detached HEAD — checkout a feature branch first" >&2
  exit 1
fi

if [[ "$current_branch" == "main" || "$current_branch" == "$DEVELOP" ]]; then
  echo "Error: must be on a feature branch (currently on '$current_branch')" >&2
  exit 1
fi

if ! git diff --quiet || ! git diff --cached --quiet; then
  echo "Error: working tree has uncommitted changes" >&2
  exit 1
fi

if ! git rev-parse --verify --quiet "$DEVELOP" >/dev/null 2>&1; then
  echo "Error: no local '$DEVELOP' branch" >&2
  exit 1
fi

# --- Sync local develop with origin (safe fast-forward only) ---
if git remote get-url origin >/dev/null 2>&1; then
  if git fetch origin "$DEVELOP" 2>/dev/null; then
    # Fast-forward local develop to origin/develop only when it is an ancestor,
    # so remote-only commits are not missed. If local develop is ahead of origin
    # (local merges done on this machine), it is kept — later merges build on it.
    if ! git fetch origin "$DEVELOP:$DEVELOP" 2>/dev/null; then
      if [[ "$(git rev-list --count "origin/$DEVELOP..$DEVELOP" 2>/dev/null || echo 0)" -gt 0 ]]; then
        echo "Note: local '$DEVELOP' is ahead of origin/$DEVELOP — merging onto local '$DEVELOP'."
      fi
    fi
  else
    echo "Warning: could not fetch origin/$DEVELOP — using local state." >&2
  fi
fi

# --- Rebase onto local develop ---
echo "Rebasing '$current_branch' onto $DEVELOP..."
if ! git rebase "$DEVELOP"; then
  echo "Rebase stopped with conflicts in '$current_branch'. Finish it yourself:" >&2
  echo "  git rebase --continue   # after resolving conflicts" >&2
  echo "or abandon with: git rebase --abort" >&2
  exit 1
fi

to_merge=$(git rev-list --count "$DEVELOP..HEAD")
if [[ "$to_merge" -eq 0 ]]; then
  echo "Nothing to merge — '$current_branch' has no commits beyond $DEVELOP."
  exit 0
fi

# --- Squash-merge into develop ---
echo "Rebase clean ($to_merge commit(s)). Squashing into $DEVELOP..."
git checkout "$DEVELOP"

git merge --squash "$current_branch"

lines=$(git log --format='%s' --reverse "$DEVELOP..$current_branch")
subject=$(echo "$lines" | head -1)
rest=$(echo "$lines" | tail -n +2)
if [[ -n "$rest" ]]; then
  msg="$subject

$rest"
else
  msg="$subject"
fi
git commit -m "$msg"

echo "Squashed $to_merge commit(s) onto $DEVELOP."
if [[ "$PUSH" -eq 1 ]]; then
  if git push origin "$DEVELOP"; then
    echo "Pushed $DEVELOP to origin."
  else
    echo "Warning: push failed — the squash commit is local on $DEVELOP" >&2
  fi
else
  echo "Skipped push (--no-push) — $DEVELOP has local-only commits."
fi

echo "Done. On '$DEVELOP'; you can delete the feature branch with: git branch -D $current_branch"
