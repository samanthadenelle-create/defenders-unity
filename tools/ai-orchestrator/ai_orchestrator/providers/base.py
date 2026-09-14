"""The common provider interface and its factory.

Nothing above this layer knows whether a role runs locally or over the network.
"""

from __future__ import annotations

import abc
from dataclasses import dataclass, field
from typing import Any

from ..config import ModelRole, OrchestratorConfig, ProviderSettings


class ProviderError(RuntimeError):
    """A provider call failed (transport, HTTP status, unparseable body)."""


class ProviderConfigError(ProviderError):
    """A provider is not usable as configured - e.g. its API key is absent.

    This exists so the reasoner half FAILS LOUDLY on a machine with no
    ``OPENAI_API_KEY`` instead of degrading into a stub that returns invented
    text. A missing key is a stop, never a fallback.
    """


@dataclass
class ProviderResponse:
    """One model call's outcome, uniform across providers."""

    text: str = ""
    data: dict[str, Any] | None = None
    tool_calls: list[dict[str, Any]] = field(default_factory=list)
    input_tokens: int = 0
    output_tokens: int = 0
    raw: dict[str, Any] = field(default_factory=dict)


class LLMProvider(abc.ABC):
    """Spec section 4's interface."""

    name: str = "provider"

    def __init__(self, settings: ProviderSettings, role: ModelRole) -> None:
        self.settings = settings
        self.role = role

    @property
    def model(self) -> str:
        """The model string, resolved from config - never a literal in code."""
        return self.role.model

    @abc.abstractmethod
    def generate(
        self, prompt: str, *, system: str | None = None, **options: Any
    ) -> ProviderResponse:
        """Free-form completion."""

    @abc.abstractmethod
    def generate_structured(
        self,
        prompt: str,
        schema: dict[str, Any],
        *,
        system: str | None = None,
        **options: Any,
    ) -> ProviderResponse:
        """Completion constrained to a JSON Schema; ``response.data`` is parsed."""

    @abc.abstractmethod
    def call_tools(
        self,
        messages: list[dict[str, Any]],
        tools: list[dict[str, Any]],
        **options: Any,
    ) -> ProviderResponse:
        """Tool-calling turn; ``response.tool_calls`` carries requested calls."""

    def estimate_cost(self, input_tokens: int, output_tokens: int) -> float:
        return self.role.pricing.estimate_usd(input_tokens, output_tokens)


def build_provider(config: OrchestratorConfig, role_name: str) -> LLMProvider:
    """Resolve a ROLE into a live provider instance.

    The only place a provider class is chosen. Callers name roles, not models.
    """
    from .ollama_provider import OllamaProvider
    from .openai_provider import OpenAIProvider

    role = config.role(role_name)
    settings = config.provider(role.provider)
    registry: dict[str, type[LLMProvider]] = {
        "ollama": OllamaProvider,
        "openai": OpenAIProvider,
    }
    try:
        cls = registry[role.provider]
    except KeyError:
        raise ProviderConfigError(
            f"role '{role_name}' names provider '{role.provider}', which has no"
            f" implementation. Known providers: {', '.join(sorted(registry))}"
        ) from None
    return cls(settings, role)
