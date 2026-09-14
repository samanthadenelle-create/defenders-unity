"""Provider abstraction (spec sections 3-4).

``LLMProvider.generate()`` / ``generate_structured()`` / ``call_tools()`` is the
whole surface the orchestrator knows. Swapping the local model, the reasoning
model, or a whole provider is a models.yaml edit, not a code change.
"""

from .base import (
    LLMProvider,
    ProviderConfigError,
    ProviderError,
    ProviderResponse,
    build_provider,
)
from .ollama_provider import OllamaProvider
from .openai_provider import OpenAIProvider

__all__ = [
    "LLMProvider",
    "ProviderConfigError",
    "ProviderError",
    "ProviderResponse",
    "build_provider",
    "OllamaProvider",
    "OpenAIProvider",
]
