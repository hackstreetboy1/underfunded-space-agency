from pydantic import BaseModel

from app.schemas.common import ProviderMode, ServiceStatus


class HealthResponse(BaseModel):
    backend: ServiceStatus
    gemini: ServiceStatus
    elevenlabs: ServiceStatus
    # Additive: older clients ignore it. Defaults to "live" so a peer that omits it
    # is never mistaken for fakes.
    mode: ProviderMode = "live"
