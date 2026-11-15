import re
from functools import cache
from pathlib import Path
from typing import Final, Literal

PromptName = Literal["interpret", "narrate", "tip", "explain"]

_PROMPT_DIR: Final = Path(__file__).resolve().parent
_HEADER_COMMENT: Final = re.compile(r"\A\s*<!--.*?-->\s*", re.DOTALL)
# Shared rules every prompt carries, kept in one file so the four prompts can't drift apart.
NAMES_PLACEHOLDER: Final = "{{names}}"


def _read(name: str) -> str:
    text = (_PROMPT_DIR / f"{name}.md").read_text(encoding="utf-8")
    return _HEADER_COMMENT.sub("", text, count=1).strip()


@cache
def load_prompt(name: PromptName) -> str:
    """System prompt text with the shared naming rules filled in; the leading `<!-- version -->`
    comment is for maintainers only."""
    text = _read(name)
    if NAMES_PLACEHOLDER not in text:
        raise ValueError(f"prompt {name} is missing the {NAMES_PLACEHOLDER} rules")
    return text.replace(NAMES_PLACEHOLDER, _read("names"))
