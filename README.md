# WeekChgkSPB

## .env example
```yml
TELEGRAM_BOT_TOKEN=123456789:AAaa1122BBbb
TELEGRAM_CHAT_ID=-4815162342
TELEGRAM_CHANNEL_ID=@bladedriver
TELEGRAM_CHANNEL_POSTS_PER_WEEK=2
TELEGRAM_CHANNEL_POST_DAYS=Monday,Thursday
TELEGRAM_CHANNEL_POST_TIME=12:00
TELEGRAM_CHANNEL_TRIGGER_WINDOW_MINUTES=180
DB_PATH=/data/posts.db

# Qwen auto parsing: off | shadow | active (default: off)
ANNOUNCEMENT_AUTO_PARSE_MODE=off
QWEN_API_KEY=sk-...
QWEN_API_BASE_URL=https://dashscope-intl.aliyuncs.com/compatible-mode/v1/
QWEN_MODEL=qwen3.5-flash-2026-02-23
QWEN_TIMEOUT_SECONDS=30
```

- `TELEGRAM_CHANNEL_POSTS_PER_WEEK` — expected number of scheduled posts each week (must match the number of days listed).
- `TELEGRAM_CHANNEL_POST_DAYS` — comma-separated list of days of week for publishing (case-insensitive).
- `TELEGRAM_CHANNEL_POST_TIME` — local server time (HH:mm) when the post should be published.
- `TELEGRAM_CHANNEL_TRIGGER_WINDOW_MINUTES` — optional grace period to catch up missed publishes (default 180 minutes).
- `ANNOUNCEMENT_AUTO_PARSE_MODE` — `off` keeps the old manual flow, `shadow` shows a candidate and then the old three messages, `active` saves only a fully validated candidate.
- `QWEN_API_KEY` — Alibaba Cloud Model Studio International API key; required only for `shadow` and `active`.
- `QWEN_API_BASE_URL` — HTTPS OpenAI-compatible endpoint under `aliyuncs.com`.
- `QWEN_MODEL` — defaults to the pinned `qwen3.5-flash-2026-02-23` snapshot.
- `QWEN_TIMEOUT_SECONDS` — one-request timeout from 1 to 30 seconds; there are no retries.

The bot extracts the standard full-team price locally. Price blocks, contacts, registration instructions and other RSS body sections are not sent to Qwen. Any ambiguous price, API/JSON error, or validation mismatch falls back to the manual flow. Start production evaluation in `shadow`; switch to `active` only after the agreed offline/shadow gates are met.

When `shadow` or `active` starts, posts still present in RSS that already exist in `posts` but have neither an announcement nor an `announcement_parse_attempts` entry are processed as backlog. Each post is attempted only once. In `off`, previously notified posts are not sent again.
