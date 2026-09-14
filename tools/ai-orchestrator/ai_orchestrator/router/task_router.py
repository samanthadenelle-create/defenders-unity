"""Task router (spec section 6).

Classification happens BEFORE any model is constructed, let alone called. That
ordering is the whole cost-control mechanism in spec section 13: a DETERMINISTIC
request must reach conventional code without a single token being spent, and the
test suite proves it by routing through a provider that raises if touched.

The rules are deliberately lexical and explainable. A classifier that is itself
an LLM call would spend money deciding whether to spend money.
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field
from enum import Enum


class Classification(str, Enum):
    DETERMINISTIC = "DETERMINISTIC"
    WORKER = "WORKER"
    REASONING = "REASONING"
    REASONING_THEN_WORKER = "REASONING_THEN_WORKER"
    ESCALATION = "ESCALATION"

    @property
    def uses_model(self) -> bool:
        return self is not Classification.DETERMINISTIC

    @property
    def needs_reasoner(self) -> bool:
        return self in (
            Classification.REASONING,
            Classification.REASONING_THEN_WORKER,
            Classification.ESCALATION,
        )


@dataclass(frozen=True)
class RoutingDecision:
    classification: Classification
    reason: str
    matched: list[str] = field(default_factory=list)
    operation: str | None = None  # deterministic operation name, when applicable


# -- DETERMINISTIC: the spec section 6 list, each bound to a real operation ----
_DETERMINISTIC_RULES: list[tuple[str, re.Pattern[str]]] = [
    ("git_status", re.compile(r"\bgit status\b|\bstatus of the (repo|repository)\b")),
    ("git_diff", re.compile(r"\bgit diff\b|\bshow (me )?the diff\b")),
    ("copy_file", re.compile(r"\bcopy (the )?(file|files)\b")),
    ("parse_json", re.compile(r"\bparse (the )?json\b|\bvalidate (the )?json\b")),
    ("run_tests", re.compile(r"\brun (the )?(unit )?tests?\b|\bexecute (the )?tests?\b")),
    ("compare_hashes", re.compile(r"\bcompare (the )?hash(es)?\b|\bchecksum\b|\bsha256\b")),
    ("regex_replace", re.compile(r"\bregex replace\b|\bsearch and replace\b|\bsed\b")),
    ("count_files", re.compile(r"\bcount (the )?(files|lines|matches)\b|\bhow many files\b")),
    ("list_directory", re.compile(r"\blist (the )?(directory|folder|files in)\b")),
    ("search_files", re.compile(r"\b(grep|ripgrep|search the repo(sitory)? for)\b")),
    ("build_report", re.compile(r"\b(usage|cost) report\b|\breport from known fields\b")),
]

# -- REASONING: judgement, architecture, ambiguity ----------------------------
_REASONING_TERMS = [
    "architecture",
    "architect",
    "rca",
    "root cause",
    "design the",
    "system design",
    "trade-?off",
    "security",
    "threat model",
    "economy balance",
    "game economy",
    "conflicting requirement",
    "ambiguous",
    "decide (whether|if|between)",
    "which approach",
    "strategy for",
    "should we",
    "why (is|does|did)",
    "complex (bug|debug)",
    "migration plan",
    "refactor(ing)? strategy",
]

# -- WORKER: mechanical execution --------------------------------------------
_WORKER_TERMS = [
    "docstring",
    "document(ation)?",
    "readme",
    "comment",
    "format(ting)?",
    "rename",
    "boilerplate",
    "localis|localiz",
    "translate",
    "generate (a )?(unit )?tests?",
    "write (a )?tests?",
    "add (a )?(null )?(check|guard|validation)",
    "extract (the )?(method|constant)",
    "tidy",
    "clean ?up",
    "summari[sz]e (the )?(log|file|diff)",
    "convert (the )?(json|yaml|csv|markdown)",
    "update (the )?(json|yaml|markdown|md) ",
    "simple refactor",
    "mechanical",
]

# -- REASONING_THEN_WORKER: substantial development ---------------------------
_SUBSTANTIAL_TERMS = [
    "implement",
    "build (a|the) (feature|system|service)",
    "add (a|the) (feature|system|screen|panel)",
    "across (multiple|several) (files|modules)",
    "end[- ]to[- ]end",
    "new (subsystem|module|pipeline)",
    "fix (the )?(bug|failure|regression)",
]

_ESCALATION_TERMS = [
    "escalat",
    "worker failed",
    "failed twice",
    "needs? (a )?reasoner",
]


def _hits(text: str, terms: list[str]) -> list[str]:
    return [t for t in terms if re.search(t, text)]


class TaskRouter:
    """Classify a natural-language request into one of the five categories."""

    def classify(self, request: str) -> RoutingDecision:
        text = (request or "").strip().lower()
        if not text:
            return RoutingDecision(
                Classification.REASONING, "empty request cannot be executed blind"
            )

        escalation = _hits(text, _ESCALATION_TERMS)
        if escalation:
            return RoutingDecision(
                Classification.ESCALATION,
                "request names a prior worker failure or asks for escalation",
                escalation,
            )

        reasoning = _hits(text, _REASONING_TERMS)
        substantial = _hits(text, _SUBSTANTIAL_TERMS)
        worker = _hits(text, _WORKER_TERMS)

        # DETERMINISTIC wins only when nothing in the request asks for judgement
        # or authorship: "run the tests" is code; "decide why the tests fail" is
        # not, even though it contains "tests".
        if not reasoning and not substantial:
            for operation, pattern in _DETERMINISTIC_RULES:
                if pattern.search(text):
                    return RoutingDecision(
                        Classification.DETERMINISTIC,
                        f"conventional code performs '{operation}' reliably;"
                        " no model is required",
                        [pattern.pattern],
                        operation,
                    )

        if reasoning and worker and not substantial:
            return RoutingDecision(
                Classification.REASONING_THEN_WORKER,
                "request mixes a judgement call with mechanical execution",
                reasoning + worker,
            )
        if reasoning:
            return RoutingDecision(
                Classification.REASONING,
                "request requires judgement the worker must not make",
                reasoning,
            )
        if substantial:
            return RoutingDecision(
                Classification.REASONING_THEN_WORKER,
                "substantial development: plan first, then execute",
                substantial,
            )
        if worker:
            return RoutingDecision(
                Classification.WORKER,
                "mechanical execution inside explicit boundaries",
                worker,
            )

        # Unknown shape. Spend reasoning rather than let the worker invent
        # requirements (spec section 2: the worker must not redefine anything).
        return RoutingDecision(
            Classification.REASONING,
            "request shape is unrecognised; defaulting to the reasoner rather than"
            " letting the worker define its own requirements",
        )
