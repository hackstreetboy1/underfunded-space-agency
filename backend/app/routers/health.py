from fastapi import APIRouter

from app.deps import HealthDep
from app.schemas import HealthResponse

router = APIRouter(tags=["health"])


@router.get("/health", response_model=HealthResponse)
async def health(monitor: HealthDep) -> HealthResponse:
    return await monitor.current()
