# Ralph Loop for Clawd-Net

This folder contains a small automation loop for running Qwen against `docs/MISSION.txt` until all milestones in `docs/PLAN.md` are completed or a guard condition stops the loop.

## Files

- `ralph-loop.sh` — main loop runner
- `qwen_pretty_stream.py` — converts Qwen `stream-json` output into cleaner terminal messages
- `logs/qwen-stream/` — raw JSONL event logs produced by the renderer

## What the loop does

On each iteration, `ralph-loop.sh`:

1. Checks `~/projects/Clawd-Net/docs/PLAN.md` for open milestones matching `^### \[ \] `
2. Exits if there are no open milestones
3. Exits if the git working tree is dirty
4. Fetches remotes
5. Exits if the current branch is behind or diverged from upstream
6. Pushes local commits if the branch is ahead of upstream
7. Runs Qwen with:
   - `--yolo`
   - `--output-format stream-json`
   - `--include-partial-messages`
   - prompt content from `docs/MISSION.txt`
8. Pipes Qwen JSON events through `qwen_pretty_stream.py`

## Usage

Run from anywhere:

```bash
chmod +x ~/projects/Clawd-Net/ralph/qwen_pretty_stream.py
chmod +x ~/projects/Clawd-Net/ralph/ralph-loop.sh
~/projects/Clawd-Net/ralph/ralph-loop.sh --max-iterations 25