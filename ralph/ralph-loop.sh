#!/usr/bin/env bash
set -euo pipefail

PROJECT_DIR="${HOME}/projects/Clawd-Net"
RALPH_DIR="${PROJECT_DIR}/ralph"
PLAN_FILE="${PROJECT_DIR}/docs/PLAN.md"
MISSION_FILE="${PROJECT_DIR}/docs/MISSION.txt"
RENDERER_FILE="${RALPH_DIR}/qwen_pretty_stream.py"
RAW_LOG_DIR="${RALPH_DIR}/logs/qwen-stream"
MAX_ITERATIONS=10

usage() {
  cat <<EOF
Usage: $0 [--max-iterations N]

Options:
  --max-iterations N   Maximum number of loop iterations (default: 10)
  -h, --help           Show this help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --max-iterations)
      if [[ $# -lt 2 ]]; then
        echo "Error: --max-iterations requires a value."
        usage
        exit 1
      fi
      MAX_ITERATIONS="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Error: unknown argument: $1"
      usage
      exit 1
      ;;
  esac
done

if ! [[ "${MAX_ITERATIONS}" =~ ^[0-9]+$ ]] || [[ "${MAX_ITERATIONS}" -le 0 ]]; then
  echo "Error: --max-iterations must be a positive integer."
  exit 1
fi

cd "${PROJECT_DIR}"

if ! git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  echo "Error: ${PROJECT_DIR} is not a git repository."
  exit 1
fi

if [[ ! -f "${PLAN_FILE}" ]]; then
  echo "Error: PLAN file not found: ${PLAN_FILE}"
  exit 1
fi

if [[ ! -f "${MISSION_FILE}" ]]; then
  echo "Error: mission file not found: ${MISSION_FILE}"
  exit 1
fi

if [[ ! -f "${RENDERER_FILE}" ]]; then
  echo "Error: renderer file not found: ${RENDERER_FILE}"
  exit 1
fi

current_branch() {
  git symbolic-ref --quiet --short HEAD
}

open_milestone_count() {
  grep -E -c '^### \[ \] ' "${PLAN_FILE}" || true
}

ensure_clean_worktree() {
  if [[ -n "$(git status --porcelain)" ]]; then
    echo "Working tree is not clean. Exiting."
    git status --short
    exit 1
  fi
}

check_sync_and_push_if_needed() {
  local branch upstream ahead_count behind_count counts

  branch="$(current_branch)" || {
    echo "Detached HEAD is not supported. Exiting."
    exit 1
  }

  echo "Fetching remotes..."
  git fetch --prune

  if git rev-parse --abbrev-ref --symbolic-full-name '@{u}' >/dev/null 2>&1; then
    upstream="$(git rev-parse --abbrev-ref --symbolic-full-name '@{u}')"
    counts="$(git rev-list --left-right --count "${upstream}...HEAD")"
    behind_count="$(awk '{print $1}' <<< "${counts}")"
    ahead_count="$(awk '{print $2}' <<< "${counts}")"

    if [[ "${behind_count}" -gt 0 && "${ahead_count}" -gt 0 ]]; then
      echo "Branch ${branch} has diverged from ${upstream} (behind: ${behind_count}, ahead: ${ahead_count}). Exiting."
      exit 1
    fi

    if [[ "${behind_count}" -gt 0 ]]; then
      echo "Branch ${branch} is behind ${upstream} by ${behind_count} commit(s). Exiting."
      echo "Pull/rebase the branch first."
      exit 1
    fi

    if [[ "${ahead_count}" -gt 0 ]]; then
      echo "Found ${ahead_count} unpushed commit(s) on ${branch}. Pushing..."
      git push
    else
      echo "Branch ${branch} is in sync with ${upstream}."
    fi

    return
  fi

  if git remote get-url origin >/dev/null 2>&1; then
    if git show-ref --verify --quiet "refs/remotes/origin/${branch}"; then
      counts="$(git rev-list --left-right --count "origin/${branch}...HEAD")"
      behind_count="$(awk '{print $1}' <<< "${counts}")"
      ahead_count="$(awk '{print $2}' <<< "${counts}")"

      if [[ "${behind_count}" -gt 0 && "${ahead_count}" -gt 0 ]]; then
        echo "Branch ${branch} has diverged from origin/${branch} (behind: ${behind_count}, ahead: ${ahead_count}). Exiting."
        exit 1
      fi

      if [[ "${behind_count}" -gt 0 ]]; then
        echo "Branch ${branch} is behind origin/${branch} by ${behind_count} commit(s). Exiting."
        echo "Pull/rebase the branch first."
        exit 1
      fi

      if [[ "${ahead_count}" -gt 0 ]]; then
        echo "No upstream set, but ${branch} is ahead of origin/${branch}. Pushing and setting upstream..."
        git push -u origin "${branch}"
      else
        echo "No upstream set for ${branch}. Binding it to origin/${branch}."
        git branch --set-upstream-to="origin/${branch}" "${branch}" >/dev/null 2>&1 || true
      fi
    else
      echo "No upstream exists for ${branch}. Pushing and setting upstream to origin/${branch}..."
      git push -u origin "${branch}"
    fi
  else
    echo "No upstream and no origin remote. Cannot push safely. Exiting."
    exit 1
  fi
}

iteration=0

mkdir -p "${RAW_LOG_DIR}"

while true; do
  iteration=$((iteration + 1))

  if [[ "${iteration}" -gt "${MAX_ITERATIONS}" ]]; then
    echo "Reached max iterations (${MAX_ITERATIONS}). Exiting."
    exit 0
  fi

  remaining="$(open_milestone_count)"

  if [[ "${remaining}" -eq 0 ]]; then
    echo "All milestones are completed. Exiting."
    exit 0
  fi

  echo
  echo "=== Iteration ${iteration}/${MAX_ITERATIONS} ==="
  echo "Open milestones: ${remaining}"

  ensure_clean_worktree
  check_sync_and_push_if_needed

  echo "Launching qwen (stream-json, partial messages)..."
  qwen --yolo \
    --output-format stream-json \
    --include-partial-messages \
    --prompt "$(cat "${MISSION_FILE}")" \
  | python3 -u "${RENDERER_FILE}" \
      --mode normal \
      --raw-log-dir "${RAW_LOG_DIR}"
done
