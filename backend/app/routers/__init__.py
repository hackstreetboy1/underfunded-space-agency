from fastapi import APIRouter

from app.routers import apod, explain, health, interpret, narrate, stt, tip, tts

ROUTERS: tuple[APIRouter, ...] = (
    health.router,
    stt.router,
    interpret.router,
    narrate.router,
    tip.router,
    explain.router,
    tts.router,
    apod.router,
)
