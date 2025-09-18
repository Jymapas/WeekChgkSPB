using Telegram.Bot.Types;

namespace WeekChgkSPB.Infrastructure.Bot;

public static class BotCommands
{
    public const string MakePostLJ = "/makepostlj";
    public const string MakePost = "/makepost";
    public const string Add = "/add";
    public const string FooterAdd = "/footer_add";
    public const string FooterList = "/footer_list";
    public const string FooterDel = "/footer_del";

    public static readonly string[] All =
    [
        MakePostLJ,
        MakePost,
        Add,
        FooterAdd,
        FooterList,
        FooterDel,
    ];

    private readonly static Dictionary<string,string> CustomDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        [MakePostLJ] = "Создать пост для LiveJournal",
        [MakePost]   = "Создать пост",
        [Add]        = "Добавить анонс",
        [FooterAdd]  = "Добавить футер",
        [FooterList] = "Список футеров",
        [FooterDel]  = "Удалить футер",
    };

    public static BotCommand?[] AsBotCommands() =>
        All.Select(c =>
            {
                var cmd = c.TrimStart('/');
                if (CustomDescriptions.TryGetValue(c, out var desc) && !string.IsNullOrWhiteSpace(desc))
                    return new BotCommand { Command = cmd, Description = desc };
                return null;
            }).ToArray();
}