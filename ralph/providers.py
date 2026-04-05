#!/usr/bin/env python3
"""
Provider abstraction for Ralph Loop.

Supports Qwen, Claude Code, and Codex backends.
"""

import subprocess
import sys
from abc import ABC, abstractmethod
from pathlib import Path
from typing import List, Optional


class AgentProvider(ABC):
    """Abstract base class for agent providers."""

    @abstractmethod
    def get_name(self) -> str:
        """Return provider name."""
        pass

    @abstractmethod
    def is_available(self) -> bool:
        """Check if provider CLI is available."""
        pass

    @abstractmethod
    def build_command(
        self,
        prompt: str,
        repo_root: Path,
        yolo: bool = False
    ) -> List[str]:
        """Build command to execute agent with prompt."""
        pass

    @abstractmethod
    def supports_rich_progress(self) -> bool:
        """Return True if provider supports structured progress output."""
        pass

    @abstractmethod
    def get_progress_renderer(self, repo_root: Path) -> Optional[List[str]]:
        """
        Return command to pipe output through for progress rendering.
        Returns None if no renderer needed.
        """
        pass


class QwenProvider(AgentProvider):
    """Qwen agent provider."""

    def get_name(self) -> str:
        return "qwen"

    def is_available(self) -> bool:
        try:
            subprocess.run(
                ["qwen", "--version"],
                capture_output=True,
                check=False
            )
            return True
        except FileNotFoundError:
            return False

    def build_command(
        self,
        prompt: str,
        repo_root: Path,
        yolo: bool = False
    ) -> List[str]:
        cmd = [
            "qwen",
            "--output-format", "stream-json",
            "--include-partial-messages",
            "--prompt", prompt
        ]
        if yolo:
            cmd.insert(1, "--yolo")
        return cmd

    def supports_rich_progress(self) -> bool:
        return True

    def get_progress_renderer(self, repo_root: Path) -> Optional[List[str]]:
        renderer_path = repo_root / "ralph" / "qwen_pretty_stream.py"
        if not renderer_path.exists():
            return None

        log_dir = repo_root / "ralph" / "logs" / "qwen-stream"
        log_dir.mkdir(parents=True, exist_ok=True)

        return [
            sys.executable, "-u", str(renderer_path),
            "--mode", "normal",
            "--raw-log-dir", str(log_dir)
        ]


class ClaudeCodeProvider(AgentProvider):
    """Claude Code agent provider."""

    def get_name(self) -> str:
        return "claude"

    def is_available(self) -> bool:
        try:
            subprocess.run(
                ["claude", "--version"],
                capture_output=True,
                check=False
            )
            return True
        except FileNotFoundError:
            return False

    def build_command(
        self,
        prompt: str,
        repo_root: Path,
        yolo: bool = False
    ) -> List[str]:
        cmd = [
            "claude",
            "--print",
            "--output-format", "stream-json",
            "--include-partial-messages",
            prompt
        ]
        if yolo:
            cmd.insert(1, "--dangerously-skip-permissions")
        return cmd

    def supports_rich_progress(self) -> bool:
        return False  # Best-effort line-based progress

    def get_progress_renderer(self, repo_root: Path) -> Optional[List[str]]:
        renderer_path = repo_root / "ralph" / "simple_stream_renderer.py"
        if not renderer_path.exists():
            return None
        return [sys.executable, "-u", str(renderer_path)]


class CodexProvider(AgentProvider):
    """Codex agent provider."""

    def get_name(self) -> str:
        return "codex"

    def is_available(self) -> bool:
        try:
            subprocess.run(
                ["codex", "--version"],
                capture_output=True,
                check=False
            )
            return True
        except FileNotFoundError:
            return False

    def build_command(
        self,
        prompt: str,
        repo_root: Path,
        yolo: bool = False
    ) -> List[str]:
        cmd = [
            "codex", "exec",
            prompt
        ]
        if yolo:
            cmd.insert(2, "--dangerously-bypass-approvals-and-sandbox")
        return cmd

    def supports_rich_progress(self) -> bool:
        return False  # Best-effort line-based progress

    def get_progress_renderer(self, repo_root: Path) -> Optional[List[str]]:
        renderer_path = repo_root / "ralph" / "simple_stream_renderer.py"
        if not renderer_path.exists():
            return None
        return [sys.executable, "-u", str(renderer_path)]


def get_provider(name: str) -> AgentProvider:
    """Get provider by name."""
    providers = {
        "qwen": QwenProvider(),
        "claude": ClaudeCodeProvider(),
        "codex": CodexProvider(),
    }

    provider = providers.get(name.lower())
    if not provider:
        raise ValueError(f"Unknown provider: {name}. Available: {', '.join(providers.keys())}")

    return provider


def list_available_providers() -> List[str]:
    """Return list of available provider names."""
    all_providers = [
        QwenProvider(),
        ClaudeCodeProvider(),
        CodexProvider(),
    ]
    return [p.get_name() for p in all_providers if p.is_available()]
