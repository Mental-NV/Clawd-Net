#!/usr/bin/env python3
"""
Ralph Loop - JSON-driven orchestration for ClawdNet execution backlog.

Replaces ralph-loop.sh with deterministic, validated JSON-based orchestration.
"""

import argparse
import json
import os
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional


class BacklogOrchestrator:
    """Orchestrates execution of backlog items."""

    def __init__(self, repo_root: Path, auto_push: bool = False, dry_run: bool = False):
        self.repo_root = repo_root
        self.auto_push = auto_push
        self.dry_run = dry_run
        self.backlog_path = repo_root / "docs" / "backlog.json"
        self.lock_path = repo_root / ".ralph-loop.lock"

    def load_backlog(self) -> Dict[str, Any]:
        """Load and parse backlog.json."""
        try:
            with open(self.backlog_path, 'r', encoding='utf-8') as f:
                return json.load(f)
        except FileNotFoundError:
            self.error(f"Backlog not found: {self.backlog_path}")
        except json.JSONDecodeError as e:
            self.error(f"Invalid JSON in backlog: {e}")

    def save_backlog(self, backlog: Dict[str, Any]) -> None:
        """Save backlog.json atomically."""
        if self.dry_run:
            print("[DRY RUN] Would save backlog.json")
            return

        # Write to temp file first
        temp_path = self.backlog_path.with_suffix('.json.tmp')
        try:
            with open(temp_path, 'w', encoding='utf-8') as f:
                json.dump(backlog, f, indent=2, ensure_ascii=False)
                f.write('\n')
            # Atomic rename
            temp_path.replace(self.backlog_path)
        except Exception as e:
            if temp_path.exists():
                temp_path.unlink()
            self.error(f"Failed to save backlog: {e}")

    def validate_backlog(self) -> bool:
        """Run backlog_validator.py. Returns True if valid."""
        validator_path = self.repo_root / "ralph" / "backlog_validator.py"
        if not validator_path.exists():
            self.error(f"Validator not found: {validator_path}")

        result = subprocess.run(
            [sys.executable, str(validator_path)],
            capture_output=True,
            text=True
        )

        if result.returncode != 0:
            print("Backlog validation failed:", file=sys.stderr)
            print(result.stderr, file=sys.stderr)
            return False

        return True

    def acquire_lock(self) -> bool:
        """Acquire execution lock. Returns True if acquired."""
        if self.lock_path.exists():
            # Check if lock is stale (older than 1 hour)
            lock_age = time.time() - self.lock_path.stat().st_mtime
            if lock_age > 3600:
                print(f"Removing stale lock (age: {lock_age:.0f}s)")
                self.lock_path.unlink()
            else:
                return False

        try:
            self.lock_path.write_text(f"{os.getpid()}\n{datetime.now(timezone.utc).isoformat()}\n")
            return True
        except Exception as e:
            print(f"Failed to acquire lock: {e}", file=sys.stderr)
            return False

    def release_lock(self) -> None:
        """Release execution lock."""
        if self.lock_path.exists():
            self.lock_path.unlink()

    def check_git_clean(self) -> bool:
        """Check if git working tree is clean."""
        result = subprocess.run(
            ["git", "status", "--porcelain"],
            cwd=self.repo_root,
            capture_output=True,
            text=True
        )
        return result.returncode == 0 and not result.stdout.strip()

    def git_fetch(self) -> bool:
        """Run git fetch. Returns True on success."""
        if self.dry_run:
            print("[DRY RUN] Would run: git fetch")
            return True

        result = subprocess.run(
            ["git", "fetch"],
            cwd=self.repo_root,
            capture_output=True,
            text=True
        )
        if result.returncode != 0:
            print(f"git fetch failed: {result.stderr}", file=sys.stderr)
            return False
        return True

    def git_push(self) -> bool:
        """Run git push. Returns True on success."""
        if self.dry_run:
            print("[DRY RUN] Would run: git push")
            return True

        result = subprocess.run(
            ["git", "push"],
            cwd=self.repo_root,
            capture_output=True,
            text=True
        )
        if result.returncode != 0:
            print(f"git push failed: {result.stderr}", file=sys.stderr)
            return False
        return True

    def select_next_item(self, backlog: Dict[str, Any]) -> Optional[Dict[str, Any]]:
        """
        Select next item to execute based on:
        1. Status is 'todo'
        2. All dependencies are 'done'
        3. Lowest priority number (P0 < P1 < P2 < P3)
        4. Lowest order value (tiebreaker)

        Returns None if no eligible item found.
        """
        items = backlog.get('items', [])
        item_map = {item['id']: item for item in items}

        # Filter to todo items with satisfied dependencies
        eligible = []
        for item in items:
            if item['status'] != 'todo':
                continue

            # Check dependencies
            depends_on = item.get('dependsOn', [])
            deps_satisfied = all(
                item_map.get(dep_id, {}).get('status') == 'done'
                for dep_id in depends_on
            )

            if deps_satisfied:
                eligible.append(item)

        if not eligible:
            return None

        # Sort by priority (P0=0, P1=1, P2=2, P3=3), then by order
        priority_map = {'P0': 0, 'P1': 1, 'P2': 2, 'P3': 3}
        eligible.sort(key=lambda x: (priority_map.get(x['priority'], 99), x['order']))

        return eligible[0]

    def mark_item_started(self, backlog: Dict[str, Any], item_id: str) -> None:
        """Mark item as in_progress and set startedAt timestamp."""
        items = backlog.get('items', [])
        for item in items:
            if item['id'] == item_id:
                item['status'] = 'in_progress'
                item['startedAt'] = datetime.now(timezone.utc).isoformat()
                break

    def mark_item_done(self, backlog: Dict[str, Any], item_id: str) -> None:
        """Mark item as done and set completedAt timestamp."""
        items = backlog.get('items', [])
        for item in items:
            if item['id'] == item_id:
                item['status'] = 'done'
                item['completedAt'] = datetime.now(timezone.utc).isoformat()
                break

    def run_validation_commands(self, item: Dict[str, Any]) -> bool:
        """Run validation commands for item. Returns True if all pass."""
        validation = item.get('validation', {})
        commands = validation.get('commands', [])

        if not commands:
            print("No validation commands defined, skipping validation")
            return True

        print(f"Running {len(commands)} validation command(s)...")

        for i, cmd in enumerate(commands, 1):
            print(f"  [{i}/{len(commands)}] {cmd}")

            if self.dry_run:
                print("  [DRY RUN] Would run command")
                continue

            result = subprocess.run(
                cmd,
                shell=True,
                cwd=self.repo_root,
                capture_output=True,
                text=True
            )

            if result.returncode != 0:
                print(f"  FAILED (exit code {result.returncode})", file=sys.stderr)
                if result.stdout:
                    print("  stdout:", result.stdout[:500], file=sys.stderr)
                if result.stderr:
                    print("  stderr:", result.stderr[:500], file=sys.stderr)
                return False

            print(f"  PASSED")

        return True

    def execute_item(self, item: Dict[str, Any]) -> bool:
        """
        Execute a backlog item by invoking clawdnet with appropriate prompt.
        Returns True if execution succeeded.
        """
        item_id = item['id']
        title = item['title']

        print(f"\n{'='*80}")
        print(f"Executing: {title}")
        print(f"ID: {item_id}")
        print(f"Priority: {item['priority']}")
        print(f"{'='*80}\n")

        # Build execution prompt
        prompt = self.build_execution_prompt(item)

        if self.dry_run:
            print("[DRY RUN] Would execute with prompt:")
            print(prompt[:500] + "..." if len(prompt) > 500 else prompt)
            return True

        # Invoke clawdnet (or qwen for now during transition)
        # TODO: Replace with actual clawdnet invocation once integrated
        print("Execution would invoke clawdnet here")
        print("For now, this is a placeholder")

        return True

    def build_execution_prompt(self, item: Dict[str, Any]) -> str:
        """Build execution prompt for item."""
        lines = [
            f"Execute backlog item: {item['title']}",
            "",
            f"**Why:** {item.get('why', 'No rationale provided')}",
            "",
            "**Deliverables:**"
        ]

        for deliverable in item.get('deliverables', []):
            status = "✓" if deliverable.get('done') else "○"
            lines.append(f"  {status} {deliverable['text']}")

        lines.append("")
        lines.append("**Exit Criteria:**")

        for criterion in item.get('exitCriteria', []):
            status = "✓" if criterion.get('done') else "○"
            lines.append(f"  {status} {criterion['text']}")

        if item.get('risks'):
            lines.append("")
            lines.append("**Risks:**")
            for risk in item['risks']:
                lines.append(f"  - {risk}")

        validation = item.get('validation', {})
        if validation.get('commands'):
            lines.append("")
            lines.append("**Validation Commands:**")
            for cmd in validation['commands']:
                lines.append(f"  - {cmd}")

        lines.append("")
        lines.append("Please implement this milestone following the deliverables and exit criteria.")

        return "\n".join(lines)

    def run_loop(self, max_iterations: Optional[int] = None) -> int:
        """
        Main execution loop.
        Returns exit code: 0 if all items done, 1 on error, 2 if work remains.
        """
        iteration = 0

        while True:
            iteration += 1
            if max_iterations and iteration > max_iterations:
                print(f"\nReached max iterations ({max_iterations}), stopping")
                return 2

            print(f"\n{'='*80}")
            print(f"Ralph Loop - Iteration {iteration}")
            print(f"{'='*80}")

            # Validate backlog
            if not self.validate_backlog():
                return 1

            # Load backlog
            backlog = self.load_backlog()

            # Select next item
            next_item = self.select_next_item(backlog)

            if not next_item:
                # Check if any items are in_progress or ready_for_validation
                items = backlog.get('items', [])
                active = [i for i in items if i['status'] in ('in_progress', 'ready_for_validation')]

                if active:
                    print("\nNo new items to start, but active items remain:")
                    for item in active:
                        print(f"  - {item['id']}: {item['status']}")
                    return 2

                # Check if all items are done
                todo_count = sum(1 for i in items if i['status'] == 'todo')
                done_count = sum(1 for i in items if i['status'] == 'done')

                if todo_count == 0:
                    print(f"\n✓ All items complete! ({done_count} done)")
                    return 0
                else:
                    print(f"\nNo eligible items to execute ({todo_count} todo, {done_count} done)")
                    print("Remaining items may have unsatisfied dependencies or be blocked")
                    return 2

            # Mark item as started
            self.mark_item_started(backlog, next_item['id'])
            self.save_backlog(backlog)

            # Execute item
            success = self.execute_item(next_item)

            if not success:
                print(f"\nExecution failed for {next_item['id']}", file=sys.stderr)
                return 1

            # Run validation
            if not self.run_validation_commands(next_item):
                print(f"\nValidation failed for {next_item['id']}", file=sys.stderr)
                return 1

            # Mark item as done
            backlog = self.load_backlog()  # Reload in case of external changes
            self.mark_item_done(backlog, next_item['id'])
            self.save_backlog(backlog)

            print(f"\n✓ Completed: {next_item['title']}")

            # Git push if auto-push enabled
            if self.auto_push:
                if not self.check_git_clean():
                    print("Working tree has uncommitted changes, skipping push")
                elif not self.git_push():
                    print("Warning: git push failed", file=sys.stderr)

    def error(self, msg: str) -> None:
        """Print error and exit."""
        print(f"Error: {msg}", file=sys.stderr)
        sys.exit(1)


def main():
    parser = argparse.ArgumentParser(
        description="Ralph Loop - JSON-driven orchestration for ClawdNet backlog"
    )
    parser.add_argument(
        '--auto-push',
        action='store_true',
        help='Automatically push to remote after each completed item'
    )
    parser.add_argument(
        '--dry-run',
        action='store_true',
        help='Show what would be done without making changes'
    )
    parser.add_argument(
        '--max-iterations',
        type=int,
        metavar='N',
        help='Maximum number of loop iterations (for testing)'
    )
    parser.add_argument(
        '--validate-only',
        action='store_true',
        help='Only validate backlog.json and exit'
    )
    parser.add_argument(
        '--show-next',
        action='store_true',
        help='Show next item that would be executed and exit'
    )

    args = parser.parse_args()

    # Find repo root
    repo_root = Path(__file__).parent.parent

    orchestrator = BacklogOrchestrator(
        repo_root=repo_root,
        auto_push=args.auto_push,
        dry_run=args.dry_run
    )

    # Validate-only mode
    if args.validate_only:
        if orchestrator.validate_backlog():
            print("✓ Backlog is valid")
            sys.exit(0)
        else:
            sys.exit(1)

    # Show-next mode
    if args.show_next:
        if not orchestrator.validate_backlog():
            sys.exit(1)

        backlog = orchestrator.load_backlog()
        next_item = orchestrator.select_next_item(backlog)

        if next_item:
            print(f"Next item: {next_item['id']}")
            print(f"  Title: {next_item['title']}")
            print(f"  Priority: {next_item['priority']}")
            print(f"  Order: {next_item['order']}")
        else:
            print("No eligible items to execute")

        sys.exit(0)

    # Check git status
    if not orchestrator.check_git_clean():
        print("Error: Working tree is not clean. Commit or stash changes first.", file=sys.stderr)
        sys.exit(1)

    # Acquire lock
    if not orchestrator.acquire_lock():
        print("Error: Another ralph-loop instance is running (lock exists)", file=sys.stderr)
        sys.exit(1)

    try:
        # Git fetch
        if not orchestrator.git_fetch():
            sys.exit(1)

        # Run main loop
        exit_code = orchestrator.run_loop(max_iterations=args.max_iterations)
        sys.exit(exit_code)

    finally:
        orchestrator.release_lock()


if __name__ == '__main__':
    main()
