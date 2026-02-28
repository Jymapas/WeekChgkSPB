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
```

- `TELEGRAM_CHANNEL_POSTS_PER_WEEK` — expected number of scheduled posts each week (must match the number of days listed).
- `TELEGRAM_CHANNEL_POST_DAYS` — comma-separated list of days of week for publishing (case-insensitive).
- `TELEGRAM_CHANNEL_POST_TIME` — local server time (HH:mm) when the post should be published.
- `TELEGRAM_CHANNEL_TRIGGER_WINDOW_MINUTES` — optional grace period to catch up missed publishes (default 180 minutes).
