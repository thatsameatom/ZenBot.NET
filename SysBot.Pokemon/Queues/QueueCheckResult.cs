using PKHeX.Core;

namespace SysBot.Pokemon;

/// <summary>
/// Stores data for indicating how a queue position/presence check resulted.
/// </summary>
/// <typeparam name="T"></typeparam>
public sealed record QueueCheckResult<T>(
    bool InQueue = false,
    TradeEntry<T>? Detail = null,
    int Position = -1,
    int QueueCount = -1)
    where T : PKM, new()
{
    public static readonly QueueCheckResult<T> None = new();

    public string GetMessage()
    {
        if (!InQueue || Detail is null)
            return "No estas en la cola.";
        var position = $"{Position}/{QueueCount}";
        var msg = $"Estas en la cola de {Detail.Type}! Posición: {position} (ID {Detail.Trade.ID})";
        var pk = Detail.Trade.TradeData;
        var strings = GameInfo.GetStrings("en");
        var receiving = Detail.Trade.Type switch
        {
            PokeTradeType.MysteryEgg => "Huevo Misterioso",
            PokeTradeType.ItemTrade => strings.itemlist[pk.HeldItem],
            _ => strings.Species[pk.Species]
        };
        if (pk.Species != 0)
            msg += $", Recibiendo: {receiving}";
        return msg;
    }
}
