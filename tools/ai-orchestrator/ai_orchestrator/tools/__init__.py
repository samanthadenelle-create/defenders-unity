"""Tool layer (spec section 9). Build a registry with :func:`default_registry`."""

from __future__ import annotations

from . import filesystem, git_tools, search, shell, tests
from .base import Tool, ToolContext, ToolRegistry, ToolResult

__all__ = [
    "Tool",
    "ToolContext",
    "ToolRegistry",
    "ToolResult",
    "default_registry",
]


def default_registry(context: ToolContext) -> ToolRegistry:
    """The full allow-list a worker may see."""
    registry = ToolRegistry(context)
    for module in (filesystem, search, git_tools, shell, tests):
        for tool in module.TOOLS:
            registry.register(tool)
    return registry
