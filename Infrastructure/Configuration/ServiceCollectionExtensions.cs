using System;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using WeekChgkSPB;
using WeekChgkSPB.Infrastructure.Bot;
using WeekChgkSPB.Infrastructure.Bot.Commands;
using WeekChgkSPB.Infrastructure.Bot.Flows;
using WeekChgkSPB.Infrastructure.Notifications;

namespace WeekChgkSPB.Infrastructure.Configuration;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWeekChgkSpbServices(
        this IServiceCollection services,
        AppSettings settings,
        string rssUrl)
    {
        services.AddSingleton(new PostsRepository(settings.DbPath));
        services.AddSingleton(new FootersRepository(settings.DbPath));
        services.AddSingleton(new AnnouncementsRepository(settings.DbPath));
        services.AddSingleton(new ChannelPostsRepository(settings.DbPath));
        services.AddSingleton(new UserManagementRepository(settings.DbPath));
        services.AddSingleton(new RssFetcher(rssUrl));
        services.AddSingleton(sp => new ModerationHandler(
            sp.GetRequiredService<ITelegramBotClient>(),
            sp.GetRequiredService<AnnouncementsRepository>(),
            sp.GetRequiredService<UserManagementRepository>(),
            sp.GetRequiredService<PostsRepository>(),
            sp.GetRequiredService<IChannelPostUpdater>(),
            settings.ChatId));
        services.AddSingleton<INotifier>(_ => new TelegramNotifier(settings.BotToken, settings.ChatId));
        services.AddSingleton(_ => new BotCommandHelper(PostFormatter.Moscow));
        services.AddSingleton<BotConversationState>();
        services.AddSingleton<IConversationFlowHandler, AddAnnouncementFlow>();
        services.AddSingleton<IConversationFlowHandler>(sp => new EditAnnouncementFlow(
            sp.GetRequiredService<IChannelPostUpdater>()));
        services.AddSingleton<IConversationFlowHandler, FooterFlow>();
        services.AddSingleton<IBotCommandHandler>(sp => new MakePostCommandHandler(BotCommands.MakePostLJ, true));
        services.AddSingleton<IBotCommandHandler>(sp => new MakePostCommandHandler(BotCommands.MakePost, false));
        services.AddSingleton<IBotCommandHandler, AddLinesCommandHandler>();
        services.AddSingleton<IBotCommandHandler, AddCommandHandler>();
        services.AddSingleton<IBotCommandHandler>(sp => new EditNameCommandHandler(
            sp.GetRequiredService<IChannelPostUpdater>()));
        services.AddSingleton<IBotCommandHandler>(sp => new EditPlaceCommandHandler(
            sp.GetRequiredService<IChannelPostUpdater>()));
        services.AddSingleton<IBotCommandHandler>(sp => new EditDateTimeCommandHandler(
            sp.GetRequiredService<IChannelPostUpdater>()));
        services.AddSingleton<IBotCommandHandler>(sp => new EditCostCommandHandler(
            sp.GetRequiredService<IChannelPostUpdater>()));
        services.AddSingleton<IBotCommandHandler, EditCommandHandler>();
        services.AddSingleton<IBotCommandHandler, DeleteAnnouncementCommandHandler>();
        services.AddSingleton<IBotCommandHandler, FooterAddCommandHandler>();
        services.AddSingleton<IBotCommandHandler, FooterListCommandHandler>();
        services.AddSingleton<IBotCommandHandler, FooterDeleteCommandHandler>();
        services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(settings.BotToken));
        services.AddSingleton<IChannelPostUpdater>(sp =>
        {
            if (!settings.HasChannel)
            {
                return new NoOpChannelPostUpdater();
            }

            return new ChannelPostUpdater(
                sp.GetRequiredService<AnnouncementsRepository>(),
                sp.GetRequiredService<FootersRepository>(),
                sp.GetRequiredService<ChannelPostsRepository>(),
                sp.GetRequiredService<ITelegramBotClient>(),
                settings.ChannelId!);
        });
        services.AddSingleton(sp => new BotRunner(
            sp.GetRequiredService<ITelegramBotClient>(),
            settings.ChatId,
            sp.GetRequiredService<PostsRepository>(),
            sp.GetRequiredService<AnnouncementsRepository>(),
            sp.GetRequiredService<FootersRepository>(),
            sp.GetRequiredService<UserManagementRepository>(),
            sp.GetRequiredService<ModerationHandler>(),
            sp.GetRequiredService<BotCommandHelper>(),
            sp.GetRequiredService<BotConversationState>(),
            sp.GetServices<IBotCommandHandler>(),
            sp.GetServices<IConversationFlowHandler>()));

        if (settings.HasScheduler)
        {
            var options = settings.ScheduleOptions!;
            var channelId = settings.ChannelId!;
            services.AddSingleton(options);
            services.AddSingleton(sp => new ScheduledPostPublisher(
                sp.GetRequiredService<AnnouncementsRepository>(),
                sp.GetRequiredService<FootersRepository>(),
                sp.GetRequiredService<ChannelPostsRepository>(),
                sp.GetRequiredService<PostsRepository>(),
                sp.GetRequiredService<ITelegramBotClient>(),
                channelId,
                options,
                TimeZoneInfo.Local,
                settings.AnnouncementRetentionDays));
        }

        return services;
    }
}
