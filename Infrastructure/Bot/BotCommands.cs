using System.Collections.Generic;
using System.Linq;
using Telegram.Bot.Types;

namespace WeekChgkSPB.Infrastructure.Bot;

public static class BotCommands
{
    public const string MakePostLJ = "/makepostlj";
    public const string MakePost = "/makepost";
    public const string Add = "/add";
    public const string AddLines = "/add_lines";
    public const string Help = "/help";
    public const string Cancel = "/cancel";
    public const string Edit = "/edit";
    public const string EditName = "/edit_name";
    public const string EditPlace = "/edit_place";
    public const string EditDateTime = "/edit_datetime";
    public const string EditCost = "/edit_cost";
    public const string Delete = "/delete";
    public const string FooterAdd = "/footer_add";
    public const string FooterList = "/footer_list";
    public const string FooterDel = "/footer_del";

    public static readonly string[] AdminOnly =
    [
        MakePostLJ,
        MakePost,
        FooterAdd,
        FooterList,
        FooterDel,
    ];

    public static readonly string[] User =
    [
        Help,
        Cancel,
        Add,
        AddLines,
        Edit,
        EditName,
        EditPlace,
        EditDateTime,
        EditCost,
        Delete,
    ];

    public static readonly string[] All =
    [
        ..User,
        ..AdminOnly,
    ];

    private readonly static Dictionary<string, string> CustomDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        [MakePost] = "Создать пост",
        [MakePostLJ] = "Создать пост для LiveJournal",
        [Add] = "Добавить анонс единым блоком",
        [AddLines] = "Добавить анонс по шагам",
        [Help] = "Справка по командам",
        [Cancel] = "Отменить текущее действие",
        [Edit] = "Редактировать анонс",
        [EditName] = "Изменить название анонса",
        [EditPlace] = "Изменить место проведения",
        [EditDateTime] = "Изменить дату и время",
        [EditCost] = "Изменить стоимость",
        [Delete] = "Удалить анонс",
        [FooterAdd] = "Добавить футер",
        [FooterList] = "Список футеров",
        [FooterDel] = "Удалить футер",
    };

    public static IReadOnlyList<BotCommand> AsAdminBotCommands() => ToBotCommands(All);

    public static IReadOnlyList<BotCommand> AsUserBotCommands() => ToBotCommands(User);

    public static IReadOnlyList<BotCommand> AsBotCommands() => AsAdminBotCommands();

    private static IReadOnlyList<BotCommand> ToBotCommands(IEnumerable<string> source) =>
        source
            .Select(c =>
            {
                var cmd = c.TrimStart('/');
                if (CustomDescriptions.TryGetValue(c, out var desc) && !string.IsNullOrWhiteSpace(desc))
                    return new BotCommand { Command = cmd, Description = desc };
                return null;
            })
            .Where(static c => c is not null)
            .Select(static c => c!)
            .ToArray();
}
