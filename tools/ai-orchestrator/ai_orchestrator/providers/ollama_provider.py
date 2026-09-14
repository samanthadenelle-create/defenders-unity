"""Local worker provider, spoken to over Ollama's NATIVE /api/chat.

Why native rather than the OpenAI-compatible /v1 shim: /api/chat accepts a raw
JSON Schema in ``format`` (proven against gpt-oss:20b on this machine,
2026-09-14, HTTP 200 with the schema honoured) and returns
``prompt_eval_count`` / ``eval_count``, which are the token numbers spec section
13 requires us to log. The /v1 shim hides both behind an OpenAI-shaped envelope.
"""

from __future__ import annotations

import json
from typing import Any

import httpx

from .base import LLMProvider, ProviderError, ProviderResponse


class OllamaProvider(LLMProvider):
    name = "ollama"

    def _post(self, path: str, payload: dict[str, Any]) -> dict[str, Any]:
        url = f"{self.settings.base_url}{path}"
        try:
            response = httpx.post(
                url, json=payload, timeout=self.settings.timeout_seconds
            )
        except httpx.HTTPError as exc:
            raise ProviderError(
                f"ollama request to {url} failed: {exc}. Is `ollama serve` running?"
            ) from exc
        if response.status_code != 200:
            raise ProviderError(
                f"ollama {path} returned HTTP {response.status_code}: {response.text[:500]}"
            )
        try:
            return response.json()
        except ValueError as exc:
            raise ProviderError(
                f"ollama {path} returned a non-JSON body: {response.text[:500]}"
            ) from exc

    def _payload(self, messages: list[dict[str, Any]], **options: Any) -> dict[str, Any]:
        payload: dict[str, Any] = {
            "model": self.model,
            "messages": messages,
            "stream": False,
        }
        # Role options from models.yaml (e.g. think: low for gpt-oss) come first,
        # call-site options win.
        merged = {
            k: v
            for k, v in self.role.options.items()
            if k in {"think", "keep_alive", "options"}
        }
        merged.update({k: v for k, v in options.items() if v is not None})
        payload.update(merged)
        return payload

    @staticmethod
    def _messages(prompt: str, system: str | None) -> list[dict[str, Any]]:
        messages: list[dict[str, Any]] = []
        if system:
            messages.append({"role": "system", "content": system})
        messages.append({"role": "user", "content": prompt})
        return messages

    @staticmethod
    def _tokens(body: dict[str, Any]) -> tuple[int, int]:
        return int(body.get("prompt_eval_count") or 0), int(body.get("eval_count") or 0)

    def generate(
        self, prompt: str, *, system: str | None = None, **options: Any
    ) -> ProviderResponse:
        body = self._post(
            "/api/chat", self._payload(self._messages(prompt, system), **options)
        )
        inp, out = self._tokens(body)
        return ProviderResponse(
            text=(body.get("message") or {}).get("content", ""),
            input_tokens=inp,
            output_tokens=out,
            raw=body,
        )

    def generate_structured(
        self,
        prompt: str,
        schema: dict[str, Any],
        *,
        system: str | None = None,
        **options: Any,
    ) -> ProviderResponse:
        body = self._post(
            "/api/chat",
            self._payload(self._messages(prompt, system), format=schema, **options),
        )
        inp, out = self._tokens(body)
        text = (body.get("message") or {}).get("content", "")
        try:
            data = json.loads(text)
        except json.JSONDecodeError as exc:
            raise ProviderError(
                f"{self.model} returned non-JSON under a structured-output schema: "
                f"{text[:500]}"
            ) from exc
        if not isinstance(data, dict):
            raise ProviderError(
                f"{self.model} returned a JSON {type(data).__name__}, expected an object"
            )
        return ProviderResponse(
            text=text, data=data, input_tokens=inp, output_tokens=out, raw=body
        )

    def call_tools(
        self,
        messages: list[dict[str, Any]],
        tools: list[dict[str, Any]],
        **options: Any,
    ) -> ProviderResponse:
        body = self._post("/api/chat", self._payload(messages, tools=tools, **options))
        inp, out = self._tokens(body)
        message = body.get("message") or {}
        return ProviderResponse(
            text=message.get("content", ""),
            tool_calls=list(message.get("tool_calls") or []),
            input_tokens=inp,
            output_tokens=out,
            raw=body,
        )
