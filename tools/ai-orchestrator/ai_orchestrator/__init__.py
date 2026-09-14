"""Hybrid AI reasoning + task execution orchestrator (WO-1706, Phase 1).

The reasoning model decides WHAT should be done; the local worker model decides
HOW to execute an approved work order inside explicit boundaries. Every request
is classified BEFORE any model call, so deterministic work never spends a token.

Phase 1 ships the WORKER half. The reasoner provider is real, config-driven, and
fails loudly when ``OPENAI_API_KEY`` is absent - it is never a silent stub.
"""

__version__ = "0.1.0"
