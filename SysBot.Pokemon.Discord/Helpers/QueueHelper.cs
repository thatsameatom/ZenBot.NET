using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Pokemon.Discord.Helpers;

namespace SysBot.Pokemon.Discord;

public static class QueueHelper<T> where T : PKM, new()
{
    private const uint MaxTradeCode = 9999_9999;
    private static readonly PokeTradeHub<T> Hub = SysCord<T>.Runner.Hub;

    public static async Task AddToQueueAsync(SocketCommandContext context, int code, string trainer, RequestSignificance sig, T trade, PokeRoutineType routine, PokeTradeType type, SocketUser trader)
    {
        if ((uint)code > MaxTradeCode)
        {
            await context.Channel.SendMessageAsync("Trade code should be 00000000-99999999!").ConfigureAwait(false);
            return;
        }

        if (Hub.Config.Trade.BannedTradeCodes.Contains((uint)code))
        {
            await context.Channel.SendMessageAsync($"**{code}** is not an allowed trade code.").ConfigureAwait(false);
            return;
        }

        try
        {
            const string helper = "¡Te he añadido a la cola! Te avisaré por aquí cuando tu intercambio este por empezar.";
            IUserMessage test = await trader.SendMessageAsync(helper).ConfigureAwait(false);

            // Try adding
            var result = AddToTradeQueue(context, trade, code, trainer, sig, routine, type, trader, out var msg, out var receiving, out var embed);

            // Notify in channel
            if (Hub.Config.Discord.UseTradeEmbeds is TradeEmbedDisplay.TradeInitialize && result)
            {
                //var embedMsg = msg.Replace(" - ", ".\n").Replace(", u", "\nU");
                _ = embed?.Build();
                embed?.Builder.AddField("** **", msg, inline: false);
                await context.Channel.SendMessageAsync(embed: embed?.Build()).ConfigureAwait(false);
            }
            else
            {
                await context.Channel.SendMessageAsync(msg + receiving).ConfigureAwait(false);
            }

            // Notify in PM to mirror what was said in the channel.
            if (result)
            {
                // msg += $"\nYour trade code will be {(typeof(T) == typeof(PB7) ? "" : $"**{code:0000 0000}**.")}";

                if (typeof(T) == typeof(PB7))
                {

                    msg += "\nTu código de intercambio será:"; 
                    var codes = PictoCodesExtensions.GetPictoCodesFromLinkCode(code);
                    var (attachment, embedPicto) = PictoCodesEmbedBuilder.CreatePictoCodesEmbed(codes);
                    await trader.SendFileAsync(attachment, $"{msg}", false, embedPicto.Build()).ConfigureAwait(false);
                }
                else
                {
                    var embedCode = new EmbedBuilder()
                        .WithTitle("Tu código de intercambio será:")
                        .WithDescription($"# {code:0000 0000}")
                        .WithTimestamp(DateTimeOffset.Now)
                        .WithThumbnailUrl("https://raw.githubusercontent.com/thatsameatom/sprites/refs/heads/main/tradecode.gif")
                        .Build();

                    await trader.SendMessageAsync($"{msg + receiving}", embed: embedCode).ConfigureAwait(false);
                }
            }

            // Clean Up
            if (result)
            {
                // Delete the user's join message for privacy
                if (!context.IsPrivate)
                    await context.Message.DeleteAsync(RequestOptions.Default).ConfigureAwait(false);
            }
            else
            {
                // Delete our "I'm adding you!", and send the same message that we sent to the general channel.
                await test.DeleteAsync().ConfigureAwait(false);
            }
        }
        catch (HttpException ex)
        {
            await HandleDiscordExceptionAsync(context, trader, ex).ConfigureAwait(false);
        }
    }

    public static Task AddToQueueAsync(SocketCommandContext context, int code, string trainer, RequestSignificance sig, T trade, PokeRoutineType routine, PokeTradeType type)
    {
        return AddToQueueAsync(context, code, trainer, sig, trade, routine, type, context.User);
    }

    private static bool AddToTradeQueue(SocketCommandContext context, T pk, int code, string trainerName, RequestSignificance sig, PokeRoutineType type, PokeTradeType t, SocketUser trader, out string msg, out string receiving, out TradeEmbedBuilder<T>? embed)
    {
        var user = trader;
        var userID = user.Id;
        string name;
        if (trader is IGuildUser guildUser)
        {
            name = NicknameHelper.Get(guildUser);
        }
        else
        {
            name = trader.Username;
        }

        var trainer = new PokeTradeTrainerInfo(trainerName, userID);
        var notifier = new DiscordTradeNotifier<T>(pk, trainer, code, user, context);
        var detail = new PokeTradeDetail<T>(pk, trainer, notifier, t, code, sig == RequestSignificance.Favored);
        var trade = new TradeEntry<T>(detail, userID, type, name);

        var hub = SysCord<T>.Runner.Hub;
        var Info = hub.Queues.Info;
        var added = Info.AddToTradeQueue(trade, userID, sig == RequestSignificance.Owner);

        if (added == QueueResultAdd.AlreadyInQueue)
        {
            msg = "Lo siento, ya estas en la cola.";
            receiving = string.Empty;
            embed = null;
            return false;
        }

        var position = Info.CheckPosition(userID, type);

        var ticketID = string.Empty;
        if (TradeStartModule<T>.IsStartChannel(context.Channel.Id))
            ticketID = $", ID Único: {detail.ID}";

        var strings = GameInfo.GetStrings("en");
        receiving = t switch
        {
            PokeTradeType.MysteryEgg => " Recibiendo: Mystery Egg.",
            PokeTradeType.ItemTrade => $" Recibiendo: {strings.itemlist[pk.HeldItem]}.",
            PokeTradeType.Specific or PokeTradeType.Giveaway => $" Recibiendo: {strings.Species[pk.Species]}.",
            _ => string.Empty
        };
        msg = $"{user.Mention} - Añadido a la cola de {type}{ticketID}.";

        embed = new TradeEmbedBuilder<T>(pk, hub, new QueueUser(trainer.ID, name), type, t);

        if (hub.Config.Discord.UseTradeEmbeds is not TradeEmbedDisplay.TradeInitialize)
        {
            msg += $"Posición Actual: {position.Position}.";
            var botct = Info.Hub.Bots.Count;
            if (position.Position > botct)
            {
                var eta = Info.Hub.Config.Queues.EstimateDelay(position.Position, botct);
                msg += $" Estimado: {eta:F1} minutos.";
            }
        }

        return true;
    }

    private static async Task HandleDiscordExceptionAsync(SocketCommandContext context, SocketUser trader, HttpException ex)
    {
        var hub = SysCord<T>.Runner.Hub;
        var app = await context.Client.GetApplicationInfoAsync().ConfigureAwait(false);
        var owner = app.Team != null ? app?.Team?.TeamMembers?.FirstOrDefault(member => member.Role == TeamRole.Owner)?.User.Id : app.Owner.Id;
        string message = string.Empty;
        EmbedBuilder embedBuilder = new();
        switch (ex.DiscordCode)
        {
            case DiscordErrorCode.UnknownMessage:
                {
                    // The message was deleted before we could delete it.
                    message = "The message was deleted before I could delete it!";
                    embedBuilder.Title = "Message Deletion Error";
                }
                break;
            case DiscordErrorCode.InsufficientPermissions or DiscordErrorCode.MissingPermissions:
                {
                    // Check if the exception was raised due to missing "Send Messages" or "Manage Messages" permissions. Nag the bot owner if so.
                    var permissions = context.Guild.CurrentUser.GetPermissions(context.Channel as IGuildChannel);
                    if (!permissions.SendMessages)
                    {
                        // Nag the owner in logs.
                        message = "You must grant me \"Send Messages\" permissions!";
                        Base.LogUtil.LogError(message, "QueueHelper");
                        return;
                    }
                    if (!permissions.ManageMessages)
                    {
                        message = "I must be granted \"Manage Messages\" permissions!";
                        embedBuilder.Title = "Permissions Error";
                    }
                    if (!permissions.EmbedLinks)
                    {
                        message = "I'm missing the \"Embed Links\" permission!";
                    }
                }
                break;
            case DiscordErrorCode.CannotSendMessageToUser:
                {
                    // The user either has DMs turned off, or Discord thinks they do.
                    message = context.User == trader ? $"{context.User.Mention}\n¡Debes habilitar los mensajes directos para que pueda enviarte tu código de intercambio!" : "The mentioned user must enable private messages in order for me to DM them their trade code!";
                    if (context.User == trader)
                        hub.Queues.Info.ClearTrade(context.User.Id);
                    else
                        hub.Queues.Info.ClearTrade(trader.Id);
                    embedBuilder.Title = "Error de Privacidad";
                }
                break;
            default:
                {
                    // Send a generic error message.
                    message = ex.DiscordCode != null ? $"Discord error {(int)ex.DiscordCode}: {ex.Reason}" : $"Http error {(int)ex.HttpCode}: {ex.Message}";
                }
                break;
        }
        embedBuilder.Description = message;
        embedBuilder.Color = Color.Red;
        embedBuilder.ThumbnailUrl = context.Client.CurrentUser.GetAvatarUrl();
        var pingOwner = ex.DiscordCode == (DiscordErrorCode.InsufficientPermissions | DiscordErrorCode.MissingPermissions);
        var embed = embedBuilder.Build();

        try
        {
            // Get the bots permissions in the channel
            var currentUser = context.Guild.GetUser(context.Client.CurrentUser.Id);
            var channelPerms = currentUser.GetPermissions(context.Channel as IGuildChannel);

            // Check embed links and attach files perms
            bool canSendEmbed = channelPerms.Has(ChannelPermission.EmbedLinks);
            bool canAttachFiles = channelPerms.Has(ChannelPermission.AttachFiles);

            if (!canSendEmbed && !canAttachFiles || !canSendEmbed)
            {
                await context.Message.ReplyAsync(pingOwner ? $"<@{owner}> - {message}" : message).ConfigureAwait(false);
            }
            else
            {
                await context.Message.ReplyAsync(pingOwner ? $"<@{owner}>" : "", false, embed: embed).ConfigureAwait(false);
            }
        }
        catch
        {
            await context.Channel.SendMessageAsync(pingOwner ? $"<@{owner}> {message}" : message).ConfigureAwait(false);
        }
    }
}
