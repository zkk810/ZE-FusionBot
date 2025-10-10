using Discord;
using Discord.Commands;
using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Color = System.Drawing.Color;
using DiscordColor = Discord.Color;

namespace SysBot.Pokemon.Discord;

public class TradeStartModule<T> : ModuleBase<SocketCommandContext> where T : PKM, new()
{
    private class TradeStartAction(ulong ChannelId, Action<PokeRoutineExecutorBase, PokeTradeDetail<T>> messager, string channel)
        : ChannelAction<PokeRoutineExecutorBase, PokeTradeDetail<T>>(ChannelId, messager, channel);

    private static DiscordSocketClient? _discordClient;

    private static readonly Dictionary<ulong, TradeStartAction> Channels = [];

    private static void Remove(TradeStartAction entry)
    {
        Channels.Remove(entry.ChannelID);
        SysCord<T>.Runner.Hub.Queues.Forwarders.Remove(entry.Action);
    }

#pragma warning disable RCS1158 // Static member in generic type should use a type parameter.
    public static void RestoreTradeStarting(DiscordSocketClient discord)
    {
        _discordClient = discord; // Store the DiscordSocketClient instance

        var cfg = SysCordSettings.Settings;
        foreach (var ch in cfg.TradeStartingChannels)
        {
            if (discord.GetChannel(ch.ID) is ISocketMessageChannel c)
                AddLogChannel(c, ch.ID);
        }

        LogUtil.LogInfo("Added Trade Start Notification to Discord channel(s) on Bot startup.", "Discord");
    }

    public static bool IsStartChannel(ulong cid)
#pragma warning restore RCS1158 // Static member in generic type should use a type parameter.
    {
        return Channels.TryGetValue(cid, out _);
    }

    [Command("startHere")]
    [Summary("Makes the bot log trade starts to the channel.")]
    [RequireSudo]
    public async Task AddLogAsync()
    {
        var c = Context.Channel;
        var cid = c.Id;
        if (Channels.TryGetValue(cid, out _))
        {
            await ReplyAsync("Already logging here.").ConfigureAwait(false);
            return;
        }

        AddLogChannel(c, cid);

        // Add to discord global loggers (saves on program close)
        SysCordSettings.Settings.TradeStartingChannels.AddIfNew([GetReference(Context.Channel)]);
        await ReplyAsync("Added Start Notification output to this channel!").ConfigureAwait(false);
    }

    private static readonly Dictionary<string, string> BallFileMap = new(StringComparer.OrdinalIgnoreCase)
{
    { "poke ball", "poke_ball" },
    { "great ball", "great_ball" },
    { "ultra ball", "ultra_ball" },
    { "master ball", "master_ball" },
    { "dive ball", "dive_ball" },
    { "nest ball", "nest_ball" },
    { "repeat ball", "repeat_ball" },
    { "timer ball", "timer_ball" },
    { "luxury ball", "luxury_ball" },
    { "premier ball", "premier_ball" },
    { "heal ball", "heal_ball" },
    { "quick ball", "quick_ball" },
    { "dusk ball", "dusk_ball" },
    { "cherish ball", "cherish_ball" },
    { "friend ball", "friend_ball" },
    { "level ball", "level_ball" },
    { "lure ball", "lure_ball" },
    { "moon ball", "moon_ball" },
    { "love ball", "love_ball" },
    { "fast ball", "fast_ball" },
    { "heavy ball", "heavy_ball" },
    { "dream ball", "dream_ball" },
    { "beast ball", "beast_ball" },
    { "safari ball", "safari_ball" },
    { "sport ball", "sport_ball" },
    { "strange ball", "strange_ball" },
};

    private static void AddLogChannel(ISocketMessageChannel c, ulong cid)
    {
        async void Logger(PokeRoutineExecutorBase bot, PokeTradeDetail<T> detail)
        {
            if (detail.Type == PokeTradeType.Random)
                return;

            var user = _discordClient?.GetUser(detail.Trainer.ID);
            if (user == null)
            {
                Console.WriteLine($"User not found for ID {detail.Trainer.ID}.");
                return;
            }

            string speciesName = detail.TradeData != null
                ? GameInfo.Strings.Species[detail.TradeData.Species]
                : "";

            // Default fallback icon
            string ballImgUrl = "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/specialrequest.gif";

            if (detail.TradeData != null &&
                detail.Type is not (PokeTradeType.Clone or PokeTradeType.Dump or PokeTradeType.Seed or PokeTradeType.FixOT))
            {
                try
                {
                    string rawBallName = GameInfo.GetStrings(1).balllist[detail.TradeData.Ball].Trim();

                        if (!string.IsNullOrEmpty(rawBallName) && BallFileMap.TryGetValue(rawBallName, out string? fileName) && fileName != null)
                    {
                        ballImgUrl = $"https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/AltBallImg/28x28/{fileName}.png";
                        Console.WriteLine($"Resolved ball icon: {ballImgUrl}");
                    }
                    else
                    {
                        Console.WriteLine($"Unknown ball name: '{rawBallName}' — using fallback icon.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error resolving ball image: {ex.Message}");
                }
            }

            string tradeTitle = detail.IsMysteryEgg ? "✨ Mystery Egg" : detail.Type switch
            {
                PokeTradeType.Clone => "Cloned Pokémon",
                PokeTradeType.Dump => "Pokémon Dump",
                PokeTradeType.FixOT => "Fixing OT",
                PokeTradeType.Seed => "Special Request",
                _ => speciesName
            };

            string embedImageUrl = detail.IsMysteryEgg
                ? "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/mysteryegg3.png"
                : detail.Type switch
                {
                    PokeTradeType.Clone => "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/Cloning.png",
                    PokeTradeType.Dump => "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/Dumping.png",
                    PokeTradeType.FixOT => "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/FixOTing.png",
                    PokeTradeType.Seed => "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/Seeding.png",
                    _ => detail.TradeData != null
                        ? AbstractTrade<T>.PokeImg(detail.TradeData, false, true)
                        : ""
                };

            var (r, g, b) = await GetDominantColorAsync(embedImageUrl);

            string footerText = detail.Type switch
            {
                PokeTradeType.Clone or PokeTradeType.Dump or PokeTradeType.Seed or PokeTradeType.FixOT
                    => "Now Initializing...",
                _ => $"Now Initializing...\nYour {(detail.IsMysteryEgg ? "✨ Mystery Egg" : speciesName)} is now on its way!"
            };

            // --- NEW: Handle hidden trades cleanly
            string authorText;
            string? authorIconUrl = null;

            if (detail.IsHiddenTrade)
            {
                authorText = "Up Next: Publicly Hidden User";
                authorIconUrl = "https://i.imgur.com/pTqYqXP.gif";
            }
            else
            {
                authorText = $"Up Next: {user.Username}";
                authorIconUrl = user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl();
            }

            var embed = new EmbedBuilder()
                .WithColor(new DiscordColor(r, g, b))
                .WithThumbnailUrl(embedImageUrl)
                .WithAuthor(authorText, authorIconUrl)
                .WithDescription($"**Receiving**: {tradeTitle}\n**Bot Trade ID**: {detail.ID}")
                .WithFooter($"{footerText}\u200B", ballImgUrl)
                .Build();

            await c.SendMessageAsync(embed: embed);

        }

        SysCord<T>.Runner.Hub.Queues.Forwarders.Add(Logger);
        Channels.Add(cid, new TradeStartAction(cid, Logger, c.Name));
    }



    [Command("startInfo")]
    [Summary("Dumps the Start Notification settings.")]
    [RequireSudo]
    public async Task DumpLogInfoAsync()
    {
        foreach (var c in Channels)
            await ReplyAsync($"{c.Key} - {c.Value}").ConfigureAwait(false);
    }

    [Command("startClear")]
    [Summary("Clears the Start Notification settings in that specific channel.")]
    [RequireSudo]
    public async Task ClearLogsAsync()
    {
        var cfg = SysCordSettings.Settings;
        if (Channels.TryGetValue(Context.Channel.Id, out var entry))
            Remove(entry);
        cfg.TradeStartingChannels.RemoveAll(z => z.ID == Context.Channel.Id);
        await ReplyAsync($"Start Notifications cleared from channel: {Context.Channel.Name}").ConfigureAwait(false);
    }

    [Command("startClearAll")]
    [Summary("Clears all the Start Notification settings.")]
    [RequireSudo]
    public async Task ClearLogsAllAsync()
    {
        foreach (var l in Channels)
        {
            var entry = l.Value;
            await ReplyAsync($"Logging cleared from {entry.ChannelName} ({entry.ChannelID}!").ConfigureAwait(false);
            SysCord<T>.Runner.Hub.Queues.Forwarders.Remove(entry.Action);
        }
        Channels.Clear();
        SysCordSettings.Settings.TradeStartingChannels.Clear();
        await ReplyAsync("Start Notifications cleared from all channels!").ConfigureAwait(false);
    }

    private RemoteControlAccess GetReference(IChannel channel) => new()
    {
        ID = channel.Id,
        Name = channel.Name,
        Comment = $"Added by {Context.User.Username} on {DateTime.Now:yyyy.MM.dd-hh:mm:ss}",
    };

    public static async Task<(int R, int G, int B)> GetDominantColorAsync(string imagePath)
    {
        try
        {
            Bitmap image = await LoadImageAsync(imagePath);

            var colorCount = new Dictionary<Color, int>();
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    var pixelColor = image.GetPixel(x, y);

                    if (pixelColor.A < 128 || pixelColor.GetBrightness() > 0.9) continue;

                    var brightnessFactor = (int)(pixelColor.GetBrightness() * 100);
                    var saturationFactor = (int)(pixelColor.GetSaturation() * 100);
                    var combinedFactor = brightnessFactor + saturationFactor;

                    var quantizedColor = Color.FromArgb(
                        pixelColor.R / 10 * 10,
                        pixelColor.G / 10 * 10,
                        pixelColor.B / 10 * 10
                    );

                    if (colorCount.ContainsKey(quantizedColor))
                    {
                        colorCount[quantizedColor] += combinedFactor;
                    }
                    else
                    {
                        colorCount[quantizedColor] = combinedFactor;
                    }
                }
            }

            image.Dispose();

            if (colorCount.Count == 0)
                return (255, 255, 255);

            var dominantColor = colorCount.Aggregate((a, b) => a.Value > b.Value ? a : b).Key;
            return (dominantColor.R, dominantColor.G, dominantColor.B);
        }
        catch (Exception ex)
        {
            // Log or handle exceptions as needed
            Console.WriteLine($"Error processing image from {imagePath}. Error: {ex.Message}");
            return (255, 255, 255);  // Default to white if an exception occurs
        }
    }

    private static async Task<Bitmap> LoadImageAsync(string imagePath)
    {
        if (imagePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            using var httpClient = new HttpClient();
            using var response = await httpClient.GetAsync(imagePath);
            using var stream = await response.Content.ReadAsStreamAsync();
            return new Bitmap(stream);
        }
        else
        {
            return new Bitmap(imagePath);
        }
    }
}
