using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Telegram.Bot.Types;
using WeekChgkSPB.Infrastructure.Bot;

namespace WeekChgkSPB.Tests.Infrastructure.Bot;

public class BotCommandsTests
{
    [Fact]
    public void AsBotCommands_ExcludesCommandsWithoutDescription()
    {
        var field = typeof(BotCommands).GetField("CustomDescriptions", BindingFlags.NonPublic | BindingFlags.Static)
                    ?? throw new InvalidOperationException("CustomDescriptions field not found");

        var map = (Dictionary<string, string>)field.GetValue(null)!;
        lock (map)
        {
            const string target = BotCommands.Edit;
            var hasOriginal = map.TryGetValue(target, out var originalDescription);
            if (!hasOriginal)
            {
                throw new InvalidOperationException("Expected description missing");
            }

            map.Remove(target);
            try
            {
                var commands = BotCommands.AsBotCommands();
                Assert.DoesNotContain(commands, c => string.Equals(c.Command, target.TrimStart('/'), StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                map[target] = originalDescription!;
            }
        }
    }

    [Fact]
    public void AsBotCommands_MapsCommandNamesAndDescriptions()
    {
        var commands = BotCommands.AsBotCommands();
        var makePost = Assert.Single(commands, c => c.Command == BotCommands.MakePost.TrimStart('/'));

        Assert.Equal("Создать пост", makePost.Description);
        Assert.Equal(commands.Count, commands.Select(c => c.Command).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
