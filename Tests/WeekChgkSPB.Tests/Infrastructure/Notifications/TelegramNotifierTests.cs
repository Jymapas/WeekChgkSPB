using System.Linq;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Tests.Infrastructure.Notifications;

public sealed class TelegramNotifierTests
{
    [Fact]
    public void BuildAdminEditKeyboard_ContainsCallbacksForAllAnnouncementFields()
    {
        var keyboard = TelegramNotifier.BuildAdminEditKeyboard(42);

        var callbacks = keyboard.InlineKeyboard
            .SelectMany(row => row)
            .Select(button => button.CallbackData)
            .ToList();

        Assert.Equal(
            [
                "admedit_name_42",
                "admedit_datetime_42",
                "admedit_place_42",
                "admedit_cost_42"
            ],
            callbacks);
    }
}
