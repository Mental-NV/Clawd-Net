#!/usr/bin/env python3
"""
Simple stream renderer for Claude Code and Codex.

Provides best-effort progress rendering for providers that don't expose
structured stream-json events like Qwen does.
"""

import json
import sys
from typing import Any, Dict


RESET = "\033[0m"
DIM = "\033[2m"
GREEN = "\033[32m"
YELLOW = "\033[33m"
BLUE = "\033[34m"
CYAN = "\033[36m"

TTY = sys.stdout.isatty()


def color(text: str, code: str) -> str:
    """Apply ANSI color if TTY."""
    if not TTY:
        return text
    return f"{code}{text}{RESET}"


def render_stream_json_event(event: Dict[str, Any]) -> None:
    """Render a stream-json event (Claude Code format)."""
    event_type = event.get("type")

    if event_type == "text":
        # Assistant text output
        text = event.get("text", "")
        if text:
            print(text, end="", flush=True)

    elif event_type == "tool_use":
        # Tool invocation
        tool_name = event.get("name", "unknown")
        print(f"\n{color(f'→ {tool_name}', CYAN)}", flush=True)

    elif event_type == "tool_result":
        # Tool result
        is_error = event.get("is_error", False)
        if is_error:
            print(f"{color('✗ Tool failed', YELLOW)}", flush=True)
        else:
            print(f"{color('✓ Tool completed', GREEN)}", flush=True)

    elif event_type == "message_start":
        # Message starting
        pass

    elif event_type == "message_stop":
        # Message complete
        print("\n", flush=True)


def render_line(line: str) -> None:
    """Render a plain text line (Codex format)."""
    # Codex doesn't have structured output, just print lines
    print(line, flush=True)


def main():
    """Main entry point."""
    try:
        for line in sys.stdin:
            line = line.rstrip('\n')
            if not line:
                continue

            # Try to parse as JSON (Claude Code stream-json)
            try:
                event = json.loads(line)
                render_stream_json_event(event)
            except json.JSONDecodeError:
                # Not JSON, treat as plain text (Codex)
                render_line(line)

    except KeyboardInterrupt:
        print(f"\n{color('Interrupted', YELLOW)}", file=sys.stderr)
        sys.exit(130)
    except BrokenPipeError:
        # Pipe closed, exit gracefully
        sys.exit(0)


if __name__ == '__main__':
    main()
