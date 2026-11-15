import time
from collections.abc import Callable, Sequence
from typing import Final

# Long enough to ride out a capacity spike, short enough to return to the preferred model soon after.
CIRCUIT_OPEN_S: Final = 60.0

Clock = Callable[[], float]


class ModelCircuit:
    """Remembers models that recently failed for capacity reasons (503, 429, timeout).

    An open model is not skipped outright: it moves to the back of the order, so a request still
    tries it when every other model has failed too.
    """

    def __init__(self, open_s: float = CIRCUIT_OPEN_S, clock: Clock = time.monotonic) -> None:
        self._open_s = open_s
        self._clock = clock
        self._open_until: dict[str, float] = {}

    def trip(self, model: str) -> None:
        self._open_until[model] = self._clock() + self._open_s

    def close(self, model: str) -> None:
        self._open_until.pop(model, None)

    def is_open(self, model: str) -> bool:
        until = self._open_until.get(model)
        if until is None:
            return False
        if self._clock() >= until:
            del self._open_until[model]
            return False
        return True

    def order(self, models: Sequence[str]) -> list[str]:
        """Closed models in configured order, then open ones, soonest to reopen first."""
        closed = [model for model in models if not self.is_open(model)]
        tripped = sorted((model for model in models if self.is_open(model)), key=self._open_until.__getitem__)
        return closed + tripped
