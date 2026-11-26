namespace WeekChgkSPB;

internal sealed record LiveJournalUserLink(
    long TelegramUserId,
    long LjUserId,
    string LjUsername,
    long? LinkedByUserId,
    DateTime LinkedAtUtc,
    bool IsAllowed,
    bool IsBanned);
