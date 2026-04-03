#!/usr/bin/env python3
import argparse
import datetime as dt
import json
import re
import sys
import time
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional

RESET = "\033[0m"
DIM = "\033[2m"
RED = "\033[31m"
GREEN = "\033[32m"
YELLOW = "\033[33m"
BLUE = "\033[34m"
MAGENTA = "\033[35m"
CYAN = "\033[36m"

TTY = sys.stdout.isatty()


def color(text: str, code: str) -> str:
    if not TTY:
        return text
    return f"{code}{text}{RESET}"


def truncate(text: str, limit: int = 140) -> str:
    text = re.sub(r"\s+", " ", text).strip()
    if len(text) <= limit:
        return text
    return text[: limit - 1] + "…"


def normalize_ws(text: str) -> str:
    text = text.replace("\r\n", "\n").replace("\r", "\n")
    lines = [re.sub(r"[ \t]+", " ", line).strip() for line in text.split("\n")]
    return "\n".join(line for line in lines if line)


def walk(node: Any) -> Iterable[Any]:
    yield node
    if isinstance(node, dict):
        for value in node.values():
            for child in walk(value):
                yield child
    elif isinstance(node, list):
        for item in node:
            for child in walk(item):
                yield child


def collect_strings_by_keys(node: Any, keys: set) -> List[str]:
    found: List[str] = []

    def _walk(obj: Any) -> None:
        if isinstance(obj, dict):
            for k, v in obj.items():
                if k in keys:
                    text = stringify_value(v)
                    if text:
                        found.append(text)
                _walk(v)
        elif isinstance(obj, list):
            for item in obj:
                _walk(item)

    _walk(node)
    return found


def stringify_value(value: Any, max_items: int = 4) -> Optional[str]:
    if value is None:
        return None

    if isinstance(value, str):
        text = value.strip()
        return text or None

    if isinstance(value, (int, float, bool)):
        return str(value)

    if isinstance(value, list):
        parts: List[str] = []
        for item in value[:max_items]:
            text = stringify_value(item, max_items=max_items)
            if text:
                parts.append(text)
        if not parts:
            return None
        suffix = ", …" if len(value) > max_items else ""
        return ", ".join(parts) + suffix

    if isinstance(value, dict):
        for key in (
            "text",
            "path",
            "file_path",
            "filepath",
            "directory",
            "dir",
            "command",
            "query",
            "pattern",
            "name",
            "prompt",
            "task",
            "description",
        ):
            if key in value:
                text = stringify_value(value.get(key), max_items=max_items)
                if text:
                    return text
        return None

    return None


def first_non_empty_from_sources(sources: List[Dict[str, Any]], *keys: str) -> Optional[str]:
    for source in sources:
        for key in keys:
            if key in source:
                text = stringify_value(source.get(key))
                if text:
                    return text
    return None


def tool_sources(item: Dict[str, Any]) -> List[Dict[str, Any]]:
    sources: List[Dict[str, Any]] = []
    for key in ("input", "arguments", "params", "payload"):
        value = item.get(key)
        if isinstance(value, dict):
            sources.append(value)
    sources.append(item)
    return sources


def extract_text_from_content_block(block: Any) -> List[str]:
    texts: List[str] = []

    if isinstance(block, str):
        if block.strip():
            texts.append(block)
        return texts

    if not isinstance(block, dict):
        return texts

    block_type = str(block.get("type", ""))

    if isinstance(block.get("text"), str):
        texts.append(block["text"])

    delta = block.get("delta")
    if isinstance(delta, str):
        texts.append(delta)
    elif isinstance(delta, dict) and isinstance(delta.get("text"), str):
        texts.append(delta["text"])

    if isinstance(block.get("content"), list):
        for item in block["content"]:
            texts.extend(extract_text_from_content_block(item))
    elif isinstance(block.get("content"), dict):
        texts.extend(extract_text_from_content_block(block["content"]))

    if not texts and block_type not in {"tool_use", "tool-call", "tool_call"}:
        texts.extend(collect_strings_by_keys(block, {"text"}))

    return texts


def extract_assistant_text(event: Dict[str, Any]) -> str:
    texts: List[str] = []

    message = event.get("message")
    if isinstance(message, dict):
        content = message.get("content")
        if isinstance(content, list):
            for block in content:
                texts.extend(extract_text_from_content_block(block))
        elif isinstance(content, dict):
            texts.extend(extract_text_from_content_block(content))
        if not texts and isinstance(message.get("text"), str):
            texts.append(message["text"])

    if not texts and isinstance(event.get("text"), str):
        texts.append(event["text"])

    return normalize_ws("".join(texts))


def extract_partial_text(event: Dict[str, Any]) -> str:
    candidates: List[str] = []

    if isinstance(event.get("text"), str):
        candidates.append(event["text"])

    delta = event.get("delta")
    if isinstance(delta, str):
        candidates.append(delta)
    elif isinstance(delta, dict) and isinstance(delta.get("text"), str):
        candidates.append(delta["text"])

    content = event.get("content")
    if isinstance(content, list):
        for block in content:
            candidates.extend(extract_text_from_content_block(block))
    elif isinstance(content, dict):
        candidates.extend(extract_text_from_content_block(content))

    if not candidates:
        candidates.extend(collect_strings_by_keys(event, {"text"}))

    return "".join(candidates)


def format_read_file_summary(name: str, sources: List[Dict[str, Any]]) -> str:
    path = first_non_empty_from_sources(
        sources,
        "path",
        "file_path",
        "filepath",
        "target_path",
        "relative_workspace_path",
        "file",
    )
    start = first_non_empty_from_sources(sources, "start_line", "line_start", "from_line", "offset")
    end = first_non_empty_from_sources(sources, "end_line", "line_end", "to_line")

    details = path or "<unknown path>"
    if start and end:
        details += f" [{start}-{end}]"
    elif start:
        details += f" [from {start}]"

    return f"{name}: {details}"


def format_list_directory_summary(name: str, sources: List[Dict[str, Any]]) -> str:
    path = first_non_empty_from_sources(
        sources,
        "path",
        "directory",
        "dir",
        "cwd",
        "root",
        "relative_workspace_path",
    )
    recursive = first_non_empty_from_sources(sources, "recursive")
    details = path or "<unknown dir>"
    if recursive == "True" or recursive == "true":
        details += " (recursive)"
    return f"{name}: {details}"


def format_grep_summary(name: str, sources: List[Dict[str, Any]]) -> str:
    pattern = first_non_empty_from_sources(
        sources,
        "pattern",
        "query",
        "search",
        "regex",
        "needle",
        "text",
    )
    path = first_non_empty_from_sources(
        sources,
        "path",
        "directory",
        "dir",
        "file_path",
        "filepath",
        "cwd",
        "root",
    )
    glob = first_non_empty_from_sources(sources, "glob", "file_pattern", "include", "filter")

    parts: List[str] = []
    if pattern:
        parts.append(f'"{truncate(pattern, 60)}"')
    if path:
        parts.append(f"in {path}")
    if glob:
        parts.append(f"(glob: {glob})")

    return f"{name}: {' '.join(parts)}" if parts else name


def format_write_summary(name: str, sources: List[Dict[str, Any]]) -> str:
    path = first_non_empty_from_sources(
        sources,
        "path",
        "file_path",
        "filepath",
        "target_path",
        "relative_workspace_path",
        "file",
    )
    pattern = first_non_empty_from_sources(sources, "pattern", "search", "find", "old_text")
    replacement = first_non_empty_from_sources(sources, "replacement", "replace", "new_text")

    parts: List[str] = []
    if path:
        parts.append(path)
    if pattern:
        parts.append(f'find "{truncate(pattern, 40)}"')
    if replacement:
        parts.append(f'replace with "{truncate(replacement, 40)}"')

    return f"{name}: {' | '.join(parts)}" if parts else name


def format_agent_summary(name: str, sources: List[Dict[str, Any]]) -> str:
    prompt = first_non_empty_from_sources(
        sources,
        "prompt",
        "task",
        "instruction",
        "instructions",
        "description",
        "query",
    )
    if prompt:
        return f"{name}: {truncate(prompt, 120)}"
    return name


def format_generic_tool_summary(name: str, sources: List[Dict[str, Any]]) -> str:
    command = first_non_empty_from_sources(sources, "command", "cmd")
    if command:
        return f"{name}: {truncate(command, 120)}"

    path = first_non_empty_from_sources(
        sources,
        "path",
        "file_path",
        "filepath",
        "directory",
        "dir",
        "cwd",
        "root",
        "target_path",
    )
    pattern = first_non_empty_from_sources(sources, "pattern", "query", "search", "regex", "text")
    glob = first_non_empty_from_sources(sources, "glob", "file_pattern", "include", "filter")

    if path and pattern:
        details = f'"{truncate(pattern, 60)}" in {path}'
        if glob:
            details += f" (glob: {glob})"
        return f"{name}: {details}"

    if path:
        return f"{name}: {path}"

    if pattern:
        return f'{name}: "{truncate(pattern, 60)}"'

    return name


def extract_tool_summaries(node: Any) -> List[str]:
    summaries: List[str] = []

    for item in walk(node):
        if not isinstance(item, dict):
            continue

        block_type = str(item.get("type", ""))
        if block_type not in {"tool_use", "tool-call", "tool_call"}:
            continue

        name = str(item.get("name") or item.get("tool_name") or "tool")
        sources = tool_sources(item)

        if name == "read_file":
            summaries.append(format_read_file_summary(name, sources))
            continue

        if name == "list_directory":
            summaries.append(format_list_directory_summary(name, sources))
            continue

        if name == "grep_search":
            summaries.append(format_grep_summary(name, sources))
            continue

        if name in {"write_file", "edit_file", "replace_in_file", "search_replace"}:
            summaries.append(format_write_summary(name, sources))
            continue

        if name == "agent":
            summaries.append(format_agent_summary(name, sources))
            continue

        summaries.append(format_generic_tool_summary(name, sources))

    return summaries


def unwrap_event(raw: Any) -> Optional[Dict[str, Any]]:
    if not isinstance(raw, dict):
        return None
    nested = raw.get("event")
    if isinstance(nested, dict):
        return nested
    return raw


class Renderer:
    def __init__(self, mode: str, raw_log_dir: Path) -> None:
        self.mode = mode
        self.raw_log_dir = raw_log_dir
        self.raw_log_dir.mkdir(parents=True, exist_ok=True)

        ts = dt.datetime.now().strftime("%Y%m%d-%H%M%S")
        self.raw_log_path = self.raw_log_dir / f"qwen-stream-{ts}.jsonl"
        self.raw_log = self.raw_log_path.open("a", encoding="utf-8")

        self.partial_buffer = ""
        self.partial_emitted_text = ""
        self.last_partial_flush = 0.0

        self.pending_tool_text: Optional[str] = None
        self.pending_tool_count = 0

        self.info(f"raw log: {self.raw_log_path}")

    def close(self) -> None:
        self.flush_partial(force=True)
        self.flush_pending_tool()
        self.raw_log.close()

    def log_raw(self, line: str) -> None:
        self.raw_log.write(line)
        if not line.endswith("\n"):
            self.raw_log.write("\n")
        self.raw_log.flush()

    def flush_pending_tool(self) -> None:
        if not self.pending_tool_text:
            return

        text = self.pending_tool_text
        if self.pending_tool_count > 1:
            text = f"{text} (x{self.pending_tool_count})"

        print(color(f"[tool] {text}", YELLOW), flush=True)
        self.pending_tool_text = None
        self.pending_tool_count = 0

    def emit_tool(self, text: str) -> None:
        text = text.strip()
        if not text:
            return

        if self.pending_tool_text == text:
            self.pending_tool_count += 1
            return

        self.flush_pending_tool()
        self.pending_tool_text = text
        self.pending_tool_count = 1

    def info(self, text: str) -> None:
        self.flush_pending_tool()
        print(color(f"[info] {text}", DIM), flush=True)

    def session(self, text: str) -> None:
        self.flush_pending_tool()
        print(color(f"[session] {text}", DIM), flush=True)

    def assistant(self, text: str) -> None:
        self.flush_pending_tool()
        print(color("[assistant]", CYAN), flush=True)
        for line in text.splitlines():
            print(line, flush=True)

    def result(self, text: str, ok: bool) -> None:
        self.flush_pending_tool()
        style = GREEN if ok else RED
        print(color(f"[result] {text}", style), flush=True)

    def warn(self, text: str) -> None:
        self.flush_pending_tool()
        print(color(f"[warn] {text}", MAGENTA), flush=True)

    def error(self, text: str) -> None:
        self.flush_pending_tool()
        print(color(f"[error] {text}", RED), flush=True)

    def debug(self, text: str) -> None:
        if self.mode == "debug":
            self.flush_pending_tool()
            print(color(f"[debug] {text}", BLUE), flush=True)

    def handle(self, raw: Dict[str, Any]) -> None:
        event = unwrap_event(raw)
        if event is None:
            return

        event_type = str(event.get("type", ""))
        subtype = str(event.get("subtype", ""))
        event_type_l = event_type.lower()
        subtype_l = subtype.lower()

        if event_type_l == "system" and subtype_l == "session_start":
            session_id = raw.get("session_id") or event.get("session_id")
            detail = "session started"
            if isinstance(session_id, str) and session_id.strip():
                detail += f" ({truncate(session_id, 24)})"
            self.session(detail)
            return

        if self.is_partial_event(event):
            self.handle_partial(event)
            return

        if event_type_l == "assistant":
            self.flush_partial(force=True)
            self.handle_assistant(event)
            return

        if "tool" in event_type_l or "tool" in subtype_l:
            self.flush_partial(force=True)
            self.handle_tool_event(event)
            return

        if event_type_l == "result":
            self.flush_partial(force=True)
            self.handle_result(event)
            return

        if self.looks_like_error(event):
            self.flush_partial(force=True)
            self.handle_error_event(event)
            return

        if self.mode == "debug":
            self.debug(f"hidden event type={event_type or '?'} subtype={subtype or '?'}")

    def is_partial_event(self, event: Dict[str, Any]) -> bool:
        event_type = str(event.get("type", "")).lower()
        subtype = str(event.get("subtype", "")).lower()
        candidates = {
            "message_start",
            "message_delta",
            "message_stop",
            "content_block_start",
            "content_block_delta",
            "content_block_stop",
            "text_delta",
        }
        return event_type in candidates or subtype in candidates

    def handle_partial(self, event: Dict[str, Any]) -> None:
        event_type = str(event.get("type", "")).lower()
        text = extract_partial_text(event)

        if event_type in {"message_start", "content_block_start"}:
            return

        if event_type in {"message_stop", "content_block_stop"}:
            self.flush_partial(force=True)
            return

        if not text:
            return

        self.partial_buffer += text
        self.partial_emitted_text += text

        now = time.monotonic()
        buffer = self.partial_buffer

        has_newline = "\n" in buffer
        has_sentence_boundary = re.search(r'[.!?]["\')\]]?\s*$', buffer) is not None
        has_clause_boundary = len(buffer) >= 120 and re.search(r"[:;]\s*$", buffer) is not None
        very_large_buffer = len(buffer) >= 320
        stale_large_buffer = len(buffer) >= 180 and (now - self.last_partial_flush) >= 1.2

        should_flush = (
            has_newline
            or has_sentence_boundary
            or has_clause_boundary
            or very_large_buffer
            or stale_large_buffer
        )

        if should_flush:
            self.flush_partial(force=True)

    def flush_partial(self, force: bool = False) -> None:
        if not self.partial_buffer:
            return

        if not force:
            now = time.monotonic()
            if (now - self.last_partial_flush) < 1.0 and len(self.partial_buffer) < 180:
                return

        chunk = self.partial_buffer
        self.partial_buffer = ""

        chunk = normalize_ws(chunk)
        if not chunk:
            return

        self.last_partial_flush = time.monotonic()
        self.assistant(chunk)

    def handle_assistant(self, event: Dict[str, Any]) -> None:
        text = extract_assistant_text(event)

        if text:
            normalized_partial = normalize_ws(self.partial_emitted_text)
            if normalized_partial and text == normalized_partial:
                self.debug("suppressed duplicate final assistant message")
                self.partial_emitted_text = ""
                return

            self.assistant(text)
            self.partial_emitted_text = ""
            return

        tool_summaries = extract_tool_summaries(event)
        if tool_summaries and self.mode != "minimal":
            for summary in tool_summaries:
                self.emit_tool(summary)

    def handle_tool_event(self, event: Dict[str, Any]) -> None:
        summaries = extract_tool_summaries(event)

        if summaries:
            if self.mode != "minimal":
                for summary in summaries:
                    self.emit_tool(summary)
            return

        name = (
            event.get("name")
            or event.get("tool_name")
            or event.get("tool")
            or event.get("subtype")
            or event.get("type")
            or "tool"
        )

        if self.mode != "minimal":
            self.emit_tool(str(name))

    def handle_result(self, event: Dict[str, Any]) -> None:
        ok = not bool(event.get("is_error", False)) and str(event.get("subtype", "")).lower() != "error"
        duration_ms = event.get("duration_ms")
        result_text = event.get("result")

        parts = ["success" if ok else "error"]
        if isinstance(duration_ms, (int, float)):
            parts.append(f"{duration_ms / 1000:.1f}s")

        if self.mode == "debug" and isinstance(result_text, str) and result_text.strip():
            parts.append(truncate(result_text, 120))

        self.result(" · ".join(parts), ok)

    def looks_like_error(self, event: Dict[str, Any]) -> bool:
        event_type = str(event.get("type", "")).lower()
        subtype = str(event.get("subtype", "")).lower()
        return bool(event.get("is_error", False)) or event_type == "error" or subtype == "error"

    def handle_error_event(self, event: Dict[str, Any]) -> None:
        message = None

        for key in ("message", "error", "result", "text"):
            value = event.get(key)
            if isinstance(value, str) and value.strip():
                message = value
                break

        if not message:
            message = truncate(json.dumps(event, ensure_ascii=False), 160)

        self.error(message)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Pretty-print Qwen stream-json output.")
    parser.add_argument(
        "--mode",
        choices=["minimal", "normal", "debug"],
        default="normal",
        help="Filtering level for terminal output.",
    )
    parser.add_argument(
        "--raw-log-dir",
        default="logs/qwen-stream",
        help="Directory for raw JSONL logs.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    renderer = Renderer(mode=args.mode, raw_log_dir=Path(args.raw_log_dir))

    try:
        for line in sys.stdin:
            if not line.strip():
                continue

            renderer.log_raw(line)

            try:
                raw = json.loads(line)
            except json.JSONDecodeError:
                renderer.warn(f"skipped non-JSON line: {truncate(line, 160)}")
                continue

            renderer.handle(raw)

        renderer.flush_partial(force=True)
        return 0
    finally:
        renderer.close()


if __name__ == "__main__":
    raise SystemExit(main())