# Backend (local AI proxy)

FastAPI service on `127.0.0.1:8787` that sits between the Godot client and Gemini / ElevenLabs. It classifies and phrases; Godot owns all game state (GDD Section 15). Layout, schemas and endpoints are specified in `docs/SCRIPTING.md` Section 14.

Status (Phase 3 providers): `GeminiLanguageModel` (structured output, `response_schema` = the Pydantic response models) and the ElevenLabs TTS (streamed MP3) and Scribe STT providers are live behind `USE_FAKE_PROVIDERS=false`. Fakes stay the default and are what the tests use.

## Models

Set in `.env`; an empty value uses the default below (`app/config.py`).

| Setting | Default | Why |
|---|---|---|
| `GEMINI_MODEL` | `gemini-3.5-flash-lite` | Stable Flash-tier (Lite) model, thinking `minimal`; fastest correct structured interpret in the comparison below. |
| `GEMINI_MODEL_FALLBACKS` | `gemini-3.1-flash-lite,gemini-2.5-flash-lite` | Comma list tried in order when the primary answers 503/429/404, fails in transport or misses its attempt timeout (`none` turns it off). Every attempt but the last is capped at 2.5 s, all inside the endpoint budget. A model that answered 503/429 or timed out is tried last for 60 s (`app/providers/circuit.py`). Chosen 2026-11-15 from the account's `models.list`: fast text models from other generations than the primary; `gemini-3.6-flash` was too slow (5 s). Gemini 2.5 models get a thinking budget of 0 instead of a level. The log names the model that answered. |
| `GEMINI_THINKING_LEVEL` (optional) | `minimal` for models that accept it (`gemini-3.5-flash-lite`, `gemini-3.6-flash`, `gemini-3.1-flash-lite`, `gemini-3-flash-preview`), otherwise `low` | Thinking tokens count against the output cap and add latency. `gemini-3.8-flash` rejects `minimal`. |
| `ELEVENLABS_TTS_MODEL` | `eleven_flash_v2_5` | Low-latency Flash model (ElevenLabs recommends Flash over the deprecated Turbo v2/v2.5). |
| `ELEVENLABS_STT_MODEL` | `scribe_v3` | Current batch Scribe model (`scribe_v1` is deprecated). |

Model ids were checked against the provider model pages on 2026-11-14.

### Interpret latency (2026-11-14, live)

Same small interpret request (two crew, two actions, one fact), thinking `minimal`, connection warmed by a metadata lookup first:

| Model | Interpret latency | Structure correct |
|---|---|---|
| `gemini-3.6-flash` | 5.26 s | yes |
| `gemini-3.5-flash-lite` | 0.81 s | yes |

Earlier the same day, `gemini-3.6-flash` took 5.06 s cold, and one end-to-end `/v1/interpret` with it exceeded the old 3 s budget (504). Hence the Flash-Lite default and the 6 s budgets below.

## Latency budgets and client timeouts

Server budgets (`app/limits.py`); over budget returns 504 `provider_timeout` (retryable):

| Endpoint | Server budget | Godot client timeout (must be longer) |
|---|---|---|
| `/v1/interpret` | 7 s (room for the model fallback chain) | 9 s |
| `/v1/narrate` | 6 s | 8 s (SCRIPTING 13.1 still says 6 s: raise it) |
| `/v1/tip` | 6 s | 8 s |
| `/v1/explain` | 4 s | 6 s |
| `/v1/stt` | 2.5 s | 4 s |
| `/v1/tts` | 2 s to first byte, 20 s for the whole stream | 4 s to first byte |
| `/health` | probes cached 30 s | 1 s |

The client retries with the same `request_id`, which the backend answers from its idempotency cache.

## Connection warm-up

At startup the backend awaits one round of health probes (Gemini `models.get`, ElevenLabs subscription lookup; never a generation call). This opens the TLS connections before the first real request. Both SDK clients keep idle connections for 120 s, and the 30 s health probe refresh (driven by the client's 5 s heartbeat) keeps them warm while a game is running.

## Speech-to-text response

`{transcript, confidence, min_word_confidence, low_confidence_words}`. `confidence` is the mean word probability; `min_word_confidence` is the weakest spoken word; `low_confidence_words` lists words below 0.6. The client forces transcript review when `min_word_confidence < 0.6`. With no word timings, both scores fall back to `language_probability`.

Generation settings per endpoint live in `app/providers/gemini.py` (`GENERATION_LIMITS`): temperature 0.1-0.4, output caps 400-900 tokens including thinking. TTS output is `mp3_44100_64`. STT confidence is the mean word probability (`exp(logprob)` over `type == "word"` entries), falling back to `language_probability` when no words come back.

## Prompts

`app/prompts/{interpret,narrate,tip,explain}.md` are the system prompts; the first-line HTML comment carries the version and is stripped before sending. Each prompt has a `{{names}}` line that `load_prompt` replaces with `app/prompts/names.md`, the shared rules: the player is CAPCOM ("Flight" is the Flight Director, Dee Marsh), the agency is the United Space Administration, no real program, spacecraft, rocket, agency, place or person names (NASA, Gemini, Agena, Apollo, Houston...), and no mention of AI or the services behind the game. Clarification options are plain-language choices, never action ids. `app/prompts/assembly.py` builds the user turn: every value is JSON inside a tagged block (`<context>`, `<player_text>`, `<persona>`, `<situation>`, `<request>`, `<excerpts>`, `<question>`) with `<`, `>` and `&` escaped, so player text can't close a block. Provider-side guards: tip responses keep only the model's wording (id, stage and refs come from the authored candidate), and explain drops sources that weren't supplied. Everything else is validated by Godot (SCRIPTING 13.2).

`/v1/narrate` accepts an optional `persona` (`name`, `role`, `personality`, `cadence`, `warmth`, `humor`, `stress_expression`, `sample_lines`) and `stress` (`calm` / `tense` / `critical`). Both are optional, so older clients still validate.

`/v1/interpret` (prompt v5) also takes optional `personas` (each crew member's persona plus `id` and current `stress`) and `readable_facts` (id and label of every gauge the crew can read right now). CAPCOM can phrase things any way at all; the reply is written in the addressed crew member's voice. When a reply names a readable gauge with a `<FACT:id>` token, the game has that crew member read it (the glance read, a few seconds of mission time), then speaks the line with the real value; four or more values get an in-character lead-in followed by the labelled values. Clarifications are spoken questions answered in free words: no tap-to-pick replies.

## TTS phrase cache

`/v1/tts` streams audio as it arrives. Requests with `"cacheable": true` (fixed acknowledgements, greetings) are stored on disk at `backend/cache/tts/<sha256(voice_id + model + text)>.mp3` (git-ignored) and replayed without a provider call. Only complete clips are cached. A stream that fails before the first byte returns the usual error JSON; a failure after it truncates the response and caches nothing.

## Astronomy Picture of the Day (loading screens)

`GET /v1/apod` returns the newest cached APOD image day (`date`, `title`, `explanation` cut to 400 characters, `media_type`, NASA's standard `url` for reference, `copyright`, `image_path`, `is_fallback`); `GET /v1/apod/image[?date=YYYY-MM-DD]` serves the cached JPEG/PNG. The game uses only these two paths and never contacts NASA. With real providers and `NASA_API_KEY` set, the proxy calls the APOD API at most once per UTC day for every launch together: `backend/cache/apod/last_attempt.json` is written before the call, whatever its outcome, then `<date>.json` and `<date>.<ext>` (standard `url`, never `hdurl`, max 5 MiB, JPEG or PNG from a `*.nasa.gov` host; newest 7 days kept). Video days, errors and oversized images serve the newest cached day (`is_fallback: true`); with nothing cached both paths answer 404 `apod_unavailable`. The key goes in the `X-Api-Key` header, never a URL. Fake providers (or a missing key) serve a bundled archive photo from memory and write nothing to the cache.

## Health

`/health` also reports `mode`: `"fake"` when every provider is a local stand-in, `"live"` when at least one real provider is wired (a fake standing in for a missing key still reports `unconfigured`). The field is additive: older clients ignore it, and a body without it reads as `"live"`. The client shows fake mode as practice mode on the main menu.

`/health` probes are metadata lookups, never generation: `models.get` for the Gemini model and `user/subscription` for ElevenLabs (`degraded` once the character quota is used up; a key scoped without `user_read` that still authenticates counts as `ok`). Each provider caches its probe for 30 s, STT and TTS share one ElevenLabs probe, and startup warms the cache so the first heartbeat is fast.

## Keys

API keys live only in `backend/.env`. Never copy them into code, docs, logs, tests, commits or the Godot client. `.env.example` lists the variable names with no values. `Settings` holds keys as `SecretStr`, and the request log records only request id, path, status and duration.

The log handlers (ours, uvicorn's and the root's) carry a redaction filter that scrubs the configured key values, `key=`/`api_key=`/`token=` query parameters and credential headers (`Authorization`, `xi-api-key`, `x-goog-api-key`) from messages, arguments and tracebacks; `httpx`, `httpcore` and the SDK loggers are held at WARNING so request URLs are never logged.

Without `backend/.env` the proxy starts with defaults (fake providers) and logs that the file is missing. An invalid value stops startup with the setting names only, never their values.

`USE_FAKE_PROVIDERS` defaults to `true`. With it `false`, a provider whose key is missing still falls back to a fake, and `/health` reports it as `unconfigured`.

## Set up

```powershell
cd backend
py -3 -m venv .venv
.venv\Scripts\python -m pip install -r requirements.txt
```

## Run

```powershell
backend\run_backend.ps1
```

Then `GET http://127.0.0.1:8787/health` returns `{"backend": "ok", "gemini": "ok", "elevenlabs": "ok", "mode": "fake"}` with fakes. The script survives having its output redirected (uvicorn logs to stderr). Development builds of the game start it themselves when nothing answers at boot (`USA_BACKEND_AUTOSTART=0` turns that off) and stop it on quit.

For a live run without editing `.env`, set `USE_FAKE_PROVIDERS=false` in the shell first; process environment variables override `.env`. The last live smoke is recorded in `docs/dev/LIVE_SMOKE.md`. Interactive API docs are at `/docs`.

## Test

From the project root:

```powershell
backend\.venv\Scripts\python -m pytest backend\tests -q
```

Tests build `Settings` with `_env_file=None`, so they never read the real `.env`. An autouse fixture blocks every non-loopback DNS lookup and socket connect, so no test can reach a provider; real-provider tests use the SDK stubs in `tests/stubs.py`.

## Contract notes

- Errors are always `{"error_code": ..., "retryable": ...}`; provider and validation text is never forwarded.
- `request_id` (every `POST`, JSON body or the `/v1/stt` form field) is 1-128 characters from `A-Z a-z 0-9 _ . : -`; anything else is 422 `invalid_request`.
- `POST` endpoints are idempotent per `request_id` for 10 minutes (per endpoint). Reusing an id with a different payload returns 409 `request_id_conflict`.
- Each endpoint has a token-bucket rate limit (`app/limits.py`); over the limit returns 429 `rate_limited` with `Retry-After`. Only requests that reach a provider are charged: 422/413/415 rejections, 409 conflicts, idempotent replays and TTS phrase-cache hits are free. A 429 is not cached, so a retry with the same `request_id` works once the bucket refills.
- `/v1/stt` takes multipart fields `audio` (WAV) and `request_id`. The WAV must have a `fmt ` and a `data` chunk (else 415); over 4 MiB or 30 s of audio is 413 `payload_too_large`.
- Request bodies are capped before parsing: 256 KiB for JSON endpoints, 4 MiB + 64 KiB for `/v1/stt` (413 `payload_too_large`).
- Local only: requests whose peer address, `Host` header or `Origin` header is not loopback get 403 `forbidden` (DNS rebinding and cross-site POSTs from a browser). The Godot client sends no `Origin`.
- A duplicate `request_id` that arrives while the first is still running waits for it and gets the same outcome (value or error); the provider is called and the rate limit charged once. This includes `/v1/tts` streams.
- `/v1/tts` `voice_id` must match `[A-Za-z0-9_-]{1,64}` (it becomes part of the provider URL); anything else is 422 `invalid_request`.
- Gemini responses blocked for safety are 502 `provider_error` with `retryable: false`; empty, truncated or off-schema output and any unexpected SDK failure are 502 with `retryable: true`.
- `FAKE_PROVIDER_DELAY_MS` (0-60000, default 0) makes every fake generation call (not `/health`) wait that long, so the 504 `provider_timeout` path can be tested over the wire. It applies only with `USE_FAKE_PROVIDERS=true`; set it in the process environment of a test server, never in `.env`.
