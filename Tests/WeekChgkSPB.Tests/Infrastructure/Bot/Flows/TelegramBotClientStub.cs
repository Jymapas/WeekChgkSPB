using System;
using System.Reflection;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace WeekChgkSPB.Tests.Infrastructure.Bot.Flows;

internal class TelegramBotClientStub : DispatchProxy
{
    public static ITelegramBotClient Create() => DispatchProxy.Create<ITelegramBotClient, TelegramBotClientStub>();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
        {
            return null;
        }

        var returnType = targetMethod.ReturnType;

        if (targetMethod.Name == "get_BotId")
        {
            return 0L;
        }

        if (returnType == typeof(Task))
        {
            return Task.CompletedTask;
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GenericTypeArguments[0];

            if (resultType == typeof(Message))
            {
                return Task.FromResult(new Message());
            }

            var defaultValue = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
            return typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, new[] { defaultValue });
        }

        return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
    }
}
