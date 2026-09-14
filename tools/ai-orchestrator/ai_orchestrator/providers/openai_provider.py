"""Reasoner provider (OpenAI Responses API, spec section 3).

STATE OF THIS FILE, 2026-09-14: the class is real and config-driven, and it has
NEVER BEEN EXERCISED AGAINST THE LIVE API - there is no ``OPENAI_API_KEY`` on
this machine (WO-1706 section 3). The request/response mapping below follows the
Responses API shape and is UNPROVEN; the first run with a real key must confirm
it, and any mismatch is a bug in this file, not in the orchestrator above it.

What IS proven is the failure mode: with no key, every call raises
:class:`ProviderConfigError` naming the environment variable. There is no stub
path, no canned text, no silent degrade to the local worker.
"""

from __future__ import annotations

import json
from typing import Any

import httpx

from .base import LLMProvider, ProviderConfigError, ProviderError, ProviderResponse


class OpenAIProvider(LLMProvider):
    name = "openai"

    def _require_key(self) -> str:
        key = self.settings.api_key()
        if not key:
            var = self.settings.api_key_env or "OPENAI_API_KEY"
            raise ProviderConfigError(
                f"the '{self.role.role}' role needs provider 'openai' model"
                f" '{self.model}', but environment variable {var} is not set."
                " Set it in the environment (never in a repo file) and re-run."
                " The reasoner half of WO-1706 is blocked until then."
            )
        return key

    def _post(self, path: str, payload: dict[str, Any]) -> dict[str, Any]:
        key = self._require_key()
        url = f"{self.settings.base_url}{path}"
        try:
            response = httpx.post(
                url,
                json=payload,
                headers={
                    "Authorization": f"Bearer {key}",
                    "Content-Type": "application/json",
                },
                timeout=self.settings.timeout_seconds,
            )
        except httpx.HTTPError as exc:
            raise ProviderError(f"openai request to {url} failed: {exc}") from exc
        if response.status_code != 200:
            raise ProviderError(
                f"openai {path} returned HTTP {response.status_code}: {response.text[:500]}"
            )
        return response.json()

    def _payload(self, prompt: str, system: str | None, **options: Any) -> dict[str, Any]:
        payload: dict[str, Any] = {"model": self.model, "input": prompt}
        if system:
            payload["instructions"] = system
        reasoning = self.role.options.get("reasoning")
        if reasoning:
            payload["reasoning"] = {"effort": reasoning}
        payload.update({k: v for k, v in options.items() if v is not None})
        return payload

    @staticmethod
    def _tokens(body: dict[str, Any]) -> tuple[int, int]:
        usage = body.get("usage") or {}
        return int(usage.get("input_tokens") or 0), int(usage.get("output_tokens") or 0)

    @staticmethod
    def _text(body: dict[str, Any]) -> str:
        if isinstance(body.get("output_text"), str):
            return body["output_text"]
        chunks: list[str] = []
        for item in body.get("output") or []:
            for part in item.get("content") or []:
                if isinstance(part, dict) and isinstance(part.get("text"), str):
                    chunks.append(part["text"])
        return "".join(chunks)

    def generate(
        self, prompt: str, *, system: str | None = None, **options: Any
    ) -> ProviderResponse:
        body = self._post("/responses", self._payload(prompt, system, **options))
        inp, out = self._tokens(body)
        return ProviderResponse(
            text=self._text(body), input_tokens=inp, output_tokens=out, raw=body
        )

    def generate_structured(
        self,
        prompt: str,
        schema: dict[str, Any],
        *,
        system: str | None = None,
        **options: Any,
    ) -> ProviderResponse:
        payload = self._payload(prompt, system, **options)
        payload["text"] = {
            "format": {
                "type": "json_schema",
                "name": "structured_output",
                "schema": schema,
                "strict": False,
            }
        }
        body = self._post("/responses", payload)
        inp, out = self._tokens(body)
        text = self._text(body)
        try:
            data = json.loads(text)
        except json.JSONDecodeError as exc:
            raise ProviderError(
                f"{self.model} returned non-JSON under json_schema: {text[:500]}"
            ) from exc
        return ProviderResponse(
            text=text, data=data, input_tokens=inp, output_tokens=out, raw=body
        )

    def call_tools(
        self,
        messages: list[dict[str, Any]],
        tools: list[dict[str, Any]],
        **options: Any,
    ) -> ProviderResponse:
        self._require_key()
        payload: dict[str, Any] = {
            "model": self.model,
            "input": messages,
            "tools": tools,
        }
        payload.update({k: v for k, v in options.items() if v is not None})
        body = self._post("/responses", payload)
        inp, out = self._tokens(body)
        tool_calls = [
            item
            for item in (body.get("output") or [])
            if item.get("type") == "function_call"
        ]
        return ProviderResponse(
            text=self._text(body),
            tool_calls=tool_calls,
            input_tokens=inp,
            output_tokens=out,
            raw=body,
        )
