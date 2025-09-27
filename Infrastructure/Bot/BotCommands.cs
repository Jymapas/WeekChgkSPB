using Telegram.Bot.Types;

namespace WeekChgkSPB.Infrastructure.Bot;

public static class BotCommands
{
    public const string MakePostLJ = "/makepostlj";
    public const string MakePost = "/makepost";
    public const string Add = "/add";
    public const string AddLines = "/add_lines";
    public const string Edit = "/edit";
    public const string EditName = "/edit_name";
    public const string EditPlace = "/edit_place";
    public const string EditDateTime = "/edit_datetime";
    public const string EditCost = "/edit_cost";
    public const string FooterAdd = "/footer_add";
    public const string FooterList = "/footer_list";
    public const string FooterDel = "/footer_del";

    public static readonly string[] All =
    [
        MakePostLJ,
        MakePost,
        Add,
        AddLines,
        Edit,
        EditName,
        EditPlace,
        EditDateTime,
        EditCost,
        FooterAdd,
        FooterList,
        FooterDel,
    ];

    private readonly static Dictionary<string, string> CustomDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        [MakePostLJ] = "Создать пост для LiveJournal",
        [MakePost] = "Создать пост",
        [Add] = "Добавить анонс единым блоком",
        [AddLines] = "Добавить анонс по шагам",
        [Edit] = "Редактировать анонс",
        [EditName] = "Изменить название анонса",
        [EditPlace] = "Изменить место проведения",
        [EditDateTime] = "Изменить дату и время",
        [EditCost] = "Изменить стоимость",
        [FooterAdd] = "Добавить футер",
        [FooterList] = "Список футеров",
        [FooterDel] = "Удалить футер",
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
