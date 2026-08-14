#!/usr/bin/env bash
set -euo pipefail

# Squash-merges the current feature branch into develop.
#
# Flow:
#   1. Prechecks: must be on a feature branch (not main/develop) with a clean
#      working tree.
#   2. Locate the worktree where 'develop' is checked out — git refuses to
#      check out a branch used by another worktree — and sync that worktree's
#      local develop with origin/develop via a safe fast-forward (in a plain
#      single-worktree repo a fetch does the sync instead). Local develop is the
#      base of truth (solo-dev: merge to your machine first), so any local
#      commits beyond origin/develop are kept and pushed together with this
#      merge.
#   3. Rebase the current branch onto local develop.
#   4. If the rebase hits conflicts, the branch is left mid-rebase for the user
#      to resolve (git rebase --continue / git rebase --abort). No merge happens.
#   5. If the rebase is clean, squash all the branch's commits into one commit on
#      develop, doing the checkout/merge/commit/push in the worktree that owns
#      'develop' (a failed push is a warning, not an error; use --no-push to
#      merge locally only).
#
# Usage: scripts/merge.sh ["message"] [--no-push]
#   An optional single message sets the squash commit subject (default: built
#   from the branch's commit subjects). --no-push may appear in any position.

DEVELOP="develop"

MESSAGE=""
PUSH=1
for arg in "$@"; do
  case "$arg" in
    --no-push)
      PUSH=0
      ;;
    --help|-h)
      echo "Usage: scripts/merge.sh [\"message\"] [--no-push]" >&2
      exit 0
      ;;
    --*)
      echo "Error: unknown argument '$arg' (expected an optional message and/or --no-push)" >&2
      exit 1
      ;;
    *)
      MESSAGE="$arg"
      ;;
  esac
done

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

# --- Locate the worktree that owns 'develop' ---
dev_wt=""
while IFS= read -r line; do
  if [[ "$line" == worktree\ * ]]; then
    entry_wt="${line#worktree }"
  elif [[ "$line" == "branch refs/heads/$DEVELOP" ]]; then
    dev_wt="$entry_wt"
  fi
done < <(git worktree list --porcelain)

# --- Develop worktree sanity + sync ---
if [[ -n "$dev_wt" ]]; then
  if ! git -C "$dev_wt" diff --quiet || ! git -C "$dev_wt" diff --cached --quiet; then
    echo "Error: '$DEVELOP' worktree ($dev_wt) has uncommitted changes" >&2
    exit 1
  fi
  if [[ "$(git -C "$dev_wt" rev-parse HEAD 2>/dev/null)" != "$(git -C "$dev_wt" rev-parse "$DEVELOP" 2>/dev/null)" ]]; then
    echo "Error: '$DEVELOP' worktree ($dev_wt) is detached or mid-rebase/merge — resolve it first" >&2
    exit 1
  fi

  if git remote get-url origin >/dev/null 2>&1; then
    if git -C "$dev_wt" fetch origin "$DEVELOP" 2>/dev/null; then
      if ! git -C "$dev_wt" merge --ff-only "origin/$DEVELOP" >/dev/null 2>&1; then
        if [[ "$(git rev-list --count "origin/$DEVELOP..$DEVELOP" 2>/dev/null || echo 0)" -gt 0 ]]; then
          echo "Note: local '$DEVELOP' is ahead of origin/$DEVELOP — merging onto local '$DEVELOP'."
        fi
      fi
    else
      echo "Warning: could not fetch origin/$DEVELOP — using local state." >&2
    fi
  fi
else
  if git remote get-url origin >/dev/null 2>&1; then
    if git fetch origin "$DEVELOP" 2>/dev/null; then
      if ! git fetch origin "$DEVELOP:$DEVELOP" 2>/dev/null; then
        if [[ "$(git rev-list --count "origin/$DEVELOP..$DEVELOP" 2>/dev/null || echo 0)" -gt 0 ]]; then
          echo "Note: local '$DEVELOP' is ahead of origin/$DEVELOP — merging onto local '$DEVELOP'."
        fi
      fi
    else
      echo "Warning: could not fetch origin/$DEVELOP — using local state." >&2
    fi
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

# --- Build squash commit message ---
if [[ -n "$MESSAGE" ]]; then
  msg="$MESSAGE"
else
  lines=$(git log --format='%s' --reverse "$DEVELOP..$current_branch")
  subject=$(echo "$lines" | head -1)
  rest=$(echo "$lines" | tail -n +2)
  if [[ -n "$rest" ]]; then
    msg="$subject

$rest"
  else
    msg="$subject"
  fi
fi

# --- Squash-merge into develop (in the worktree that owns it) ---
echo "Rebase clean ($to_merge commit(s)). Squashing into $DEVELOP..."
if [[ -n "$dev_wt" ]]; then
  git -C "$dev_wt" checkout -q "$DEVELOP"
  git -C "$dev_wt" merge --squash "$current_branch"
else
  git checkout -q "$DEVELOP"
  git merge --squash "$current_branch"
fi

if [[ -n "$dev_wt" ]]; then
  git -C "$dev_wt" commit -m "$msg"
else
  git commit -m "$msg"
  git checkout -q "$current_branch"
fi

echo "Squashed $to_merge commit(s) onto $DEVELOP."
if [[ "$PUSH" -eq 1 ]]; then
  if [[ -n "$dev_wt" ]]; then
    if git -C "$dev_wt" push origin "$DEVELOP"; then
      echo "Pushed $DEVELOP to origin."
    else
      echo "Warning: push failed — the squash commit is local on $DEVELOP" >&2
    fi
  else
    if git push origin "$DEVELOP"; then
      echo "Pushed $DEVELOP to origin."
    else
      echo "Warning: push failed — the squash commit is local on $DEVELOP" >&2
    fi
  fi
else
  echo "Skipped push (--no-push) — $DEVELOP has local-only commits."
fi

if [[ -n "$dev_wt" ]]; then
  echo "Done. You are on '$current_branch' (the '$DEVELOP' worktree was updated: $dev_wt)."
else
  echo "Done. You are back on '$current_branch' (committed onto local '$DEVELOP')."
fi
echo "Delete the feature branch with: git branch -D $current_branch"