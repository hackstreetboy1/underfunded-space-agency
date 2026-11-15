import asyncio
import logging
import time
from collections.abc import Mapping
from dataclasses import dataclass
from typing import Final, TypeVar

import httpx
from google import genai
from google.genai import errors, types
from pydantic import BaseModel, ValidationError

from app.config import Settings, ThinkingLevel
from app.limits import LATENCY_BUDGET_S, Endpoint
from app.prompts.assembly import explain_contents, interpret_contents, narrate_contents, tip_contents
from app.providers.base import ProviderError
from app.providers.circuit import ModelCircuit
from app.providers.probe import CachedProbe, Clock
from app.schemas import (
    ExplainRequest,
    ExplainResponse,
    InterpretRequest,
    InterpretResponse,
    NarrateRequest,
    NarrateResponse,
    ServiceStatus,
    TipRequest,
    TipResponse,
)

log = logging.getLogger("usa.backend")

M = TypeVar("M", bound=BaseModel)

# The router's latency budget is the real limit; this only stops a hung socket.
_HTTP_TIMEOUT_MS: Final = 15_000
# Idle connections outlive the 30 s health probe, so the probes keep the TLS connection warm.
KEEPALIVE_EXPIRY_S: Final = 120.0
_RETRYABLE_CODES: Final = frozenset({408, 429, 500, 502, 503, 504})
_MISCONFIGURED_CODES: Final = frozenset({400, 401, 403, 404})
# A wrong or retired model id is specific to that model, so the next one in the chain is still worth a try.
_MODEL_SPECIFIC_CODES: Final = _RETRYABLE_CODES | {404}
# Cap on every attempt but the last, so an overloaded model leaves time for the fallbacks.
FIRST_ATTEMPT_S: Final = 2.5
# Stop slightly before the router's budget so the chain's own typed error is the one returned.
_BUDGET_MARGIN_S: Final = 0.1
# Not worth starting a call with less time than this left.
_MIN_ATTEMPT_S: Final = 0.3


@dataclass(frozen=True, slots=True)
class GenerationLimits:
    temperature: float
    max_output_tokens: int


# Output caps include thinking tokens, so they leave headroom over the longest valid answer.
GENERATION_LIMITS: Final[dict[str, GenerationLimits]] = {
    "interpret": GenerationLimits(temperature=0.35, max_output_tokens=700),
    "narrate": GenerationLimits(temperature=0.4, max_output_tokens=400),
    "tip": GenerationLimits(temperature=0.2, max_output_tokens=400),
    "explain": GenerationLimits(temperature=0.2, max_output_tokens=900),
}


def generation_config(
    schema: type[BaseModel], system_prompt: str, limits: GenerationLimits, thinking_level: ThinkingLevel | None
) -> types.GenerateContentConfig:
    thinking = (
        types.ThinkingConfig(thinking_budget=0)
        if thinking_level is None
        else types.ThinkingConfig(thinking_level=types.ThinkingLevel(thinking_level.upper()))
    )
    return types.GenerateContentConfig(
        system_instruction=system_prompt,
        response_mime_type="application/json",
        response_schema=schema,
        temperature=limits.temperature,
        max_output_tokens=limits.max_output_tokens,
        candidate_count=1,
        thinking_config=thinking,
    )


def _retryable(code: int | None) -> bool:
    return code in _RETRYABLE_CODES


class _ModelUnavailable(ProviderError):
    """This model is overloaded, unreachable or unknown; another model may still answer."""


class GeminiLanguageModel:
    """Structured-output calls to the configured Flash-tier models. Never sees numeric telemetry.

    Each call walks the model chain (`Settings.gemini_models`) within the endpoint's latency budget:
    every attempt but the last is capped at `first_attempt_s`, and a model that answers 503/429 or
    times out is tried last for the next `CIRCUIT_OPEN_S` (`ModelCircuit`).
    """

    def __init__(
        self,
        settings: Settings,
        client: genai.Client | None = None,
        clock: Clock = time.monotonic,
        *,
        budgets_s: Mapping[Endpoint, float] = LATENCY_BUDGET_S,
        first_attempt_s: float = FIRST_ATTEMPT_S,
    ) -> None:
        self._client = client or genai.Client(
            api_key=settings.gemini_api_key.get_secret_value(),
            http_options=types.HttpOptions(
                timeout=_HTTP_TIMEOUT_MS,
                async_client_args={"limits": httpx.Limits(keepalive_expiry=KEEPALIVE_EXPIRY_S)},
            ),
        )
        self._model = settings.gemini_model
        self._models = settings.gemini_models
        self._thinking_levels = {model: settings.thinking_level_for(model) for model in self._models}
        self._budgets_s = budgets_s
        self._first_attempt_s = first_attempt_s
        self._circuit = ModelCircuit(clock=clock)
        self._probe = CachedProbe(self._check, clock=clock)

    @property
    def model(self) -> str:
        return self._model

    async def interpret(self, request: InterpretRequest, system_prompt: str) -> InterpretResponse:
        return await self._generate("interpret", InterpretResponse, system_prompt, interpret_contents(request))

    async def narrate(self, request: NarrateRequest, system_prompt: str) -> NarrateResponse:
        return await self._generate("narrate", NarrateResponse, system_prompt, narrate_contents(request))

    async def tip(self, request: TipRequest, system_prompt: str) -> TipResponse:
        draft = await self._generate("tip", TipResponse, system_prompt, tip_contents(request))
        return authored_tip(request, draft)

    async def explain(self, request: ExplainRequest, system_prompt: str) -> ExplainResponse:
        answer = await self._generate("explain", ExplainResponse, system_prompt, explain_contents(request))
        supplied = {excerpt.id for excerpt in request.excerpts}
        return ExplainResponse(answer=answer.answer, sources=[s for s in answer.sources if s in supplied])

    async def health(self) -> ServiceStatus:
        return await self._probe.status()

    async def _check(self) -> ServiceStatus:
        """Metadata lookup for the configured model: no tokens generated, no quota spent."""
        try:
            await self._client.aio.models.get(model=self._model)
        except errors.APIError as exc:
            log.warning("gemini probe failed code=%s", exc.code)
            if exc.code == 429:
                return "degraded"
            return "unconfigured" if exc.code in _MISCONFIGURED_CODES else "down"
        return "ok"

    async def _generate(self, name: Endpoint, schema: type[M], system_prompt: str, contents: str) -> M:
        """Tries each model in circuit order until one answers or the budget runs out.

        Raises `TimeoutError` when every attempt timed out, otherwise `ProviderError` (retryable if any
        failure was). A blocked prompt or a rejected key stops the chain: another model won't help.
        """
        loop = asyncio.get_running_loop()
        deadline = loop.time() + self._budgets_s[name] - _BUDGET_MARGIN_S
        candidates = self._circuit.order(self._models)
        timed_out = 0
        retryable = False
        for index, model in enumerate(candidates):
            remaining = deadline - loop.time()
            if remaining < _MIN_ATTEMPT_S:
                break
            attempt_s = remaining if index == len(candidates) - 1 else min(self._first_attempt_s, remaining)
            log.info("gemini %s attempt=%d model=%s timeout_s=%.1f", name, index + 1, model, attempt_s)
            started = loop.time()
            try:
                async with asyncio.timeout(attempt_s):
                    result = await self._attempt(name, model, schema, system_prompt, contents)
            except TimeoutError:
                log.warning("gemini %s model=%s timed out after %.1f s", name, model, attempt_s)
                self._circuit.trip(model)
                timed_out += 1
                continue
            except _ModelUnavailable as exc:
                self._circuit.trip(model)
                retryable = retryable or exc.retryable
                continue
            except ProviderError as exc:
                if not exc.retryable:
                    raise
                retryable = True
                continue
            self._circuit.close(model)
            elapsed_ms = (loop.time() - started) * 1000
            log.info("gemini %s answered model=%s attempt=%d ms=%.0f", name, model, index + 1, elapsed_ms)
            return result
        if timed_out and not retryable:
            raise TimeoutError
        raise ProviderError(retryable=retryable or timed_out > 0)

    async def _attempt(self, name: str, model: str, schema: type[M], system_prompt: str, contents: str) -> M:
        config = generation_config(schema, system_prompt, GENERATION_LIMITS[name], self._thinking_levels[model])
        try:
            response = await self._client.aio.models.generate_content(model=model, contents=contents, config=config)
        except errors.APIError as exc:
            log.warning("gemini %s model=%s failed code=%s", name, model, exc.code)
            if exc.code in _MODEL_SPECIFIC_CODES:
                raise _ModelUnavailable(retryable=_retryable(exc.code)) from exc
            raise ProviderError(retryable=_retryable(exc.code)) from exc
        except httpx.TimeoutException as exc:
            raise TimeoutError from exc
        except httpx.TransportError as exc:
            log.warning("gemini %s model=%s transport error type=%s", name, model, type(exc).__name__)
            raise _ModelUnavailable(retryable=True) from exc
        except TimeoutError:
            raise
        except Exception as exc:
            # Anything else the SDK raises (an unparsable HTTP body, another transport) is a provider
            # failure, never a 500. Only the type is logged: SDK messages can carry request details.
            log.warning("gemini %s model=%s failed type=%s", name, model, type(exc).__name__)
            raise ProviderError(retryable=True) from exc
        blocked = block_reason(response)
        if blocked is not None:
            # The same input is blocked again on retry, so the client should fall back at once.
            log.warning("gemini %s model=%s blocked reason=%s", name, model, blocked)
            raise ProviderError(retryable=False)
        try:
            text = response.text
        except Exception as exc:
            log.warning("gemini %s model=%s response unreadable type=%s", name, model, type(exc).__name__)
            raise ProviderError(retryable=True) from exc
        if not text:
            log.warning("gemini %s model=%s returned no text", name, model)
            raise ProviderError(retryable=True)
        try:
            return schema.model_validate_json(text)
        except ValidationError as exc:
            log.warning("gemini %s model=%s returned off-schema output", name, model)
            raise ProviderError(retryable=True) from exc


_BLOCKED_FINISH_REASONS: Final = frozenset(
    {"SAFETY", "RECITATION", "BLOCKLIST", "PROHIBITED_CONTENT", "SPII", "IMAGE_SAFETY", "LANGUAGE"}
)


def _name(value: object) -> str:
    return str(getattr(value, "name", None) or value or "").upper()


def block_reason(response: object) -> str | None:
    """Why the prompt or the first candidate was blocked, or None. Tolerates missing fields."""
    feedback = getattr(response, "prompt_feedback", None)
    prompt_block = _name(getattr(feedback, "block_reason", None))
    if prompt_block and prompt_block != "BLOCKED_REASON_UNSPECIFIED":
        return prompt_block
    candidates = getattr(response, "candidates", None) or []
    finish = _name(getattr(candidates[0], "finish_reason", None)) if candidates else ""
    return finish if finish in _BLOCKED_FINISH_REASONS else None


def authored_tip(request: TipRequest, draft: TipResponse) -> TipResponse:
    """Keeps only the model's wording; id, stage and refs always come from the authored hint."""
    by_id = {candidate.tip_id: candidate for candidate in request.candidates}
    chosen = by_id.get(draft.tip_id)
    if chosen is None or chosen.stage != request.stage:
        chosen = next((c for c in request.candidates if c.stage == request.stage), request.candidates[0])
    text = draft.text.strip() if draft.tip_id == chosen.tip_id else ""
    return TipResponse(
        tip_id=chosen.tip_id,
        stage=request.stage,
        text=text or chosen.text,
        fact_refs=list(chosen.fact_refs),
        allowed_action_refs=list(chosen.allowed_action_refs),
    )
