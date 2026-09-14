"""Configuration loading. Model names live in models.yaml and nowhere else.

Spec section 5: "Model names must never be hard-coded throughout the
application." Code asks for a ROLE (``reasoner`` / ``worker`` /
``worker_fallback``); this module is the only thing that knows what string that
role currently resolves to.
"""

from __future__ import annotations

import os
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import yaml

PACKAGE_ROOT = Path(__file__).resolve().parent
TOOL_ROOT = PACKAGE_ROOT.parent
DEFAULT_CONFIG_PATH = TOOL_ROOT / "models.yaml"
DEFAULT_STATE_DIR = TOOL_ROOT / ".state"


class ConfigError(RuntimeError):
    """models.yaml is missing, malformed, or lacks a requested role."""


@dataclass(frozen=True)
class Pricing:
    """USD per 1M tokens. Local providers are 0/0."""

    input_per_mtok: float = 0.0
    output_per_mtok: float = 0.0

    def estimate_usd(self, input_tokens: int, output_tokens: int) -> float:
        return (
            input_tokens * self.input_per_mtok + output_tokens * self.output_per_mtok
        ) / 1_000_000.0


@dataclass(frozen=True)
class ModelRole:
    """One row of the ``models:`` block: a role bound to a provider + model."""

    role: str
    provider: str
    model: str
    pricing: Pricing = field(default_factory=Pricing)
    options: dict[str, Any] = field(default_factory=dict)


@dataclass(frozen=True)
class ProviderSettings:
    name: str
    base_url: str
    timeout_seconds: float = 600.0
    api_key_env: str | None = None

    def api_key(self) -> str | None:
        """Read the key from the ENVIRONMENT at call time. Never from a file."""
        if not self.api_key_env:
            return None
        value = os.environ.get(self.api_key_env, "").strip()
        return value or None


@dataclass(frozen=True)
class OrchestratorConfig:
    path: Path
    roles: dict[str, ModelRole]
    providers: dict[str, ProviderSettings]
    limits: dict[str, Any]

    def role(self, name: str) -> ModelRole:
        try:
            return self.roles[name]
        except KeyError:
            known = ", ".join(sorted(self.roles)) or "<none>"
            raise ConfigError(
                f"models.yaml ({self.path}) defines no role '{name}'. Known roles: {known}"
            ) from None

    def provider(self, name: str) -> ProviderSettings:
        try:
            return self.providers[name]
        except KeyError:
            known = ", ".join(sorted(self.providers)) or "<none>"
            raise ConfigError(
                f"models.yaml ({self.path}) defines no provider '{name}'. Known: {known}"
            ) from None

    @property
    def max_autonomous_retries(self) -> int:
        return int(self.limits.get("max_autonomous_retries", 2))

    @property
    def max_files_per_work_order(self) -> int:
        return int(self.limits.get("max_files_per_work_order", 12))


_RESERVED_ROLE_KEYS = {"provider", "model", "pricing"}


def load_config(path: str | Path | None = None) -> OrchestratorConfig:
    """Parse models.yaml into an :class:`OrchestratorConfig`."""
    cfg_path = Path(path) if path else DEFAULT_CONFIG_PATH
    if not cfg_path.is_file():
        raise ConfigError(f"model configuration not found: {cfg_path}")

    raw = yaml.safe_load(cfg_path.read_text(encoding="utf-8")) or {}
    if not isinstance(raw, dict):
        raise ConfigError(f"{cfg_path} must contain a YAML mapping")

    roles: dict[str, ModelRole] = {}
    for role_name, body in (raw.get("models") or {}).items():
        if not isinstance(body, dict) or "provider" not in body or "model" not in body:
            raise ConfigError(
                f"{cfg_path}: role '{role_name}' needs both 'provider' and 'model'"
            )
        pricing_body = body.get("pricing") or {}
        roles[role_name] = ModelRole(
            role=role_name,
            provider=str(body["provider"]),
            model=str(body["model"]),
            pricing=Pricing(
                input_per_mtok=float(pricing_body.get("input_per_mtok", 0.0)),
                output_per_mtok=float(pricing_body.get("output_per_mtok", 0.0)),
            ),
            options={k: v for k, v in body.items() if k not in _RESERVED_ROLE_KEYS},
        )

    providers: dict[str, ProviderSettings] = {}
    for provider_name, body in (raw.get("providers") or {}).items():
        body = body or {}
        providers[provider_name] = ProviderSettings(
            name=provider_name,
            base_url=str(body.get("base_url", "")).rstrip("/"),
            timeout_seconds=float(body.get("timeout_seconds", 600.0)),
            api_key_env=body.get("api_key_env"),
        )

    if not roles:
        raise ConfigError(f"{cfg_path} defines no models")

    return OrchestratorConfig(
        path=cfg_path,
        roles=roles,
        providers=providers,
        limits=dict(raw.get("limits") or {}),
    )
