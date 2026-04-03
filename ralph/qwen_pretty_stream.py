#!/usr/bin/env python3
import argparse
import datetime as dt
import json
import re
import sys
import time
from pathlib import Path
from typing import Any, Iterable

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
            yield from walk(value)
    elif isinstance(node, list):
        for item in node:
            yield from walk(item)


def collect_strings_by_keys(node: Any, keys: set[str]) -> list[str]:
    found: list[str] = []

    def _walk(obj: Any) -> None:
        if isinstance(obj, dict):
            for k, v in obj.items():
                if k in keys:
                    if isinstance(v, str):
                        found.append(v)
                    elif isinstance(v, dict):
                        txt = v.get("text")
                        if isinstance(txt, str):
                            found.append(txt)
                _walk(v)
        elif isinstance(obj, list):
            for item in obj:
                _walk(item)

    _walk(node)
    return found


def extract_text_from_content_block(block: Any) -> list[str]:
    texts: list[str] = []

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

    if not texts and block_type not in {"tool_use", "tool-call", "tool_call"}:
        texts.extend(collect_strings_by_keys(block, {"text"}))

    return texts


def extract_assistant_text(event: dict[str, Any]) -> str:
    texts: list[str] = []

    message = event.get("message")
    if isinstance(message, dict):
        content = message.get("content")
        if isinstance(content, list):
            for block in content:
                texts.extend(extract_text_from_content_block(block))
        if not texts and isinstance(message.get("text"), str):
            texts.append(message["text"])

    if not texts and isinstance(event.get("text"), str):
        texts.append(event["text"])

    return normalize_ws("".join(texts))


def extract_partial_text(event: dict[str, Any]) -> str:
    candidates: list[str] = []

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


def extract_tool_summaries(node: Any) -> list[str]:
    summaries: list[str] = []

    for item in walk(node):
        if not isinstance(item, dict):
            continue

        block_type = str(item.get("type", ""))
        if block_type not in {"tool_use", "tool-call", "tool_call"}:
            continue

        name = item.get("name") or item.get("tool_name") or "tool"
        command = item.get("command")
        if isinstance(command, str) and command.strip():
            summaries.append(f"{name}: {truncate(command, 120)}")
            continue

        tool_input = item.get("input")
        if isinstance(tool_input, dict):
            maybe_command = tool_input.get("command")
            if isinstance(maybe_command, str) and maybe_command.strip():
                summaries.append(f"{name}: {truncate(maybe_command, 120)}")
                continue

        summaries.append(str(name))

    return summaries


def unwrap_event(raw: Any) -> dict[str, Any] | None:
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
        self.in_partial_section = False

        self.info(f"raw log: {self.raw_log_path}")

    def close(self) -> None:
        self.flush_partial(force=True)
        self.raw_log.close()

    def log_raw(self, line: str) -> None:
        self.raw_log.write(line)
        if not line.endswith("\n"):
            self.raw_log.write("\n")
        self.raw_log.flush()

    def info(self, text: str) -> None:
        print(color(f"[info] {text}", DIM), flush=True)

    def session(self, text: str) -> None:
        print(color(f"[session] {text}", DIM), flush=True)

    def tool(self, text: str) -> None:
        print(color(f"[tool] {text}", YELLOW), flush=True)

    def assistant(self, text: str) -> None:
        print(color("[assistant]", CYAN), flush=True)
        for line in text.splitlines():
            print(line, flush=True)

    def result(self, text: str, ok: bool) -> None:
        style = GREEN if ok else RED
        print(color(f"[result] {text}", style), flush=True)

    def warn(self, text: str) -> None:
        print(color(f"[warn] {text}", MAGENTA), flush=True)

    def error(self, text: str) -> None:
        print(color(f"[error] {text}", RED), flush=True)

    def debug(self, text: str) -> None:
        if self.mode == "debug":
            print(color(f"[debug] {text}", BLUE), flush=True)

    def handle(self, raw: dict[str, Any]) -> None:
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

    def is_partial_event(self, event: dict[str, Any]) -> bool:
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

    def handle_partial(self, event: dict[str, Any]) -> None:
        event_type = str(event.get("type", "")).lower()
        text = extract_partial_text(event)

        if event_type in {"message_start", "content_block_start"}:
            self.in_partial_section = True
            return

        if event_type in {"message_stop", "content_block_stop"}:
            self.flush_partial(force=True)
            self.in_partial_section = False
            return

        if not text:
            return

        self.partial_buffer += text
        self.partial_emitted_text += text

        now = time.monotonic()
        should_flush = (
            "\n" in self.partial_buffer
            or len(self.partial_buffer) >= 160
            or re.search(r"[.!?]\s*$", self.partial_buffer) is not None
            or (now - self.last_partial_flush) >= 0.25
        )

        if should_flush:
            self.flush_partial(force=True)

    def flush_partial(self, force: bool = False) -> None:
        if not self.partial_buffer:
            return

        if not force:
            now = time.monotonic()
            if (now - self.last_partial_flush) < 0.25 and len(self.partial_buffer) < 160:
                return

        chunk = normalize_ws(self.partial_buffer)
        self.partial_buffer = ""

        if not chunk:
            return

        self.last_partial_flush = time.monotonic()
        self.assistant(chunk)

    def handle_assistant(self, event: dict[str, Any]) -> None:
        tool_summaries = extract_tool_summaries(event)
        if self.mode != "minimal":
            for summary in tool_summaries:
                self.tool(summary)

        text = extract_assistant_text(event)
        if not text:
            return

        normalized_partial = normalize_ws(self.partial_emitted_text)
        if normalized_partial and text == normalized_partial:
            self.debug("suppressed duplicate final assistant message")
            self.partial_emitted_text = ""
            return

        self.assistant(text)
        self.partial_emitted_text = ""

    def handle_tool_event(self, event: dict[str, Any]) -> None:
        name = (
            event.get("name")
            or event.get("tool_name")
            or event.get("tool")
            or event.get("subtype")
            or event.get("type")
            or "tool"
        )

        command = event.get("command")
        if not isinstance(command, str):
            maybe_input = event.get("input")
            if isinstance(maybe_input, dict):
                maybe_command = maybe_input.get("command")
                if isinstance(maybe_command, str):
                    command = maybe_command

        text = str(name)
        if isinstance(command, str) and command.strip():
            text += f": {truncate(command, 120)}"

        if self.mode != "minimal":
            self.tool(text)

    def handle_result(self, event: dict[str, Any]) -> None:
        ok = not bool(event.get("is_error", False)) and str(event.get("subtype", "")).lower() != "error"
        duration_ms = event.get("duration_ms")
        result_text = event.get("result")

        parts = ["success" if ok else "error"]
        if isinstance(duration_ms, (int, float)):
            parts.append(f"{duration_ms/1000:.1f}s")

        if self.mode == "debug" and isinstance(result_text, str) and result_text.strip():
            parts.append(truncate(result_text, 120))

        self.result(" · ".join(parts), ok)

    def looks_like_error(self, event: dict[str, Any]) -> bool:
        event_type = str(event.get("type", "")).lower()
        subtype = str(event.get("subtype", "")).lower()
        return bool(event.get("is_error", False)) or event_type == "error" or subtype == "error"

    def handle_error_event(self, event: dict[str, Any]) -> None:
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