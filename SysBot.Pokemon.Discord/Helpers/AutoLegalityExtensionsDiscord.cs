using Discord;
using Discord.WebSocket;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using SysBot.Base;

namespace SysBot.Pokemon.Discord;

public static class AutoLegalityExtensionsDiscord
{
    public static async Task ReplyWithLegalizedSetAsync(this ISocketMessageChannel channel, ITrainerInfo sav, ShowdownSet set)
    {
        if (set.Species == 0)
        {
            await channel.SendMessageAsync("¡Vaya! ¡No he podido interpretar tu mensaje! Si querías convertir algo, ¡comprueba lo que has pegado!").ConfigureAwait(false);
            return;
        }

        try
        {
            var template = AutoLegalityWrapper.GetTemplate(set);
            var pkm = sav.GetLegal(template, out var result);
            var la = new LegalityAnalysis(pkm);
            var spec = GameInfo.Strings.Species[template.Species];
            if (!la.Valid)
            {
                var reason = result switch
                {
                    LegalizationResult.Timeout => $"Ese set de {spec} tomó mucho tiempo en generarse.",
                    LegalizationResult.VersionMismatch => "Request refused: PKHeX and Auto-Legality Mod version mismatch.",
                    _ => $"No fui capaz de crear un {spec} con ese set.",
                };
                var imsg = $"¡Vaya! {reason}";
                if (result == LegalizationResult.Failed)
                    imsg += $"\n{AutoLegalityWrapper.GetLegalizationHint(template, sav, pkm)}";
                await channel.SendMessageAsync(imsg).ConfigureAwait(false);
                return;
            }

            var msg = $"Aqui tienes tu ({result}) legalizado para {spec} ({la.EncounterOriginal.Name})!";
            await channel.SendPKMAsync(pkm, msg + $"\n{ReusableActions.GetFormattedShowdownText(pkm)}").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogUtil.LogSafe(ex, nameof(AutoLegalityExtensionsDiscord));
            var msg = $"¡Vaya! Ha surgido un problema inesperado con este set de Showdown:\n```{string.Join("\n", set.GetSetLines())}```";
            await channel.SendMessageAsync(msg).ConfigureAwait(false);
        }
    }

    public static Task ReplyWithLegalizedSetAsync(this ISocketMessageChannel channel, string content, byte gen)
    {
        content = ReusableActions.StripCodeBlock(content);
        var set = new ShowdownSet(content);
        var sav = AutoLegalityWrapper.GetTrainerInfo(gen);
        return channel.ReplyWithLegalizedSetAsync(sav, set);
    }

    public static Task ReplyWithLegalizedSetAsync<T>(this ISocketMessageChannel channel, string content) where T : PKM, new()
    {
        content = ReusableActions.StripCodeBlock(content);
        var set = new ShowdownSet(content);
        var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
        return channel.ReplyWithLegalizedSetAsync(sav, set);
    }

    public static async Task ReplyWithLegalizedSetAsync(this ISocketMessageChannel channel, IAttachment att)
    {
        var download = await NetUtil.DownloadAttachmentAsync(att).ConfigureAwait(false);
        if (!download.Success)
        {
            await channel.SendMessageAsync(download.ErrorMessage).ConfigureAwait(false);
            return;
        }

        if (download.Data is not PKM pkm)
        {
            await channel.SendMessageAsync($"El archivo adjunto {download.SanitizedFileName} no es un archivo Pokémon valido.").ConfigureAwait(false);
            return;
        }

        if (new LegalityAnalysis(pkm).Valid)
        {
            await channel.SendMessageAsync($"{download.SanitizedFileName}: Ya es legal.").ConfigureAwait(false);
            return;
        }

        var legal = pkm.LegalizePokemon();
        if (!new LegalityAnalysis(legal).Valid)
        {
            await channel.SendMessageAsync($"{download.SanitizedFileName}: No se puede legalizar.").ConfigureAwait(false);
            return;
        }

        legal.RefreshChecksum();

        var msg = $"Aqui tienes tu Pokémon legalizado {download.SanitizedFileName}!\n{ReusableActions.GetFormattedShowdownText(legal)}";
        await channel.SendPKMAsync(legal, msg).ConfigureAwait(false);
    }
}
