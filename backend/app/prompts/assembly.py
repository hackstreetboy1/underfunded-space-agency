"""Builds the user turn sent with each system prompt.

Every value is JSON-encoded inside a tagged block, with `<`, `>` and `&` escaped, so player
text can never close a block or pose as a new section of the prompt.
"""

import json
from typing import Any

from app.schemas import ExplainRequest, InterpretRequest, NarrateRequest, TipRequest


def _json(value: Any) -> str:
    encoded = json.dumps(value, ensure_ascii=False, separators=(",", ":"))
    return encoded.replace("&", "\\u0026").replace("<", "\\u003c").replace(">", "\\u003e")


def _block(tag: str, value: Any) -> str:
    return f"<{tag}>\n{_json(value)}\n</{tag}>"


def interpret_contents(request: InterpretRequest) -> str:
    context = request.model_dump(mode="json", exclude={"request_id", "player_text"})
    return "\n".join(
        (
            "Classify CAPCOM's message.",
            _block("context", context),
            _block("player_text", request.player_text),
        )
    )


def narrate_contents(request: NarrateRequest) -> str:
    persona = (
        request.persona.model_dump(mode="json")
        if request.persona is not None
        else {"id": request.crew_persona_id}
    )
    situation = {
        "event": request.event,
        "stress": request.stress,
        "facts": [fact.model_dump(mode="json", exclude_none=True) for fact in request.facts],
    }
    return "\n".join(
        ("Write the crew member's line.", _block("persona", persona), _block("situation", situation))
    )


def tip_contents(request: TipRequest) -> str:
    return "\n".join(
        (
            "Rephrase the hint for the requested stage.",
            _block("request", request.model_dump(mode="json", exclude={"request_id"})),
        )
    )


def explain_contents(request: ExplainRequest) -> str:
    excerpts = [excerpt.model_dump(mode="json") for excerpt in request.excerpts]
    return "\n".join(
        ("Answer the question.", _block("excerpts", excerpts), _block("question", request.question))
    )
