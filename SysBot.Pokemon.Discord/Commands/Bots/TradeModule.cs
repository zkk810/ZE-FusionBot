using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Discord.Helpers;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static SysBot.Pokemon.TradeSettings.TradeSettingsCategory;

namespace SysBot.Pokemon.Discord;

[Summary("Queues new Link Code trades")]
public class TradeModule<T> : ModuleBase<SocketCommandContext> where T : PKM, new()
{
    private static string Prefix => SysCordSettings.Settings.CommandPrefix;

    private static TradeQueueInfo<T> Info => SysCord<T>.Runner.Hub.Queues.Info;

    private static readonly char[] separator = [' '];

    private static readonly char[] separatorArray = [' '];

    private static readonly char[] separatorArray0 = [' '];

    [Command("fixOT")]
    [Alias("fix", "f")]
    [Summary("Fixes OT and Nickname of a Pokémon you show via Link Trade if an advert is detected.")]
    [RequireQueueRole(nameof(DiscordManager.RolesFixOT))]
    public async Task FixAdOT()
    {
        var userID = Context.User.Id;

        // Check if the user is already in the queue
        if (Info.IsUserInQueue(userID))
        {
            _ = ReplyAndDeleteAsync("You already have an existing trade in the queue. Please wait until it is processed.", 6);
            return;
        }

        var code = Info.GetRandomTradeCode(userID, Context.Channel, Context.User);
        var trainerName = Context.User.Username;
        var lgcode = Info.GetRandomLGTradeCode();
        var sig = Context.User.GetFavor();

        await QueueHelper<T>.AddToQueueAsync(Context, code, trainerName, sig, new T(), PokeRoutineType.FixOT, PokeTradeType.FixOT, Context.User, false, 1, 1, false, false, lgcode).ConfigureAwait(false);
        if (Context.Message is IUserMessage userMessage)
        {
            _ = DeleteMessagesAfterDelayAsync(userMessage, null, 6);
        }
    }

    [Command("fixOT")]
    [Alias("fix", "f")]
    [Summary("Fixes OT and Nickname of a Pokémon you show via Link Trade if an advert is detected.")]
    [RequireQueueRole(nameof(DiscordManager.RolesFixOT))]
    public async Task FixAdOT([Summary("Trade Code")] int code)
    {
        var userID = Context.User.Id;

        // Check if the user is already in the queue
        if (Info.IsUserInQueue(userID))
        {
            _ = ReplyAndDeleteAsync("You already have an existing trade in the queue. Please wait until it is processed.", 6);
            return;
        }

        var trainerName = Context.User.Username;
        var sig = Context.User.GetFavor();
        var lgcode = Info.GetRandomLGTradeCode();

        await QueueHelper<T>.AddToQueueAsync(Context, code, trainerName, sig, new T(), PokeRoutineType.FixOT, PokeTradeType.FixOT, Context.User, false, 1, 1, false, false, lgcode).ConfigureAwait(false);
        if (Context.Message is IUserMessage userMessage)
        {
            _ = DeleteMessagesAfterDelayAsync(userMessage, null, 6);
        }
    }

    [Command("fixOTList")]
    [Alias("fl", "fq")]
    [Summary("Prints the users in the FixOT queue.")]
    [RequireSudo]
    public async Task GetFixListAsync()
    {
        string msg = Info.GetTradeList(PokeRoutineType.FixOT);
        var embed = new EmbedBuilder();
        embed.AddField(x =>
        {
            x.Name = "Pending Trades";
            x.Value = msg;
            x.IsInline = false;
        });
        await ReplyAsync("These are the users who are currently waiting:", embed: embed.Build()).ConfigureAwait(false);
    }

    [Command("dittoTrade")]
    [Alias("dt", "ditto")]
    [Summary("Makes the bot trade you a Ditto with a requested stat spread and language.")]
    public async Task DittoTrade([Summary("A combination of \"ATK/SPA/SPE\" or \"6IV\"")] string keyword, [Summary("Language")] string language, [Summary("Nature")] string nature)
    {
        var userID = Context.User.Id;

        // Check if the user is already in the queue
        if (Info.IsUserInQueue(userID))
        {
            _ = ReplyAndDeleteAsync("You already have an existing trade in the queue. Please wait until it is processed.", 6);
            return;
        }

        var code = Info.GetRandomTradeCode(userID, Context.Channel, Context.User);
        await DittoTrade(code, keyword, language, nature).ConfigureAwait(false);
        if (Context.Message is IUserMessage userMessage)
        {
            _ = DeleteMessagesAfterDelayAsync(userMessage, null, 2);
        }
    }

    [Command("dittoTrade")]
    [Alias("dt", "ditto")]
    [Summary("Makes the bot trade you a Ditto with a requested stat spread and language.")]
    public async Task DittoTrade([Summary("Trade Code")] int code, [Summary("A combination of \"ATK/SPA/SPE\" or \"6IV\"")] string keyword, [Summary("Language")] string language, [Summary("Nature")] string nature)
    {
        var userID = Context.User.Id;

        // Check if the user is already in the queue
        if (Info.IsUserInQueue(userID))
        {
            _ = ReplyAndDeleteAsync("You already have an existing trade in the queue. Please wait until it is processed.", 6);
            return;
        }

        keyword = keyword.ToLower().Trim();

        if (Enum.TryParse(language, true, out LanguageID lang))
        {
            language = lang.ToString();
        }
        else
        {
            _ = ReplyAndDeleteAsync($"Couldn't recognize language: {language}.", 6, Context.Message);
            return;
        }

        nature = nature.Trim()[..1].ToUpper() + nature.Trim()[1..].ToLower();
        var set = new ShowdownSet($"{keyword}(Ditto)\nLanguage: {language}\nNature: {nature}");
        var template = AutoLegalityWrapper.GetTemplate(set);
        var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
        var pkm = sav.GetLegal(template, out var result);
        AbstractTrade<T>.DittoTrade((T)pkm);
        var la = new LegalityAnalysis(pkm);

        if (pkm is not T pk || !la.Valid)
        {
            var reason = result == "Timeout" ? "That set took too long to generate." : "I wasn't able to create something from that.";
            var imsg = $"Aw, shit son! {reason} Here's my best attempt for that Ditto!";
            await Context.Channel.SendPKMAsync(pkm, imsg).ConfigureAwait(false);
            return;
        }

        pk.ResetPartyStats();

        // Ad Name Check
        if (Info.Hub.Config.Trade.TradeConfiguration.EnableSpamCheck)
        {
            if (AbstractTrade<T>.HasAdName(pk, out string ad))
            {
                await ReplyAndDeleteAsync("Detected Adname in the Pokémon's name or trainer name, which is not allowed.", 5);
                return;
            }
        }

        var sig = Context.User.GetFavor();
        await QueueHelper<T>.AddToQueueAsync(Context, code, Context.User.Username, sig, pk, PokeRoutineType.LinkTrade, PokeTradeType.Specific).ConfigureAwait(false);
        if (Context.Message is IUserMessage userMessage)
        {
            _ = DeleteMessagesAfterDelayAsync(userMessage, null, 2);
        }
    }

    [Command("itemTrade")]
    [Alias("it", "item")]
    [Summary("Makes the bot trade you a Pokémon holding the requested item, or Ditto if stat spread keyword is provided.")]
    public async Task ItemTrade([Remainder] string item)
    {
        // Check if the user is already in the queue
        var userID = Context.User.Id;
        if (Info.IsUserInQueue(userID))
        {
            await ReplyAsync("You already have an existing trade in the queue. Please wait until it is processed.").ConfigureAwait(false);
            return;
        }
        var code = Info.GetRandomTradeCode(userID, Context.Channel, Context.User);
        await ItemTrade(code, item).ConfigureAwait(false);
    }

    [Command("itemTrade")]
    [Alias("it", "item")]
    [Summary("Makes the bot trade you a Pokémon holding the requested item.")]
    public async Task ItemTrade([Summary("Trade Code")] int code, [Remainder] string item)
    {
        var userID = Context.User.Id;

        // Check if the user is already in the queue
        if (Info.IsUserInQueue(userID))
        {
            _ = ReplyAndDeleteAsync("You already have an existing trade in the queue. Please wait until it is processed.", 6);
            return;
        }

        Species species = Info.Hub.Config.Trade.TradeConfiguration.ItemTradeSpecies == Species.None ? Species.Diglett : Info.Hub.Config.Trade.TradeConfiguration.ItemTradeSpecies;
        var set = new ShowdownSet($"{SpeciesName.GetSpeciesNameGeneration((ushort)species, 2, 8)} @ {item.Trim()}");
        var template = AutoLegalityWrapper.GetTemplate(set);
        var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
        var pkm = sav.GetLegal(template, out var result);
        pkm = EntityConverter.ConvertToType(pkm, typeof(T), out _) ?? pkm;

        if (pkm.HeldItem == 0)
        {
            _ = ReplyAndDeleteAsync($"{Context.User.Username}, the item you entered wasn't recognized.", 6, Context.Message);
            return;
        }

        var la = new LegalityAnalysis(pkm);
        if (pkm is not T pk || !la.Valid)
        {
            var reason = result == "Timeout" ? "That set took too long to generate." : "I wasn't able to create something from that.";
            var imsg = $"Oops! {reason} Here's my best attempt for that {species}!";
            await Context.Channel.SendPKMAsync(pkm, imsg).ConfigureAwait(false);
            return;
        }

        pk.ResetPartyStats();
        var sig = Context.User.GetFavor();
        await QueueHelper<T>.AddToQueueAsync(Context, code, Context.User.Username, sig, pk, PokeRoutineType.LinkTrade, PokeTradeType.Specific).ConfigureAwait(false);

        if (Context.Message is IUserMessage userMessage)
        {
            _ = DeleteMessagesAfterDelayAsync(userMessage, null, 6);
        }
    }

    [Command("tradeList")]
    [Alias("tl")]
    [Summary("Prints the users in the trade queues.")]
    [RequireSudo]
    public async Task GetTradeListAsync()
    {
        string msg = Info.GetTradeList(PokeRoutineType.LinkTrade);
        var embed = new EmbedBuilder();
        embed.AddField(x =>
        {
            x.Name = "Pending Trades";
            x.Value = msg;
            x.IsInline = false;
        });
        await ReplyAsync("These are the users who are currently waiting:", embed: embed.Build()).ConfigureAwait(false);
    }

    [Command("egg")]
    [Alias("Egg")]
    [Summary("Trades an egg via the provided Pokémon set.")]
    public async Task TradeEgg([Remainder] string egg)
    {
        var userID = Context.User.Id;
        var code = Info.GetRandomTradeCode(userID, Context.Channel, Context.User);
        await TradeEggAsync(code, egg).ConfigureAwait(false);
    }
    [Command("egg")]
    [Alias("Egg")]
    [Summary("Trades an egg generated from the provided Pokémon name.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public async Task TradeEggAsync([Summary("Trade Code")] int code, [Summary("Showdown Set")][Remainder] string content)
    {
        var userID = Context.User.Id;
        if (Info.IsUserInQueue(userID))
        {
            _ = ReplyAndDeleteAsync("You already have an existing trade in the queue. Please wait until it is processed.", 6);
            return;
        }

        if (content.Contains("Met Date", StringComparison.OrdinalIgnoreCase) &&
    content.Contains("Invalid", StringComparison.OrdinalIgnoreCase))
        {
            await ReplyAsync("Your Met Date isn’t valid. Please use formats like YYYYMMDD or MM/DD/YYYY.");
            return;
        }

        content = ReusableActions.StripCodeBlock(content);
        content = BatchNormalizer.NormalizeBatchCommands(content);
        var set = new ShowdownSet(content);
        var template = AutoLegalityWrapper.GetTemplate(set);

        _ = Task.Run(async () =>
        {
            try
            {
                var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
                var pkm = sav.GetLegal(template, out var result);
                if (pkm == null)
                {
                    await ReplyAsync("Set took too long to legalize.");
                    return;
                }
                pkm = EntityConverter.ConvertToType(pkm, typeof(T), out _) ?? pkm;
                if (pkm is not T pk)
                {
                    _ = ReplyAndDeleteAsync("I wasn't able to create an egg for that.", 5, Context.Message);
                    return;
                }
                bool versionSpecified = content.Contains("~=Version=", StringComparison.OrdinalIgnoreCase);
                if (!versionSpecified)
                {
                    if (pk is PB8 pb8)
                    {
                        pb8.Version = (GameVersion)GameVersion.BD;
                    }
                    else if (pk is PK8 pk8)
                    {
                        pk8.Version = (GameVersion)GameVersion.SW;
                    }
                }
                pk.IsNicknamed = false;
                AbstractTrade<T>.EggTrade(pk, template);
                var sig = Context.User.GetFavor();
                await AddTradeToQueueAsync(code, Context.User.Username, pk, sig, Context.User).ConfigureAwait(false);

                _ = DeleteMessagesAfterDelayAsync(null, Context.Message, 5);
            }
            catch (Exception ex)
            {
                LogUtil.LogSafe(ex, nameof(TradeModule<T>));
                _ = ReplyAndDeleteAsync("An error occurred while processing the request.", 6, Context.Message);
            }
        });
        if (Context.Message is IUserMessage userMessage)
            _ = DeleteMessagesAfterDelayAsync(userMessage, null, 5);
    }


    [Command("hidetrade")]
    [Alias("ht")]
    [Summary("Makes the bot trade you a Pokémon converted from the provided Showdown Set without showing the trade embed details.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public Task HideTradeAsync([Summary("Showdown Set")][Remainder] string content)
    {
        var userID = Context.User.Id;
        var code = Info.GetRandomTradeCode(userID, Context.Channel, Context.User);
        return HideTradeAsync(code, content);
    }

    [Command("hidetrade")]
    [Alias("ht")]
    [Summary("Makes the bot trade you a Pokémon converted from the provided Showdown Set without showing the trade embed details.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public async Task HideTradeAsync([Summary("Trade Code")] int code, [Summary("Showdown Set")][Remainder] string content)
    {
        List<Pictocodes>? lgcode = null;
        var userID = Context.User.Id;

        if (Info.IsUserInQueue(userID))
        {
            var existingTrades = Info.GetIsUserQueued(x => x.UserID == userID);
            foreach (var trade in existingTrades)
            {
                trade.Trade.IsProcessing = false;
            }

            var clearResult = Info.ClearTrade(userID);
            if (clearResult == QueueResultRemove.CurrentlyProcessing || clearResult == QueueResultRemove.NotInQueue)
            {
                _ = ReplyAndDeleteAsync("You already have an existing trade in the queue that cannot be cleared. Please wait until it is processed.", 6);
                return;
            }
        }

        var ignoreAutoOT = content.Contains("OT:") || content.Contains("TID:") || content.Contains("SID:");
        content = ReusableActions.StripCodeBlock(content);
        content = BatchNormalizer.NormalizeBatchCommands(content);
        bool isEgg = AbstractTrade<T>.IsEggCheck(content);

        _ = ShowdownParsing.TryParseAnyLanguage(content, out ShowdownSet? set);

        if (set == null || set.Species == 0)
        {
            await ReplyAsync("Unable to parse Showdown set. Could not identify the Pokémon species.");
            return;
        }

        byte finalLanguage = LanguageHelper.GetFinalLanguage(content, set, (byte)Info.Hub.Config.Legality.GenerateLanguage, AbstractTrade<T>.DetectShowdownLanguage);
        var template = AutoLegalityWrapper.GetTemplate(set);

        if (set.InvalidLines.Count != 0)
        {
            var msg = $"Unable to parse Showdown Set:\n{string.Join("\n", set.InvalidLines)}";
            _ = ReplyAndDeleteAsync(msg, 5, Context.Message);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var sav = LanguageHelper.GetTrainerInfoWithLanguage<T>((LanguageID)finalLanguage);
                var pkm = sav.GetLegal(template, out var result);

                if (pkm == null)
                {
                    var response = await ReplyAsync("Set took too long to legalize.");
                    return;
                }

                var la = new LegalityAnalysis(pkm);
                var spec = GameInfo.Strings.Species[template.Species];

                if (isEgg && pkm is T eggPk)
                {
                    eggPk.IsNicknamed = false;
                    AbstractTrade<T>.EggTrade(eggPk, template);
                    pkm = eggPk;
                    la = new LegalityAnalysis(pkm);
                }
                else
                {
                    pkm.HeldItem = pkm switch
                    {
                        PA8 => (int)HeldItem.None,
                        _ when pkm.HeldItem == 0 && !pkm.IsEgg => (int)SysCord<T>.Runner.Config.Trade.TradeConfiguration.DefaultHeldItem,
                        _ => pkm.HeldItem
                    };

                    if (pkm is PB7)
                    {
                        lgcode = GenerateRandomPictocodes(3);
                        if (pkm.Species == (int)Species.Mew && pkm.IsShiny)
                        {
                            await ReplyAsync("Mew can **not** be Shiny in LGPE. PoGo Mew does not transfer and Pokeball Plus Mew is shiny locked.");
                            return;
                        }
                    }
                }

                if (pkm is not T pk || !la.Valid)
                {
                    var reason = result == "Timeout" ? $"That {spec} set took too long to generate." :
                                 result == "VersionMismatch" ? "Request refused: PKHeX and Auto-Legality Mod version mismatch." :
                                 $"I wasn't able to create a {spec} from that set.";

                    var embedBuilder = new EmbedBuilder()
                        .WithTitle("Trade Creation Failed.")
                        .WithColor(Color.Red)
                        .AddField("Status", $"Failed to create {spec}.")
                        .AddField("Reason", reason);

                    if (result == "Failed")
                    {
                        var legalizationHint = AutoLegalityWrapper.GetLegalizationHint(template, sav, pkm);
                        if (legalizationHint.Contains("Requested shiny value (ShinyType."))
                        {
                            legalizationHint = $"{spec} **cannot** be shiny. Please try again.";
                        }

                        if (!string.IsNullOrEmpty(legalizationHint))
                        {
                            embedBuilder.AddField("Hint", legalizationHint);
                        }
                    }

                    string userMention = Context.User.Mention;
                    string messageContent = $"{userMention}, here's the report for your request:";
                    var message = await Context.Channel.SendMessageAsync(text: messageContent, embed: embedBuilder.Build()).ConfigureAwait(false);
                    _ = DeleteMessagesAfterDelayAsync(message, Context.Message, 30);
                    return;
                }

                AbstractTrade<T>.CheckAndSetUnrivaledDate(pk);

                if (pk.IsEgg)
                {
                    // Egg handling consistent with .egg command
                    switch (pk)
                    {
                        case PK9 pk9: // Scarlet/Violet
                            pk9.MetLocation = 0;
                            pk9.MetDate = default; // must be 0
                            pk9.EggMetDate = DateOnly.FromDateTime(DateTime.Now);
                            break;

                        case PB8 pb8: // BDSP
                            pb8.MetLocation = 65535;
                            pb8.EggMetDate = DateOnly.FromDateTime(DateTime.Now);
                            break;

                        case PK8 pk8: // Sword/Shield
                            pk8.MetLocation = 30002;
                            pk8.EggMetDate = DateOnly.FromDateTime(DateTime.Now);
                            pk8.DynamaxLevel = 0; // important: eggs can't have dynamax level
                            break;

                        default:
                            pk.EggMetDate = DateOnly.FromDateTime(DateTime.Now);
                            break;
                    }
                }

                pk.Language = finalLanguage;

                if (!set.Nickname.Equals(pk.Nickname) && string.IsNullOrEmpty(set.Nickname))
                {
                    pk.ClearNickname();
                }

                pk.ResetPartyStats();

                if (Info.Hub.Config.Trade.TradeConfiguration.EnableSpamCheck)
                {
                    if (AbstractTrade<T>.HasAdName(pk, out string ad))
                    {
                        await ReplyAndDeleteAsync("Detected Adname in the Pokémon's name or trainer name, which is not allowed.", 5);
                        return;
                    }
                }

                var sig = Context.User.GetFavor();
                await AddTradeToQueueAsync(
                code: code,
                Context.User.Username,
                pk,
                sig: sig,
                Context.User,
                isBatchTrade: false,
                batchTradeNumber: 1,
                totalBatchTrades: 1,
                isMysteryEgg: isEgg,
                isHiddenTrade: true,
                lgcode: lgcode,
                ignoreAutoOT: ignoreAutoOT,
                setEdited: false
                ).ConfigureAwait(false);

            }
            catch (Exception ex)
            {
                LogUtil.LogSafe(ex, nameof(TradeModule<T>));
                var msg = $"An unexpected problem happened with this Showdown Set:\n```{string.Join("\n", set.GetSetLines())}```";
                _ = ReplyAndDeleteAsync(msg, 5, Context.Message);
            }
            if (Context.Message is IUserMessage userMessage)
            {
                _ = DeleteMessagesAfterDelayAsync(userMessage, null, 5);
            }
        });

        await Task.CompletedTask;
    }

    [Command("hidetrade")]
    [Alias("ht")]
    [Summary("Makes the bot trade you the provided Pokémon file without showing the trade embed details.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public Task HideTradeAsyncAttach(
            [Summary("Trade Code")] int code,
            [Summary("Ignore AutoOT")] bool ignoreAutoOT = false)
    {
        var sig = Context.User.GetFavor();
        return HideTradeAsyncAttach(code, sig, Context.User, ignoreAutoOT: ignoreAutoOT);
    }

    [Command("hidetrade")]
    [Alias("ht")]
    [Summary("Makes the bot trade you the attached file without showing the trade embed details.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    private async Task HideTradeAsyncAttach([Summary("Ignore AutoOT")] bool ignoreAutoOT = false)
    {
        var userID = Context.User.Id;
        var code = Info.GetRandomTradeCode(userID, Context.Channel, Context.User);
        var sig = Context.User.GetFavor();
        await HideTradeAsyncAttach(code, sig, Context.User, ignoreAutoOT: ignoreAutoOT).ConfigureAwait(false);
        if (Context.Message is IUserMessage userMessage)
        {
            _ = DeleteMessagesAfterDelayAsync(userMessage, null, 5);
        }
    }

    [Command("trade")]
    [Alias("t")]
    [Summary("Makes the bot trade you a Pokémon converted from the provided Showdown Set.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public Task TradeAsync([Summary("Showdown Set")][Remainder] string content)
    {
        var userID = Context.User.Id;
        var code = Info.GetRandomTradeCode(userID, Context.Channel, Context.User);
        return TradeAsync(code, content);
    }

    [Command("trade")]
    [Alias("t")]
    [Summary("Makes the bot trade you a Pokémon converted from the provided Showdown Set.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public async Task TradeAsync([Summary("Trade Code")] int code, [Summary("Showdown Set")][Remainder] string content)
    {
        List<Pictocodes>? lgcode = null;

        var userID = Context.User.Id;
        if (Info.IsUserInQueue(userID))
        {
            var existingTrades = Info.GetIsUserQueued(x => x.UserID == userID);
            foreach (var trade in existingTrades)
            {
                trade.Trade.IsProcessing = false;
            }

            var clearResult = Info.ClearTrade(userID);
            if (clearResult == QueueResultRemove.CurrentlyProcessing || clearResult == QueueResultRemove.NotInQueue)
            {
                _ = ReplyAndDeleteAsync("You already have an existing trade in the queue that cannot be cleared. Please wait until it is processed.", 5);
                return;
            }
        }

        var ignoreAutoOT = content.Contains("OT:") || content.Contains("TID:") || content.Contains("SID:");
        content = ReusableActions.StripCodeBlock(content);
        content = BatchNormalizer.NormalizeBatchCommands(content);
        bool isEgg = AbstractTrade<T>.IsEggCheck(content);

        _ = ShowdownParsing.TryParseAnyLanguage(content, out ShowdownSet? set);

        if (set == null || set.Species == 0)
        {
            await ReplyAsync("Unable to parse Showdown set. Could not identify the Pokémon species.");
            return;
        }

        byte finalLanguage = LanguageHelper.GetFinalLanguage(content, set, (byte)Info.Hub.Config.Legality.GenerateLanguage, AbstractTrade<T>.DetectShowdownLanguage);
        var template = AutoLegalityWrapper.GetTemplate(set);

        if (set.InvalidLines.Count != 0)
        {
            var msg = $"Unable to parse Showdown Set:\n{string.Join("\n", set.InvalidLines)}";
            _ = ReplyAndDeleteAsync(msg, 6, Context.Message);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var sav = LanguageHelper.GetTrainerInfoWithLanguage<T>((LanguageID)finalLanguage);
                var pkm = sav.GetLegal(template, out var result);
                if (pkm == null)
                {
                    var response = await ReplyAsync("Showdown Set took too long to legalize.");
                    return;
                }

                var la = new LegalityAnalysis(pkm);
                var spec = GameInfo.Strings.Species[template.Species];

                if (isEgg && pkm is T eggPk)
                {
                    bool versionSpecified = content.Contains(".Version=", StringComparison.OrdinalIgnoreCase);
                    if (!versionSpecified)
                    {
                        if (eggPk is PB8 pb8)
                        {
                            pb8.Version = (GameVersion)GameVersion.BD;
                        }
                        else if (eggPk is PK8 pk8)
                        {
                            pk8.Version = (GameVersion)GameVersion.SW;
                        }
                    }
                    eggPk.IsNicknamed = false;
                    AbstractTrade<T>.EggTrade(eggPk, template);
                    pkm = eggPk;
                    la = new LegalityAnalysis(pkm);
                }
                else
                {
                    pkm.HeldItem = pkm switch
                    {
                        PA8 => (int)HeldItem.None,
                        _ when pkm.HeldItem == 0 && !pkm.IsEgg => (int)SysCord<T>.Runner.Config.Trade.TradeConfiguration.DefaultHeldItem,
                        _ => pkm.HeldItem
                    };

                    if (pkm is PB7)
                    {
                        lgcode = GenerateRandomPictocodes(3);
                        if (pkm.Species == (int)Species.Mew && pkm.IsShiny)
                        {
                            await ReplyAsync("Mew can **not** be Shiny in LGPE. PoGo Mew does not transfer and Pokeball Plus Mew is shiny locked.");
                            return;
                        }
                    }
                }

                if (pkm is not T pk || !la.Valid)
                {
                    var reason = result == "Timeout" ? $"That {spec} set took too long to generate." :
                                 result == "VersionMismatch" ? "Request refused: PKHeX and Auto-Legality Mod version mismatch." :
                                 $"I wasn't able to create a {spec} from that set.";

                    var embedBuilder = new EmbedBuilder()
                        .WithTitle("Trade Creation Failed.")
                        .WithColor(Color.Red)
                        .AddField("Status", $"Failed to create {spec}.")
                        .AddField("Reason", reason);

                    if (result == "Failed")
                    {
                        var legalizationHint = AutoLegalityWrapper.GetLegalizationHint(template, sav, pkm);
                        if (legalizationHint.Contains("Requested shiny value (ShinyType."))
                        {
                            legalizationHint = $"{spec} **cannot** be shiny. Please try again.";
                        }

                        if (!string.IsNullOrEmpty(legalizationHint))
                        {
                            embedBuilder.AddField("Hint", legalizationHint);
                        }
                    }

                    string userMention = Context.User.Mention;
                    string messageContent = $"{userMention}, here's the report for your request:";
                    var message = await Context.Channel.SendMessageAsync(text: messageContent, embed: embedBuilder.Build()).ConfigureAwait(false);
                    _ = DeleteMessagesAfterDelayAsync(message, Context.Message, 60);
                    return;
                }

                AbstractTrade<T>.CheckAndSetUnrivaledDate(pk);

                if (pk.IsEgg)
                {
                    // Egg handling consistent with .egg command
                    switch (pk)
                    {
                        case PK9 pk9: // Scarlet/Violet
                            pk9.MetLocation = 0;
                            pk9.MetDate = default; // must be 0
                            pk9.EggMetDate = DateOnly.FromDateTime(DateTime.Now);
                            break;

                        case PB8 pb8: // BDSP
                            pb8.MetLocation = 65535;
                            pb8.EggMetDate = DateOnly.FromDateTime(DateTime.Now);
                            break;

                        case PK8 pk8: // Sword/Shield
                            pk8.MetLocation = 30002;
                            pk8.EggMetDate = DateOnly.FromDateTime(DateTime.Now);
                            pk8.DynamaxLevel = 0; // important: eggs can't have dynamax level
                            break;

                        default:
                            pk.EggMetDate = DateOnly.FromDateTime(DateTime.Now);
                            break;
                    }
                }

                pk.Language = finalLanguage;

                if (!set.Nickname.Equals(pk.Nickname) && string.IsNullOrEmpty(set.Nickname))
                {
                    pk.ClearNickname();
                }

                pk.ResetPartyStats();

                if (Info.Hub.Config.Trade.TradeConfiguration.EnableSpamCheck)
                {
                    if (AbstractTrade<T>.HasAdName(pk, out string ad))
                    {
                        await ReplyAndDeleteAsync("Detected Adname in the Pokémon's name or trainer name, which is not allowed.", 5);
                        return;
                    }
                }

                var sig = Context.User.GetFavor();
                await AddTradeToQueueAsync(code, Context.User.Username, pk, sig, Context.User, isBatchTrade: false, batchTradeNumber: 1, totalBatchTrades: 1, lgcode: lgcode, ignoreAutoOT: ignoreAutoOT, setEdited: false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogUtil.LogSafe(ex, nameof(TradeModule<T>));
                var msg = $"An unexpected problem happened with this Showdown Set:\n```{string.Join("\n", set.GetSetLines())}```";
                _ = ReplyAndDeleteAsync(msg, 6, null);
            }
            if (Context.Message is IUserMessage userMessage)
            {
                _ = DeleteMessagesAfterDelayAsync(userMessage, null, 6);
            }
        });

        await Task.CompletedTask;
    }

    [Command("trade")]
    [Alias("t")]
    [Summary("Makes the bot trade you the provided Pokémon file.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public Task TradeAsyncAttach(
    [Summary("Trade Code")] int code,
    [Summary("Ignore AutoOT")] bool ignoreAutoOT = false)
    {
        var sig = Context.User.GetFavor();
        return TradeAsyncAttachInternal(code, sig, Context.User, ignoreAutoOT: ignoreAutoOT);
    }

    [Command("trade")]
    [Alias("t")]
    [Summary("Makes the bot trade you the attached file.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public async Task TradeAsyncAttach([Summary("Ignore AutoOT")] bool ignoreAutoOT = false)
    {
        var userID = Context.User.Id;
        var code = Info.GetRandomTradeCode(userID, Context.Channel, Context.User);
        var sig = Context.User.GetFavor();
        await Task.Run(async () =>
        {
            await TradeAsyncAttachInternal(code, sig, Context.User, ignoreAutoOT).ConfigureAwait(false);
        }).ConfigureAwait(false);
        if (Context.Message is IUserMessage userMessage)
        {
            _ = DeleteMessagesAfterDelayAsync(userMessage, null, 6);
        }
    }

    // Dictionaries for the TextTrade command's pending trades and queue status
    private static readonly ConcurrentDictionary<ulong, List<string>> _pendingTextTrades = new();
    private static readonly ConcurrentDictionary<ulong, bool> _usersInQueue = new();
    private static readonly ConcurrentDictionary<ulong, bool> _batchQueueMessageSent = new();

    [Command("textTrade")]
    [Alias("tt", "text")]
    [Summary("Upload a .txt or .csv file of Showdown sets, then select which Pokémon to trade.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public async Task TextTradeAsync([Remainder] string args = "")
    {
        await ProcessTextTradeBatchAsync(Context.User.Id, (SocketUser)Context.User, args);
    }

    private async Task ProcessTextTradeBatchAsync(ulong userId, SocketUser user, string args)
    {
        if (_usersInQueue.ContainsKey(userId))
        {
            await ReplyAsync("You already have an existing trade in the queue. Please wait until it is finished processing.");
            return;
        }

        if (Context.Message is IUserMessage existingMessage)
        {
            _ = DeleteMessagesAfterDelayAsync(existingMessage, null, 6);
        }

        // ===== JOB 1: File Upload =====
        if (Context.Message.Attachments.Count > 0 && string.IsNullOrWhiteSpace(args))
        {
            var file = Context.Message.Attachments.First();
            if (!file.Filename.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) &&
                !file.Filename.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                await ReplyAsync("Only `.txt` or `.csv` files are supported for TextTrade.");
                return;
            }

            // Supports sets being separated by "---" or by double new lines
            var data = await new HttpClient().GetStringAsync(file.Url);
            var rawBlocks = Regex.Split(data, @"(?:---|\r?\n\s*\r?\n)+")
                 .Select(b => b.Trim())
                 .Where(b => !string.IsNullOrWhiteSpace(b))
                 .ToList();

            // Get valid species names (string form)
            var validSpecies = Enum.GetNames(typeof(Species))
                .Where(n => n != "None")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var blocks = new List<string>();

            foreach (var block in rawBlocks)
            {
                var firstLine = block.Split('\n')[0].Trim();

                // Extract text before "@"
                string candidate = firstLine.Contains("@")
                    ? firstLine.Split('@')[0].Trim()
                    : firstLine;

                // Remove gender markers like (M) or (F)
                candidate = Regex.Replace(candidate, @"\s*\(M\)|\s*\(F\)", "", RegexOptions.IgnoreCase).Trim();

                // Special handling: Egg formats
                if (candidate.Contains("Egg", StringComparison.OrdinalIgnoreCase))
                {
                    blocks.Add(block);
                    continue;
                }

                // Only accept if it's an actual Pokémon species
                if (validSpecies.Contains(candidate))
                    blocks.Add(block);
            }

            if (blocks.Count == 0)
            {
                await ReplyAsync("No valid Pokémon sets found in the uploaded file.");
                return;
            }

            if (Context.Message is IUserMessage detectionMessage)
            {
                _ = DeleteMessagesAfterDelayAsync(detectionMessage, null, 6);
            }

            _pendingTextTrades[userId] = blocks;

            // Initial embed for the user to select from
            var embed = new EmbedBuilder()
                .WithTitle("📄 Text Trade Detected!")
                .WithDescription($"Detected **{blocks.Count}** Pokémon sets from **{file.Filename}**")
                .WithColor(Color.Blue);

            for (int i = 0; i < blocks.Count; i++)
            {
                var firstLine = blocks[i].Split('\n')[0];
                var species = firstLine.Split('@')[0].Trim();

                string icons = "";

                // ✨ Shiny check
                if (blocks[i].IndexOf("Shiny: Yes", StringComparison.OrdinalIgnoreCase) >= 0)
                    icons += "✨ ";

                // 🚩 Level check
                var levelMatch = Regex.Match(blocks[i], @"Level:\s*(\d+)", RegexOptions.IgnoreCase);
                if (levelMatch.Success && int.TryParse(levelMatch.Groups[1].Value, out int lvl))
                {
                    if (lvl < 5 || lvl > 100)
                        icons += "🚩 ";
                }

                // ⚪ Item check (missing @ on first line)
                if (!firstLine.Contains("@"))
                    icons += "⚪ ";

                // 🧾 OT/TID/SID check
                if (blocks[i].Contains("OT:", StringComparison.OrdinalIgnoreCase) ||
                    blocks[i].Contains("TID:", StringComparison.OrdinalIgnoreCase) ||
                    blocks[i].Contains("SID:", StringComparison.OrdinalIgnoreCase))
                    icons += "🧾 ";

                // 🥚 Egg check
                if (firstLine.Contains("Egg", StringComparison.OrdinalIgnoreCase))
                    icons += "🥚 ";

                embed.AddField(
                    $"{i + 1}. {species} {icons}",
                    $"Use `{Prefix}tt {i + 1}` to trade this Pokémon\nUse `{Prefix}tv {i + 1}` to view this Pokémon set",
                    false
                );
            }

            // Footer legend
            embed.AddField(
                "Multiple Pokémon",
                $"Use `{Prefix}tt 1 2 3 etc.`, to trade **no more than 6 Pokémon**",
                false
            );
            embed.WithFooter("✨ = Shiny | 🚩 = Fishy | ⚪ = No Held Item | 🧾 = Has OT/TID/SID | 🥚 = Egg\n⏳ Make a selection within 60s or the TextTrade is canceled automatically.");
            var detectionEmbedMessage = await ReplyAsync(embed: embed.Build());
            _ = DeleteMessagesAfterDelayAsync(null, detectionEmbedMessage, 60);

            _ = Task.Run(async () =>
            {
                await Task.Delay(60000);
                if (_pendingTextTrades.TryRemove(userId, out _))
                    await ReplyAsync($"⌛ {user.Mention}, your TextTrade request expired after 60 seconds.");
            });
            return;
        }

        // ===== JOB 2: Selection =====
        if (!_pendingTextTrades.TryGetValue(userId, out var sets))
        {
            await ReplyAsync("You haven’t uploaded a file yet or it expired. Attach a `.txt` or `.csv` first.");
            return;
        }

        var selections = args.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                             .Select(t => int.TryParse(t, out int idx) ? idx : 0)
                             .Where(idx => idx > 0 && idx <= sets.Count)
                             .ToList();

        if (selections.Count == 0)
        {
            await ReplyAsync($"Invalid selection. Use `{Prefix}tt 1` or `{Prefix}tt 1 2` (max 6 Pokémon).");
            return;
        }

        if (selections.Count > 6)
        {
            await ReplyAsync("You can only trade up to 6 Pokémon at a time.");
            return;
        }

        // Mark the user as in queue
        _usersInQueue[userId] = true;

        // Generate a unique batch trade code and piggyback off the batch trade logic
        int batchTradeCode = Info.GetRandomTradeCode(userId, Context.Channel, user);

        // Send the batch DM exactly once without spamming DMs
        if (!_batchQueueMessageSent.ContainsKey(userId))
        {
            _batchQueueMessageSent[userId] = true;
            await EmbedHelper.SendTradeCodeEmbedAsync(user, batchTradeCode);
        }

        // Queue all selected Pokémon and treat like a batch
        _ = Task.Run(async () =>
        {
            try
            {
                int tradeNumber = 1;
                foreach (var idx in selections)
                {
                    // Use ReusableActions to clean and normalize each block
                    // Use BatchNormalizer to ensure consistent command formatting with custom class
                    string showdownBlock = sets[idx - 1];
                    showdownBlock = ReusableActions.StripCodeBlock(showdownBlock);
                    showdownBlock = BatchNormalizer.NormalizeBatchCommands(showdownBlock);

                    await ProcessSingleTextTradeAsync(showdownBlock, batchTradeCode, tradeNumber, selections.Count, user);

                    tradeNumber++;
                    await Task.Delay(1000); // delay to avoid spamming
                }
            }
            finally
            {
                // Cleanup for queue, pending trades, and batch message flag
                _pendingTextTrades.TryRemove(userId, out _);
                _usersInQueue.TryRemove(userId, out _);
                _batchQueueMessageSent.TryRemove(userId, out _);
            }
        });
    }

    // Process a single Pokémon trade based on the TextTrade batch method
    private async Task ProcessSingleTextTradeAsync(string tradeContent, int batchTradeCode, int tradeNumber, int totalTrades, SocketUser user)
    {
        // Pre-checks content and ignores AutoOT if OT/TID/SID present
        tradeContent = ReusableActions.StripCodeBlock(tradeContent);
        bool ignoreAutoOT = tradeContent.Contains("OT:") || tradeContent.Contains("TID:") || tradeContent.Contains("SID:");

        // Showdown parsing logic to get the set
        if (!ShowdownParsing.TryParseAnyLanguage(tradeContent, out ShowdownSet? set) || set == null || set.Species == 0)
        {
            await ReplyAsync($"{user.Mention}, could not parse the Pokémon set. Skipping trade.");
            return;
        }

        // Determine final language and legal template
        byte finalLanguage = LanguageHelper.GetFinalLanguage(tradeContent, set, (byte)Info.Hub.Config.Legality.GenerateLanguage, AbstractTrade<T>.DetectShowdownLanguage);
        var template = AutoLegalityWrapper.GetTemplate(set);

        if (set.InvalidLines.Count != 0)
        {
            await ReplyAsync($"{user.Mention}, invalid lines found:\n{string.Join("\n", set.InvalidLines)}");
            return;
        }

        // Generate PKM via ALM
        PKM? pkm = null;
        string result = "Unknown";

        await Task.Run(() =>
        {
            try
            {
                // Use language-specific sav to get the legal PKM
                var sav = LanguageHelper.GetTrainerInfoWithLanguage<T>((LanguageID)finalLanguage);
                pkm = sav.GetLegal(template, out result);
            }
            catch (Exception ex)
            {
                LogUtil.LogSafe(ex, nameof(ProcessSingleTextTradeAsync));
            }
        });

        if (pkm == null)
        {
            await EmbedHelper.SendTradeCanceledEmbedAsync(user, $"Failed to generate Pokémon: {result}");
            return;
        }

        // Egg & Held Item fixes
        if (pkm.IsEgg) AbstractTrade<T>.CheckAndSetUnrivaledDate(pkm);
        if (pkm.HeldItem == 0 && !pkm.IsEgg)
            pkm.HeldItem = (int)SysCord<T>.Runner.Config.Trade.TradeConfiguration.DefaultHeldItem;

        // Spam and/or Admon check
        if (Info.Hub.Config.Trade.TradeConfiguration.EnableSpamCheck && AbstractTrade<T>.HasAdName((T)pkm, out string ad))
        {
            await ReplyAndDeleteAsync($"{user.Mention}, detected invalid Adname in Pokémon or Trainer name.", 5);
            return;
        }

        // If LG, utilize LG code method
        var lgCode = Info.GetRandomLGTradeCode();

        // Add to queue
        await AddTradeToQueueAsync(
            batchTradeCode,
            Context.User.Username,
            (T)pkm,
            Context.User.GetFavor(),
            Context.User,
            isBatchTrade: true,
            batchTradeNumber: tradeNumber,
            totalBatchTrades: totalTrades,
            lgcode: lgCode,
            tradeType: PokeTradeType.Batch,
            ignoreAutoOT: ignoreAutoOT,
            setEdited: false
        ).ConfigureAwait(false);
    }

    [Command("textView")]
    [Alias("tv")]
    [Summary("View a specific Pokémon set from your pending TextTrade file by number.")]
    public async Task TextViewAsync([Remainder] string args = "")
    {
        ulong userId = Context.User.Id;

        if (!_pendingTextTrades.TryGetValue(userId, out var sets))
        {
            await ReplyAsync($"{Context.User.Mention}, you don’t have an active TextTrade file loaded. Upload one first with `{Prefix}tt`.");
            return;
        }

        if (string.IsNullOrWhiteSpace(args) || !int.TryParse(args, out int idx) || idx <= 0 || idx > sets.Count)
        {
            await ReplyAsync($"Invalid set number. Use `{Prefix}tv 1` through `{Prefix}tv {sets.Count}`.");
            return;
        }

        var showdownBlock = sets[idx - 1];
        showdownBlock = ReusableActions.StripCodeBlock(showdownBlock);

        // Build an embed with the full showdown set
        var embed = new EmbedBuilder()
            .WithTitle($"👀 Viewing Set #{idx}")
            .WithDescription($"```\n{showdownBlock}\n```")
            .WithFooter($"Use {Prefix}tt {idx} to trade this Pokémon.")
            .WithColor(Color.DarkPurple);

        var sentEmbed = await ReplyAsync(embed: embed.Build());
        _ = DeleteMessagesAfterDelayAsync(null, sentEmbed, 60);
    }

    [Command("batchTrade")]
    [Alias("bt")]
    [Summary("Makes the bot trade multiple Pokémon from the provided list, up to a maximum of 6 trades.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public async Task BatchTradeAsync([Summary("List of Showdown Sets separated by '---'")][Remainder] string content)
    {
        // Offload the entire trade logic onto a background task
        _ = Task.Run(async () =>
        {
            try
            {
                var userID = Context.User.Id;

                // Check if user is already in queue and clear them
                if (Info.IsUserInQueue(userID))
                {
                    var existingTrades = Info.GetIsUserQueued(x => x.UserID == userID);
                    foreach (var trade in existingTrades)
                        trade.Trade.IsProcessing = false;

                    var clearResult = Info.ClearTrade(userID);
                    if (clearResult is QueueResultRemove.CurrentlyProcessing or QueueResultRemove.NotInQueue)
                    {
                        _ = ReplyAndDeleteAsync("You already have an existing trade in the queue that cannot be cleared. Please wait until it is processed.", 6);
                        return;
                    }
                }

                // Normalize and split sets
                content = ReusableActions.StripCodeBlock(content);
                content = BatchNormalizer.NormalizeBatchCommands(content);
                var trades = ParseBatchTradeContent(content);

                if (trades.Count < 2)
                {
                    await ReplyAndDeleteAsync($"Batch trades require at least two Pokémon. Use `{Prefix}t` to trade single Pokémon.", 5, Context.Message);
                    return;
                }

                int maxTradesAllowed = Info.Hub.Config.Trade.TradeConfiguration.MaxPkmsPerTrade;
                if (maxTradesAllowed < 1)
                {
                    await ReplyAndDeleteAsync("Batch trading is disabled on this bot. Contact an admin.", 5, Context.Message);
                    return;
                }

                if (trades.Count > maxTradesAllowed)
                {
                    await ReplyAndDeleteAsync($"You can only process up to {maxTradesAllowed} Pokémon per batch trade.", 5, Context.Message);
                    return;
                }

                var batchTradeCode = Info.GetRandomTradeCode(userID, Context.Channel, Context.User);
                var batchPokemonList = new List<T>();
                var errors = new List<BatchTradeError>();

                for (int i = 0; i < trades.Count; i++)
                {
                    var tradeText = trades[i];

                    try
                    {
                        var (pk, error, set, hint) = await ProcessSingleTradeForBatch(tradeText);

                        if (pk != null)
                        {
                            batchPokemonList.Add(pk);
                        }
                        else
                        {
                            var speciesName = set?.Species > 0
                                ? GameInfo.Strings.Species[set.Species]
                                : "Unknown";

                            errors.Add(new BatchTradeError
                            {
                                TradeNumber = i + 1,
                                SpeciesName = speciesName,
                                ErrorMessage = error ?? "Unknown error occurred.",
                                LegalizationHint = hint,
                                ShowdownSet = set != null ? string.Join("\n", set.GetSetLines()) : tradeText
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError($"[BatchTrade Fatal Crash] Trade #{i + 1}\nInput:\n{tradeText}\nException:\n{ex}", "Batch Command");

                        errors.Add(new BatchTradeError
                        {
                            TradeNumber = i + 1,
                            SpeciesName = "Crash",
                            ErrorMessage = "A fatal error occurred while parsing this set.",
                            LegalizationHint = ex.Message,
                            ShowdownSet = tradeText
                        });
                    }
                }

                if (batchPokemonList.Count == 0)
                {
                    var failEmbed = BuildErrorEmbed(errors, trades.Count);
                    var failMsg = await ReplyAsync(embed: failEmbed);
                    _ = DeleteMessagesAfterDelayAsync(failMsg, Context.Message, 20);
                    return;
                }

                if (errors.Count > 0)
                {
                    var warnEmbed = BuildErrorEmbed(errors, trades.Count);
                    var warnMsg = await ReplyAsync(embed: warnEmbed);
                    _ = DeleteMessagesAfterDelayAsync(warnMsg, Context.Message, 20);
                }

                await ProcessBatchContainer(batchPokemonList, batchTradeCode, trades.Count);

                if (Context.Message is IUserMessage userMessage)
                    _ = DeleteMessagesAfterDelayAsync(userMessage, null, 6);
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[BatchTrade Fatal Crash] Exception in BatchTradeAsync: {ex}", "BatchTradeInitiated");
                await ReplyAsync("⚠️ Something went *really* wrong while processing your batch trade. Contact an admin.");
            }
        });
    }


    private static Task<(T? Pokemon, string? Error, ShowdownSet? Set, string? LegalizationHint)> ProcessSingleTradeForBatch(string tradeContent)
    {
        tradeContent = ReusableActions.StripCodeBlock(tradeContent);
        var ignoreAutoOT = tradeContent.Contains("OT:") || tradeContent.Contains("TID:") || tradeContent.Contains("SID:");
        bool isEgg = AbstractTrade<T>.IsEggCheck(tradeContent);
        _ = ShowdownParsing.TryParseAnyLanguage(tradeContent, out ShowdownSet? set);
        if (set == null || set.Species == 0)
            return Task.FromResult<(T?, string?, ShowdownSet?, string?)>((null, "Unable to parse Showdown set. Could not identify the Pokémon species.", set, null));
        byte finalLanguage = LanguageHelper.GetFinalLanguage(tradeContent, set, (byte)Info.Hub.Config.Legality.GenerateLanguage, AbstractTrade<T>.DetectShowdownLanguage);
        var template = AutoLegalityWrapper.GetTemplate(set);
        var sav = LanguageHelper.GetTrainerInfoWithLanguage<T>((LanguageID)finalLanguage);
        var pkm = sav.GetLegal(template, out var result);
        if (pkm == null)
        {
            var spec = GameInfo.Strings.Species[template.Species];
            var reason = result == "Timeout" ? $"That {spec} set took too long to generate." :
            result == "VersionMismatch" ? "Request refused: PKHeX and Auto-Legality Mod version mismatch." :
            $"I wasn't able to create a {spec} from that set.";
            return Task.FromResult<(T?, string?, ShowdownSet?, string?)>((null, reason, set, null));
        }
        var la = new LegalityAnalysis(pkm);

        // Handle eggs similar to regular trade commands
        if (isEgg && pkm is T eggPk)
        {
            bool versionSpecified = tradeContent.Contains(".Version=", StringComparison.OrdinalIgnoreCase);
            if (!versionSpecified)
            {
                if (eggPk is PB8 pb8)
                {
                    pb8.Version = (GameVersion)GameVersion.BD;
                }
                else if (eggPk is PK8 pk8)
                {
                    pk8.Version = (GameVersion)GameVersion.SW;
                }
            }
            eggPk.IsNicknamed = false;
            AbstractTrade<T>.EggTrade(eggPk, template);
            pkm = eggPk;
            la = new LegalityAnalysis(pkm);
        }

        if (pkm is not T pk || !la.Valid)
        {
            var spec = GameInfo.Strings.Species[template.Species];
            var reason = result == "Timeout" ? $"That {spec} set took too long to generate." :
            result == "VersionMismatch" ? "Request refused: PKHeX and Auto-Legality Mod version mismatch." :
            $"I wasn't able to create a {spec} from that set.";
            string? legalizationHint = null;

            if (result == "Failed")
            {
                legalizationHint = AutoLegalityWrapper.GetLegalizationHint(template, sav, pkm);
                if (legalizationHint.Contains("Requested shiny value (ShinyType."))
                {
                    legalizationHint = $"{spec} cannot be shiny. Please try again.";
                }
            }

            return Task.FromResult<(T?, string?, ShowdownSet?, string?)>((null, reason, set, legalizationHint));
        }

        // Apply standard processing
        if (pk is PA8)
            pk.HeldItem = (int)HeldItem.None;
        else if (pk.HeldItem == 0 && !pk.IsEgg)
            pk.HeldItem = (int)SysCord<T>.Runner.Config.Trade.TradeConfiguration.DefaultHeldItem;

        if (pk.IsEgg)
        {
            // Egg handling consistent with .egg command
            switch (pk)
            {
                case PK9 pk9: // Scarlet/Violet
                    pk9.MetLocation = 0;
                    pk9.MetDate = default; // must be 0
                    pk9.EggMetDate = DateOnly.FromDateTime(DateTime.Now);
                    break;

                case PB8 pb8: // BDSP
                    pb8.MetLocation = 65535;
                    pb8.EggMetDate = DateOnly.FromDateTime(DateTime.Now);
                    break;

                case PK8 pk8: // Sword/Shield
                    pk8.MetLocation = 30002;
                    pk8.EggMetDate = DateOnly.FromDateTime(DateTime.Now);
                    pk8.DynamaxLevel = 0; // important: eggs can't have dynamax level
                    break;

                default:
                    pk.EggMetDate = DateOnly.FromDateTime(DateTime.Now);
                    break;
            }
        }

        pk.Language = finalLanguage;
        if (!set.Nickname.Equals(pk.Nickname) && string.IsNullOrEmpty(set.Nickname))
            pk.ClearNickname();
        pk.ResetPartyStats();

        // Check for spam/ad names
        if (Info.Hub.Config.Trade.TradeConfiguration.EnableSpamCheck)
        {
            if (AbstractTrade<T>.HasAdName(pk, out string ad))
            {
                return Task.FromResult<(T?, string?, ShowdownSet?, string?)>((null, "Detected Adname in the Pokémon's name or trainer name, which is not allowed.", set, null));
            }
        }
        return Task.FromResult<(T?, string?, ShowdownSet?, string?)>((pk, null, set, null));
    }

    private static Embed BuildErrorEmbed(List<BatchTradeError> errors, int totalTrades)
    {
        var embed = new EmbedBuilder()
            .WithTitle("❌ Batch Trade Validation")
            .WithColor(Color.Red)
            .WithDescription($"⚠️ {errors.Count} out of {totalTrades} trades failed to process.")
            .WithFooter("Fix the issues and try again.");

        foreach (var err in errors)
        {
            var lines = !string.IsNullOrEmpty(err.ShowdownSet)
                ? string.Join(" | ", err.ShowdownSet.Split('\n').Take(2))
                : "No data";

            var value = $"**Error:** {err.ErrorMessage}";
            if (!string.IsNullOrEmpty(err.LegalizationHint))
                value += $"\n💡 **Hint:** {err.LegalizationHint}";

            value += $"\n**Set Preview:** {lines}";

            if (value.Length > 1024)
                value = value[..1021] + "...";

            embed.AddField($"Trade #{err.TradeNumber} - {err.SpeciesName}", value);
        }

        return embed.Build();
    }

    private class BatchTradeError
    {
        public int TradeNumber { get; set; }
        public string SpeciesName { get; set; } = "Unknown";
        public string ErrorMessage { get; set; } = "Unknown error";
        public string? LegalizationHint { get; set; }
        public string ShowdownSet { get; set; } = "";
    }
    private async Task ProcessBatchContainer(List<T> batchPokemonList, int batchTradeCode, int totalTrades)
    {
        var userID = Context.User.Id;
        var code = batchTradeCode;
        var sig = Context.User.GetFavor();
        var firstPokemon = batchPokemonList[0];
        // Create a single detail with all batch trades
        await QueueHelper<T>.AddBatchContainerToQueueAsync(Context, code, Context.User.Username, firstPokemon, batchPokemonList, sig, Context.User, totalTrades).ConfigureAwait(false);
    }

    private static List<string> ParseBatchTradeContent(string content)
    {
        var delimiters = new[] { "---", "—-" }; // Both three hyphens and em dash + hyphen
        return content.Split(delimiters, StringSplitOptions.RemoveEmptyEntries)
                      .Select(trade => trade.Trim())
                      .ToList();
    }

    [Command("batchTradeZip")]
    [Alias("btz")]
    [Summary("Makes the bot trade multiple Pokémon from the provided .zip file, up to a maximum of 6 trades.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public async Task BatchTradeZipAsync()
    {
        // First, check if batch trades are allowed
        if (!SysCord<T>.Runner.Config.Trade.TradeConfiguration.AllowBatchTrades)
        {
            _ = ReplyAndDeleteAsync("Batch trades are currently disabled.", 6);
            return;
        }

        // Check if the user is already in the queue
        var userID = Context.User.Id;
        if (Info.IsUserInQueue(userID))
        {
            _ = ReplyAndDeleteAsync("You already have an existing trade in the queue. Please wait until it is processed.", 2);
            return;
        }

        var attachment = Context.Message.Attachments.FirstOrDefault();
        if (attachment == default)
        {
            _ = ReplyAndDeleteAsync("No attachment provided!", 6);
            return;
        }

        if (!attachment.Filename.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            _ = ReplyAndDeleteAsync("Invalid file format. Please provide a .zip file.", 6);
            return;
        }

        var zipBytes = await new HttpClient().GetByteArrayAsync(attachment.Url);
        await using var zipStream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var entries = archive.Entries.ToList();

        const int maxTradesAllowed = 6; // for full team in the zip created

        // Check if batch mode is allowed and if the number of trades exceeds the limit
        if (maxTradesAllowed < 1 || entries.Count > maxTradesAllowed)
        {
            _ = ReplyAndDeleteAsync($"You can only process up to {maxTradesAllowed} trades at a time. Please reduce the number of Pokémon in your .zip file.", 6, Context.Message);
            return;
        }

        var batchTradeCode = Info.GetRandomTradeCode(userID, Context.Channel, Context.User);
        int batchTradeNumber = 1;

        foreach (var entry in entries)
        {
            await using var entryStream = entry.Open();
            var pkBytes = await TradeModule<T>.ReadAllBytesAsync(entryStream).ConfigureAwait(false);
            var pk = EntityFormat.GetFromBytes(pkBytes);

            if (pk is T)
            {
                await ProcessSingleTradeAsync((T)pk, batchTradeCode, true, batchTradeNumber, entries.Count);
                batchTradeNumber++;
            }
        }
        if (Context.Message is IUserMessage userMessage)
        {
            _ = DeleteMessagesAfterDelayAsync(userMessage, null, 6);
        }
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream)
    {
        await using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream).ConfigureAwait(false);
        return memoryStream.ToArray();
    }

    private async Task ProcessSingleTradeAsync(T pk, int batchTradeCode, bool isBatchTrade, int batchTradeNumber, int totalBatchTrades)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var la = new LegalityAnalysis(pk);
                var spec = GameInfo.Strings.Species[pk.Species];

                if (!la.Valid)
                {
                    await ReplyAsync($"The {spec} in the provided file is not legal.").ConfigureAwait(false);
                    return;
                }
                // Set correct MetDate for Mightiest Mark
                AbstractTrade<T>.CheckAndSetUnrivaledDate(pk);
                pk.ResetPartyStats();

                var userID = Context.User.Id;
                var code = Info.GetRandomTradeCode(userID, Context.Channel, Context.User);
                var lgcode = Info.GetRandomLGTradeCode();

                // Ad Name Check
                if (Info.Hub.Config.Trade.TradeConfiguration.EnableSpamCheck)
                {
                    if (AbstractTrade<T>.HasAdName(pk, out string ad))
                    {
                        await ReplyAndDeleteAsync("Detected Adname in the Pokémon's name or trainer name, which is not allowed.", 5);
                        return;
                    }
                }

                // Add the trade to the queue
                var sig = Context.User.GetFavor();
                await AddTradeToQueueAsync(batchTradeCode, Context.User.Username, pk, sig, Context.User, isBatchTrade, batchTradeNumber, totalBatchTrades, lgcode: lgcode, tradeType: PokeTradeType.Batch).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogUtil.LogSafe(ex, nameof(TradeModule<T>));
            }
        });

        // Return immediately to avoid blocking
        await Task.CompletedTask;
    }

    private async Task ProcessSingleTradeAsync(string tradeContent, int batchTradeCode, bool isBatchTrade, int batchTradeNumber, int totalBatchTrades)
    {
        tradeContent = ReusableActions.StripCodeBlock(tradeContent);
        var ignoreAutoOT = tradeContent.Contains("OT:") || tradeContent.Contains("TID:") || tradeContent.Contains("SID:");

        _ = ShowdownParsing.TryParseAnyLanguage(tradeContent, out ShowdownSet? set);

        if (set == null || set.Species == 0)
        {
            await ReplyAsync("Unable to parse Showdown set. Could not identify the Pokémon species.");
            return;
        }

        byte finalLanguage = LanguageHelper.GetFinalLanguage(tradeContent, set, (byte)Info.Hub.Config.Legality.GenerateLanguage, AbstractTrade<T>.DetectShowdownLanguage);
        var template = AutoLegalityWrapper.GetTemplate(set);

        if (set.InvalidLines.Count != 0)
        {
            var msg = $"Unable to parse Showdown Set:\n{string.Join("\n", set.InvalidLines)}";
            await ReplyAsync(msg).ConfigureAwait(false);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var sav = LanguageHelper.GetTrainerInfoWithLanguage<T>((LanguageID)finalLanguage);
                var pkm = sav.GetLegal(template, out var result);
                if (pkm == null)
                {
                    var response = await ReplyAsync("Showdown Set took too long to legalize.");
                    return;
                }

                var la = new LegalityAnalysis(pkm);
                var spec = GameInfo.Strings.Species[template.Species];

                if (pkm is not T pk || !la.Valid)
                {
                    var reason = result == "Timeout" ? $"That {spec} set took too long to generate." :
                                 result == "VersionMismatch" ? "Request refused: PKHeX and Auto-Legality Mod version mismatch." :
                                 $"I wasn't able to create a {spec} from that set.";

                    var embedBuilder = new EmbedBuilder()
                        .WithTitle("Trade Creation Failed.")
                        .WithColor(Color.Red)
                        .AddField("Status", $"Failed to create {spec}.")
                        .AddField("Reason", reason);

                    if (result == "Failed")
                    {
                        var legalizationHint = AutoLegalityWrapper.GetLegalizationHint(template, sav, pkm);
                        if (legalizationHint.Contains("Requested shiny value (ShinyType."))
                        {
                            legalizationHint = $"{spec} **cannot** be shiny. Please try again.";
                        }

                        if (!string.IsNullOrEmpty(legalizationHint))
                        {
                            embedBuilder.AddField("Hint", legalizationHint);
                        }
                    }

                    string userMention = Context.User.Mention;
                    string messageContent = $"{userMention}, here's the report for your request:";
                    var message = await Context.Channel.SendMessageAsync(text: messageContent, embed: embedBuilder.Build()).ConfigureAwait(false);
                    _ = DeleteMessagesAfterDelayAsync(message, Context.Message, 60);
                    return;
                }

                if (pkm is PA8)
                {
                    pkm.HeldItem = (int)HeldItem.None;
                }
                else if (pkm.HeldItem == 0 && !pkm.IsEgg)
                {
                    pkm.HeldItem = (int)SysCord<T>.Runner.Config.Trade.TradeConfiguration.DefaultHeldItem;
                }

                if (pkm is PB7)
                {
                    if (pkm.Species == (int)Species.Mew)
                    {
                        if (pkm.IsShiny)
                        {
                            await ReplyAsync("Mew can **not** be Shiny in LGPE. PoGo Mew does not transfer and Pokeball Plus Mew is shiny locked.");
                            return;
                        }
                    }
                }

                AbstractTrade<T>.CheckAndSetUnrivaledDate(pk);

                if (pk.IsEgg)
                {
                    // Egg handling consistent with .egg command
                    switch (pk)
                    {
                        case PK9 pk9: // Scarlet/Violet
                            pk9.MetLocation = 0;
                            pk9.MetDate = default; // must be 0
                            pk9.EggMetDate = DateOnly.FromDateTime(DateTime.Now);
                            break;

                        case PB8 pb8: // BDSP
                            pb8.MetLocation = 65535;
                            pb8.EggMetDate = DateOnly.FromDateTime(DateTime.Now);
                            break;

                        case PK8 pk8: // Sword/Shield
                            pk8.MetLocation = 30002;
                            pk8.EggMetDate = DateOnly.FromDateTime(DateTime.Now);
                            pk8.DynamaxLevel = 0; // important: eggs can't have dynamax level
                            break;

                        default:
                            pk.EggMetDate = DateOnly.FromDateTime(DateTime.Now);
                            break;
                    }
                }

                pk.Language = finalLanguage;

                if (!set.Nickname.Equals(pk.Nickname) && string.IsNullOrEmpty(set.Nickname))
                {
                    pk.ClearNickname();
                }

                pk.ResetPartyStats();

                var userID = Context.User.Id;
                var code = Info.GetRandomTradeCode(userID, Context.Channel, Context.User);
                var lgcode = Info.GetRandomLGTradeCode();
                if (pkm is PB7)
                {
                    lgcode = GenerateRandomPictocodes(3);
                }

                if (Info.Hub.Config.Trade.TradeConfiguration.EnableSpamCheck)
                {
                    if (AbstractTrade<T>.HasAdName(pk, out string ad))
                    {
                        await ReplyAndDeleteAsync("Detected Adname in the Pokémon's name or trainer name, which is not allowed.", 6);
                        return;
                    }
                }

                var sig = Context.User.GetFavor();
                await AddTradeToQueueAsync(batchTradeCode, Context.User.Username, pk, sig, Context.User, isBatchTrade, batchTradeNumber, totalBatchTrades, lgcode: lgcode, tradeType: PokeTradeType.Batch, ignoreAutoOT: ignoreAutoOT, setEdited: false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogUtil.LogSafe(ex, nameof(TradeModule<T>));
            }
        });

        await Task.CompletedTask;
    }

    [Command("listEvents")]
    [Alias("le")]
    [Summary("Lists available event files, filtered by a specific letter or substring, and sends the list via DM.")]
    public async Task ListEventsAsync([Remainder] string args = "")
    {
        const int itemsPerPage = 20; // Number of items per page
        var eventsFolderPath = SysCord<T>.Runner.Config.Trade.RequestFolderSettings.EventsFolder;
        var botPrefix = SysCord<T>.Runner.Config.Discord.CommandPrefix;

        // Check if the events folder path is not set or empty
        if (string.IsNullOrEmpty(eventsFolderPath))
        {
            _ = ReplyAndDeleteAsync("This bot does not have this feature set up.", 6, Context.Message);
            return;
        }

        // Parsing the arguments to separate filter and page number
        string filter = "";
        int page = 1;
        var parts = args.Split(separator, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length > 0)
        {
            // Check if the last part is a number (page number)
            if (int.TryParse(parts.Last(), out int parsedPage))
            {
                page = parsedPage;
                filter = string.Join(" ", parts.Take(parts.Length - 1));
            }
            else
            {
                filter = string.Join(" ", parts);
            }
        }

        var allEventFiles = Directory.GetFiles(eventsFolderPath)
                                     .Select(Path.GetFileNameWithoutExtension)
                                     .OrderBy(file => file)
                                     .ToList();

#pragma warning disable CS8602 // Dereference of a possibly null reference.
        var filteredEventFiles = allEventFiles
                                 .Where(file => string.IsNullOrWhiteSpace(filter) || file.Contains(filter, StringComparison.OrdinalIgnoreCase))
                                 .ToList();
#pragma warning restore CS8602 // Dereference of a possibly null reference.

        IUserMessage replyMessage;

        // Check if there are no files matching the filter
        if (!filteredEventFiles.Any())
        {
            replyMessage = await ReplyAsync($"No events found matching the filter '{filter}'.");
            _ = DeleteMessagesAfterDelayAsync(replyMessage, Context.Message, 6);
        }
        else
        {
            var pageCount = (int)Math.Ceiling(filteredEventFiles.Count / (double)itemsPerPage);
            page = Math.Clamp(page, 1, pageCount); // Ensure page number is within valid range

            var pageItems = filteredEventFiles.Skip((page - 1) * itemsPerPage).Take(itemsPerPage);

            var embed = new EmbedBuilder()
                .WithTitle($"Available Events - Filter: '{filter}'")
                .WithDescription($"Page {page} of {pageCount}")
                .WithColor(Color.Blue);

            foreach (var item in pageItems)
            {
                var index = allEventFiles.IndexOf(item) + 1; // Get the index from the original list
                embed.AddField($"{index}. {item}", $"Use `{botPrefix}er {index}` to request this event.");
            }

            if (Context.User is IUser user)
            {
                try
                {
                    var dmChannel = await user.CreateDMChannelAsync();
                    await dmChannel.SendMessageAsync(embed: embed.Build());
                    replyMessage = await ReplyAsync($"{Context.User.Mention}, I've sent you a DM with the list of events.");
                }
                catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
                {
                    // This exception is thrown when the bot cannot send DMs to the user
                    replyMessage = await ReplyAsync($"{Context.User.Mention}, I'm unable to send you a DM. Please check your **Server Privacy Settings**.");
                }
            }
            else
            {
                replyMessage = await ReplyAsync("**Error**: Unable to send a DM. Please check your **Server Privacy Settings**.");
            }

            _ = DeleteMessagesAfterDelayAsync(replyMessage, Context.Message, 10);
        }
    }

    [Command("eventRequest")]
    [Alias("er")]
    [Summary("Downloads event attachments from the specified bot owner's EventsFolder and adds to trade queue.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public async Task EventRequestAsync(int index)
    {
        var userID = Context.User.Id;

        // Check if the user is already in the queue
        if (Info.IsUserInQueue(userID))
        {
            _ = ReplyAndDeleteAsync("You already have an existing trade in the queue. Please wait until it is processed.", 6, Context.Message);
            return;
        }

        try
        {
            var eventsFolderPath = SysCord<T>.Runner.Config.Trade.RequestFolderSettings.EventsFolder;
            var eventFiles = Directory.GetFiles(eventsFolderPath)
                .Select(Path.GetFileName)
                .OrderBy(x => x)
                .ToList();

            // Check if the events folder path is not set or empty
            if (string.IsNullOrEmpty(eventsFolderPath))
            {
                _ = ReplyAndDeleteAsync("This bot does not have this feature set up.", 5, Context.Message);
                return;
            }

            if (index < 1 || index > eventFiles.Count)
            {
                _ = ReplyAndDeleteAsync($"Invalid event index. Please use a valid event number from the `{Prefix}le` command.", 6, Context.Message);
                return;
            }

            var selectedFile = eventFiles[index - 1]; // Adjust for zero-based indexing
#pragma warning disable CS8604 // Possible null reference argument.
            var fileData = await File.ReadAllBytesAsync(Path.Combine(eventsFolderPath, selectedFile));
#pragma warning restore CS8604 // Possible null reference argument.
            var download = new Download<PKM>
            {
                Data = EntityFormat.GetFromBytes(fileData),
                Success = true
            };

            var pk = GetRequest(download);
            if (pk == null)
            {
                _ = ReplyAndDeleteAsync("Failed to convert event file to the required PKM type.", 6, Context.Message);
                return;
            }

            var code = Info.GetRandomTradeCode(userID, Context.Channel, Context.User);
            var lgcode = Info.GetRandomLGTradeCode();
            var sig = Context.User.GetFavor();

            await ReplyAsync("Event request added to queue.").ConfigureAwait(false);
            await AddTradeToQueueAsync(code, Context.User.Username, pk, sig, Context.User, lgcode: lgcode).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _ = ReplyAndDeleteAsync($"An error occurred: {ex.Message}", 6, Context.Message);
        }
        finally
        {
            if (Context.Message is IUserMessage userMessage)
            {
                _ = DeleteMessagesAfterDelayAsync(userMessage, null, 6);
            }
        }
    }

    [Command("battleReadyList")]
    [Alias("brl")]
    [Summary("Lists available battle-ready files, filtered by a specific letter or substring, and sends the list via DM.")]
    public async Task BattleReadyListAsync([Remainder] string args = "")
    {
        const int itemsPerPage = 20; // Number of items per page
        var battleReadyFolderPath = SysCord<T>.Runner.Config.Trade.RequestFolderSettings.BattleReadyPKMFolder;

        // Check if the battleready folder path is not set or empty
        if (string.IsNullOrEmpty(battleReadyFolderPath))
        {
            _ = ReplyAndDeleteAsync("This bot does not have this feature set up.", 6, Context.Message);
            return;
        }

        // Parsing the arguments to separate filter and page number
        string filter = "";
        int page = 1;
        var parts = args.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length > 0)
        {
            // Check if the last part is a number (page number)
            if (int.TryParse(parts.Last(), out int parsedPage))
            {
                page = parsedPage;
                filter = string.Join(" ", parts.Take(parts.Length - 1));
            }
            else
            {
                filter = string.Join(" ", parts);
            }
        }

        var allBattleReadyFiles = Directory.GetFiles(battleReadyFolderPath)
                                           .Select(Path.GetFileNameWithoutExtension)
                                           .OrderBy(file => file)
                                           .ToList();

#pragma warning disable CS8602 // Dereference of a possibly null reference.
        var filteredBattleReadyFiles = allBattleReadyFiles
                                       .Where(file => string.IsNullOrWhiteSpace(filter) || file.Contains(filter, StringComparison.OrdinalIgnoreCase))
                                       .ToList();
#pragma warning restore CS8602 // Dereference of a possibly null reference.

        IUserMessage replyMessage;

        // Check if there are no files matching the filter
        if (!filteredBattleReadyFiles.Any())
        {
            replyMessage = await ReplyAsync($"No battle-ready files found matching the filter '{filter}'.");
            _ = DeleteMessagesAfterDelayAsync(replyMessage, Context.Message, 10);
        }
        else
        {
            var pageCount = (int)Math.Ceiling(filteredBattleReadyFiles.Count / (double)itemsPerPage);
            page = Math.Clamp(page, 1, pageCount); // Ensure page number is within valid range

            var pageItems = filteredBattleReadyFiles.Skip((page - 1) * itemsPerPage).Take(itemsPerPage);

            var embed = new EmbedBuilder()
                .WithTitle($"Available Battle-Ready Files - Filter: '{filter}'")
                .WithDescription($"Page {page} of {pageCount}")
                .WithColor(Color.Blue);

            foreach (var item in pageItems)
            {
                var index = allBattleReadyFiles.IndexOf(item) + 1; // Get the index from the original list
                embed.AddField($"{index}. {item}", $"Use `{Prefix}brr {index}` to request this battle-ready file.");
            }

            if (Context.User is IUser user)
            {
                try
                {
                    var dmChannel = await user.CreateDMChannelAsync();
                    await dmChannel.SendMessageAsync(embed: embed.Build());
                    replyMessage = await ReplyAsync($"{Context.User.Mention}, I've sent you a DM with the list of battle-ready files.");
                }
                catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
                {
                    // This exception is thrown when the bot cannot send DMs to the user
                    replyMessage = await ReplyAsync($"{Context.User.Mention}, I'm unable to send you a DM. Please check your **Server Privacy Settings**.");
                }
            }
            else
            {
                replyMessage = await ReplyAsync("**Error**: Unable to send a DM. Please check your **Server Privacy Settings**.");
            }

            _ = DeleteMessagesAfterDelayAsync(replyMessage, Context.Message, 10);
        }
    }

    [Command("battleReadyRequest")]
    [Alias("brr", "br")]
    [Summary("Downloads battle-ready attachments from the specified folder and adds to trade queue.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    public async Task BattleReadyRequestAsync(int index)
    {
        var userID = Context.User.Id;

        // Check if the user is already in the queue
        if (Info.IsUserInQueue(userID))
        {
            _ = ReplyAndDeleteAsync("You already have an existing trade in the queue. Please wait until it is processed.", 6, Context.Message);
            return;
        }

        try
        {
            var battleReadyFolderPath = SysCord<T>.Runner.Config.Trade.RequestFolderSettings.BattleReadyPKMFolder;
            var battleReadyFiles = Directory.GetFiles(battleReadyFolderPath)
                .Select(Path.GetFileName)
                .OrderBy(x => x)
                .ToList();

            // Check if the battleready folder path is not set or empty
            if (string.IsNullOrEmpty(battleReadyFolderPath))
            {
                _ = ReplyAndDeleteAsync("This bot does not have this feature set up.", 6, Context.Message);
                return;
            }

            if (index < 1 || index > battleReadyFiles.Count)
            {
                _ = ReplyAndDeleteAsync($"Invalid battle-ready file index. Please use a valid file number from the `{Prefix}blr` command.", 6, Context.Message);
                return;
            }

            var selectedFile = battleReadyFiles[index - 1];
#pragma warning disable CS8604 // Possible null reference argument.
            var fileData = await File.ReadAllBytesAsync(Path.Combine(battleReadyFolderPath, selectedFile));
#pragma warning restore CS8604 // Possible null reference argument.
            var download = new Download<PKM>
            {
                Data = EntityFormat.GetFromBytes(fileData),
                Success = true
            };

            var pk = GetRequest(download);
            if (pk == null)
            {
                _ = ReplyAndDeleteAsync("Failed to convert battle-ready file to the required PKM type.", 6, Context.Message);
                return;
            }

            var code = Info.GetRandomTradeCode(userID, Context.Channel, Context.User);
            var lgcode = Info.GetRandomLGTradeCode();
            var sig = Context.User.GetFavor();

            await ReplyAsync("Battle-ready request added to queue.").ConfigureAwait(false);
            await AddTradeToQueueAsync(code, Context.User.Username, pk, sig, Context.User, lgcode: lgcode).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _ = ReplyAndDeleteAsync($"An error occurred: {ex.Message}", 5, Context.Message);
        }
        finally
        {
            if (Context.Message is IUserMessage userMessage)
            {
                _ = DeleteMessagesAfterDelayAsync(userMessage, null, 5);
            }
        }
    }

    [Command("tradeUser")]
    [Alias("tu", "tradeOther")]
    [Summary("Makes the bot trade the mentioned user the attached file.")]
    [RequireSudo]
    public async Task TradeAsyncAttachUser([Summary("Trade Code")] int code, [Remainder] string _)
    {
        if (Context.Message.MentionedUsers.Count > 1)
        {
            await ReplyAsync("Too many mentions. Queue one user at a time.").ConfigureAwait(false);
            return;
        }

        if (Context.Message.MentionedUsers.Count == 0)
        {
            await ReplyAsync("A user must be mentioned in order to do this.").ConfigureAwait(false);
            return;
        }

        var usr = Context.Message.MentionedUsers.ElementAt(0);
        var sig = usr.GetFavor();
        await Task.Run(async () =>
        {
            await TradeAsyncAttachInternal(code, sig, usr).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    [Command("tradeUser")]
    [Alias("tu", "tradeOther")]
    [Summary("Makes the bot trade the mentioned user the attached file.")]
    [RequireSudo]
    public Task TradeAsyncAttachUser([Remainder] string _)
    {
        var userID = Context.User.Id;
        var code = Info.GetRandomTradeCode(userID, Context.Channel, Context.User);
        return TradeAsyncAttachUser(code, _);
    }

    private async Task TradeAsyncAttachInternal(int code, RequestSignificance sig, SocketUser usr, bool ignoreAutoOT = false)
    {
        var attachment = Context.Message.Attachments.FirstOrDefault();
        if (attachment == default)
        {
            await ReplyAsync("No attachment provided!").ConfigureAwait(false);
            return;
        }
        var att = await NetUtil.DownloadPKMAsync(attachment).ConfigureAwait(false);
        var pk = GetRequest(att);
        if (pk == null)
        {
            await ReplyAsync("Attachment provided is not compatible with this module!").ConfigureAwait(false);
            return;
        }
        await AddTradeToQueueAsync(code, usr.Username, pk, sig, usr, ignoreAutoOT: ignoreAutoOT).ConfigureAwait(false);
    }

    private async Task HideTradeAsyncAttach(int code, RequestSignificance sig, SocketUser usr, bool ignoreAutoOT = false)
    {
        var attachment = Context.Message.Attachments.FirstOrDefault();
        if (attachment == default)
        {
            await ReplyAsync("No attachment provided!").ConfigureAwait(false);
            return;
        }

        var att = await NetUtil.DownloadPKMAsync(attachment).ConfigureAwait(false);
        var pk = GetRequest(att);
        if (pk == null)
        {
            await ReplyAsync("Attachment provided is not compatible with this module!").ConfigureAwait(false);
            return;
        }
        await AddTradeToQueueAsync(code, usr.Username, pk, sig, usr, isHiddenTrade: true, ignoreAutoOT: ignoreAutoOT).ConfigureAwait(false);
    }

    private static T? GetRequest(Download<PKM> dl)
    {
        if (!dl.Success)
            return null;
        return dl.Data switch
        {
            null => null,
            T pk => pk,
            _ => EntityConverter.ConvertToType(dl.Data, typeof(T), out _) as T,
        };
    }

    private async Task AddTradeToQueueAsync(int code, string trainerName, T? pk, RequestSignificance sig, SocketUser usr, bool isBatchTrade = false, int batchTradeNumber = 1, int totalBatchTrades = 1, bool isHiddenTrade = false, bool isMysteryEgg = false, List<Pictocodes>? lgcode = null, PokeTradeType tradeType = PokeTradeType.Specific, bool ignoreAutoOT = false, bool setEdited = false)
    {
        lgcode ??= TradeModule<T>.GenerateRandomPictocodes(3);
        if (pk is not null && !pk.CanBeTraded())
        {
            var reply = await ReplyAsync("Provided Pokémon content is blocked from trading!").ConfigureAwait(false);
            await Task.Delay(6000).ConfigureAwait(false); // Delay for 6 seconds
            await reply.DeleteAsync().ConfigureAwait(false);
            return;
        }
        var la = new LegalityAnalysis(pk!);
        if (!la.Valid)
        {
            string responseMessage;
            if (pk?.IsEgg == true)
            {
                string speciesName = SpeciesName.GetSpeciesName(pk.Species, (int)LanguageID.English);
                responseMessage = $"Invalid Showdown Set for the {speciesName} egg. Please review your information and try again.\n```\n{la.Report()}\n```";
            }
            else
            {
                string speciesName = SpeciesName.GetSpeciesName(pk!.Species, (int)LanguageID.English);
                responseMessage = $"{speciesName} attachment is not legal, and cannot be traded!\n\nLegality Report:\n```\n{la.Report()}\n```";
            }
            var reply = await ReplyAsync(responseMessage).ConfigureAwait(false);
            await Task.Delay(6000);
            await reply.DeleteAsync().ConfigureAwait(false);
            return;
        }
        bool isNonNative = false;
        if (la.EncounterOriginal.Context != pk?.Context || pk?.GO == true)
        {
            isNonNative = true;
        }
        if (Info.Hub.Config.Legality.DisallowNonNatives && (la.EncounterOriginal.Context != pk?.Context || pk?.GO == true))
        {
            // Allow the owner to prevent trading entities that require a HOME Tracker even if the file has one already.
            string speciesName = SpeciesName.GetSpeciesName(pk!.Species, (int)LanguageID.English);
            await ReplyAsync($"This **{speciesName}** is not native to this game, and cannot be traded!  Trade with the correct bot, then trade to HOME.").ConfigureAwait(false);
            return;
        }
        if (Info.Hub.Config.Legality.DisallowTracked && pk is IHomeTrack { HasTracker: true })
        {
            // Allow the owner to prevent trading entities that already have a HOME Tracker.
            string speciesName = SpeciesName.GetSpeciesName(pk.Species, (int)LanguageID.English);
            await ReplyAsync($"This {speciesName} file is tracked by HOME, and cannot be traded!").ConfigureAwait(false);
            return;
        }
        // handle past gen file requests
        // thanks manu https://github.com/Manu098vm/SysBot.NET/commit/d8c4b65b94f0300096704390cce998940413cc0d
        if (!la.Valid && la.Results.Any(m => m.Identifier is CheckIdentifier.Memory))
        {
            var clone = (T)pk!.Clone();
            clone.HandlingTrainerName = pk.OriginalTrainerName;
            clone.HandlingTrainerGender = pk.OriginalTrainerGender;
            if (clone is PK8 or PA8 or PB8 or PK9)
                ((dynamic)clone).HandlingTrainerLanguage = (byte)pk.Language;
            clone.CurrentHandler = 1;
            la = new LegalityAnalysis(clone);
            if (la.Valid) pk = clone;
        }
        await QueueHelper<T>.AddToQueueAsync(Context, code, trainerName, sig, pk!, PokeRoutineType.LinkTrade, tradeType, usr, isBatchTrade, batchTradeNumber, totalBatchTrades, isHiddenTrade, isMysteryEgg, lgcode, ignoreAutoOT: ignoreAutoOT, setEdited: setEdited, isNonNative: isNonNative).ConfigureAwait(false);
    }

    public static List<Pictocodes> GenerateRandomPictocodes(int count)
    {
        Random rnd = new();
        List<Pictocodes> randomPictocodes = [];
        Array pictocodeValues = Enum.GetValues(typeof(Pictocodes));

        for (int i = 0; i < count; i++)
        {
#pragma warning disable CS8605 // Unboxing a possibly null value.
            Pictocodes randomPictocode = (Pictocodes)pictocodeValues.GetValue(rnd.Next(pictocodeValues.Length));
#pragma warning restore CS8605 // Unboxing a possibly null value.
            randomPictocodes.Add(randomPictocode);
        }

        return randomPictocodes;
    }

    private async Task ReplyAndDeleteAsync(string message, int delaySeconds, IMessage? messageToDelete = null)
    {
        try
        {
            var sentMessage = await ReplyAsync(message).ConfigureAwait(false);
            _ = DeleteMessagesAfterDelayAsync(sentMessage, messageToDelete, delaySeconds);
        }
        catch (Exception ex)
        {
            LogUtil.LogSafe(ex, nameof(TradeModule<T>));
        }
    }

    private async Task DeleteMessagesAfterDelayAsync(IMessage? sentMessage, IMessage? messageToDelete, int delaySeconds)
    {
        try
        {
            await Task.Delay(delaySeconds * 1000);

            if (sentMessage != null)
            {
                try
                {
                    await sentMessage.DeleteAsync();
                }
                catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.UnknownMessage)
                {
                    // Ignore Unknown Message exception
                }
            }

            if (messageToDelete != null)
            {
                try
                {
                    await messageToDelete.DeleteAsync();
                }
                catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.UnknownMessage)
                {
                    // Ignore Unknown Message exception
                }
            }
        }
        catch (Exception ex)
        {
            LogUtil.LogSafe(ex, nameof(TradeModule<T>));
        }
    }

    [Command("homeReady")]
    [Alias("hr")]
    [Summary("Displays instructions on how to use the HOME-Ready module.")]
    private async Task HomeReadyInstructionsAsync()
    {
        var embed0 = new EmbedBuilder()
            .WithTitle("[HOME-READY MODULE INSTRUCTIONS]");

        embed0.WithImageUrl("https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/homereadybreak.png");
        var message0 = await ReplyAsync(embed: embed0.Build());


        var embed1 = new EmbedBuilder()
            .AddField($"GET LIST: `{Prefix}hrl <Pokemon>`",
                      $"- This will search for any Pokemon in the entire module.\n" +
                      $"**Example:** `{Prefix}hrl Mewtwo`\n");

        embed1.WithImageUrl("https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/homereadybreak.png");
        var message1 = await ReplyAsync(embed: embed1.Build());


        var embed2 = new EmbedBuilder()
            .AddField($"CHANGE PAGES: `{Prefix}hrl <page> <species>`",
                      $"- This will change the page you're viewing, with or without additional variables.\n" +
                      $"**Example:** `{Prefix}hrl 5 Charmander`\n");

        embed2.WithImageUrl("https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/homereadybreak.png");
        var message2 = await ReplyAsync(embed: embed2.Build());

        var embed3 = new EmbedBuilder()
            .AddField($"TRADING FILES: `{Prefix}hrr <number>`",
                      $"- This will trade you the Pokemon through the bot via the designated number.\n" +
                      $"**Example:** `{Prefix}hrr 682`\n");

        embed3.WithImageUrl("https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/homereadybreak.png");
        var message3 = await ReplyAsync(embed: embed3.Build());

        _ = Task.Run(async () =>
        {
            await Task.Delay(60_000);
            await message0.DeleteAsync();
            await message1.DeleteAsync();
            await message2.DeleteAsync();
            await message3.DeleteAsync();
        });
    }

    [Command("homeReadyRequest")]
    [Alias("hrr")]
    [Summary("Downloads HOME-ready attachments from the specified folder and adds it to the trade queue.")]
    [RequireQueueRole(nameof(DiscordManager.RolesTrade))]
    private async Task HOMEReadyRequestAsync(int index)
    {
        // Check if the user is already in the queue
        var userID = Context.User.Id;
        if (Info.IsUserInQueue(userID))
        {
            await ReplyAsync("You're already in a queue. Finish with your current queue before attempting to join another.").ConfigureAwait(false);
            return;
        }
        try
        {
            var homeReadyFolderPath = SysCord<T>.Runner.Config.Trade.RequestFolderSettings.HOMEReadyPKMFolder;
            var botPrefix = SysCord<T>.Runner.Config.Discord.CommandPrefix;
            var homeReadyFiles = Directory.GetFiles(homeReadyFolderPath)
                                            .Select(Path.GetFileName)
                                            .OrderBy(x => x)
                                            .ToList();

            // Check if the HOME-ready folder path is not set or empty
            if (string.IsNullOrEmpty(homeReadyFolderPath))
            {
                await ReplyAsync("This bot does not have this feature set up.");
                return;
            }

            if (index < 1 || index > homeReadyFiles.Count)
            {
                await ReplyAsync("Your selection was invalid. Please use a valid file number.").ConfigureAwait(false);
                return;
            }

            var selectedFile = homeReadyFiles[index - 1];
            var fileData = await File.ReadAllBytesAsync(Path.Combine(homeReadyFolderPath, selectedFile));

            var download = new Download<PKM>
            {
                Data = EntityFormat.GetFromBytes(fileData),
                Success = true
            };

            var pk = GetRequest(download);
            if (pk == null)
            {
                await ReplyAsync("Failed to convert the legal HOME-ready file to the required PKM type.").ConfigureAwait(false);
                return;
            }

            var code = Info.GetRandomTradeCode(userID, Context.Channel, Context.User);
            var lgcode = Info.GetRandomLGTradeCode();
            var sig = Context.User.GetFavor();
            var tradeMessage = await Context.Channel.SendMessageAsync($"HOME-Ready request added to queue.");
            await AddTradeToQueueAsync(code, Context.User.Username, pk, sig, Context.User, lgcode: lgcode).ConfigureAwait(false);

        }
        catch (Exception ex)
        {
            await ReplyAsync($"**Error:** {ex.Message}").ConfigureAwait(false);
        }
        finally
        {
            if (Context.Message is IUserMessage userMessage)
            {
                await userMessage.DeleteAsync().ConfigureAwait(false);
            }
        }

    }

    [Command("homeReadylist")]
    [Alias("hrl")]
    [Summary("Lists available HOME-ready files, filtered by a specific letter or substring, then sends the list to the channel.")]
    private async Task HOMEListAsync([Remainder] string args = "")
    {
        const int itemsPerPage = 10; // Number of items per page
        var homeReadyFolderPath = SysCord<T>.Runner.Config.Trade.RequestFolderSettings.HOMEReadyPKMFolder;

        // Check if the homeready folder path is not set or empty
        if (string.IsNullOrEmpty(homeReadyFolderPath))
        {
            await ReplyAsync("This bot does not have this feature set up.");
            return;
        }

        // Parsing the arguments to separate filter and page number
        string filter = "";
        int page = 1; // Declare and initialize the page variable
        var parts = args.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length > 0)
        {
            // Check if the last part is a number (page number)
            if (int.TryParse(parts.Last(), out int parsedPage))
            {
                page = parsedPage;
                filter = string.Join(" ", parts.Take(parts.Length - 1));
            }
            else
            {
                filter = string.Join(" ", parts);
            }
        }

        var allHOMEReadyFiles = Directory.GetFiles(homeReadyFolderPath)
                                           .Select(Path.GetFileName)
                                           .OrderBy(file => file)
                                           .ToList();

        var filteredHOMEReadyFiles = allHOMEReadyFiles
                                       .Where(file => string.IsNullOrWhiteSpace(filter) || file.Contains(filter, StringComparison.OrdinalIgnoreCase))
                                       .ToList();

        IUserMessage replyMessage;

        // Check if there are no files matching the filter
        if (!filteredHOMEReadyFiles.Any())
        {
            replyMessage = await ReplyAsync($"No HOME-ready files found matching the filter '{filter}'.");
        }
        else
        {
            var pageCount = (int)Math.Ceiling(filteredHOMEReadyFiles.Count / (double)itemsPerPage);
            page = Math.Clamp(page, 1, pageCount); // Ensure page number is within valid range

            var pageItems = filteredHOMEReadyFiles.Skip((page - 1) * itemsPerPage).Take(itemsPerPage);

            var embed = new EmbedBuilder()
                .WithTitle($"Available HOME-Ready Files - Filter: '{filter}'")
                .WithDescription($"Page {page} of {pageCount}")
                .WithColor(Color.Blue);

            foreach (var item in pageItems)

            {
                var index = allHOMEReadyFiles.IndexOf(item) + 1; // Get the index from the original list
                embed.AddField($"{index}. {item}", $"Use `{Prefix}hrr {index}` to trade this legal HOME-ready file.");
            }

            // Send confirmation message in the same channel
            replyMessage = await ReplyAsync($"Use `{Prefix}hrl <page>` to change the page you are viewing.\n**Current Support:** USUM/LGPE/POGO/BDSP/SWSH/PLA -> SV.");
            var message = await ReplyAsync(embed: embed.Build());

            // Delay for 20 seconds
            await Task.Delay(20_000);

            // Delete the message
            await message.DeleteAsync();

        }
    }
}
