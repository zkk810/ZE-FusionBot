using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.WebSocket;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using PKHeX.Drawing.PokeSprite;
using SysBot.Pokemon.Discord.Commands.Bots;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Color = System.Drawing.Color;
using DiscordColor = Discord.Color;

namespace SysBot.Pokemon.Discord;

public static class QueueHelper<T> where T : PKM, new()
{
    private const uint MaxTradeCode = 9999_9999;

    public static async Task AddToQueueAsync(SocketCommandContext context, int code, string trainer, RequestSignificance sig, T trade, PokeRoutineType routine, PokeTradeType type, SocketUser trader, bool isBatchTrade = false, int batchTradeNumber = 1, int totalBatchTrades = 1, bool isHiddenTrade = false, bool isMysteryEgg = false, List<Pictocodes>? lgcode = null, bool ignoreAutoOT = false, bool setEdited = false, bool isNonNative = false)
    {
        if ((uint)code > MaxTradeCode)
        {
            await context.Channel.SendMessageAsync("Trade code should be 00000000-99999999!").ConfigureAwait(false);
            return;
        }

        try
        {
            // Only send trade code for non-batch trades (batch container will handle its own)
            if (!isBatchTrade)
            {
                if (trade is PB7 && lgcode != null)
                {
                    var (thefile, lgcodeembed) = CreateLGLinkCodeSpriteEmbed(lgcode);
                    await trader.SendFileAsync(thefile, $"Your trade code will be.", embed: lgcodeembed).ConfigureAwait(false);
                }
                else
                {
                    await EmbedHelper.SendTradeCodeEmbedAsync(trader, code).ConfigureAwait(false);
                }
            }

            var result = await AddToTradeQueue(context, trade, code, trainer, sig, routine, type, trader, isBatchTrade, batchTradeNumber, totalBatchTrades, isHiddenTrade, isMysteryEgg, lgcode, ignoreAutoOT, setEdited, isNonNative).ConfigureAwait(false);
        }
        catch (HttpException ex)
        {
            await HandleDiscordExceptionAsync(context, trader, ex).ConfigureAwait(false);
        }
    }

    public static Task AddToQueueAsync(SocketCommandContext context, int code, string trainer, RequestSignificance sig, T trade, PokeRoutineType routine, PokeTradeType type, bool ignoreAutoOT = false)
    {
        return AddToQueueAsync(context, code, trainer, sig, trade, routine, type, context.User, ignoreAutoOT: ignoreAutoOT);
    }

    private static async Task<TradeQueueResult> AddToTradeQueue(SocketCommandContext context, T pk, int code, string trainerName, RequestSignificance sig, PokeRoutineType type, PokeTradeType t, SocketUser trader, bool isBatchTrade, int batchTradeNumber, int totalBatchTrades, bool isHiddenTrade, bool isMysteryEgg = false,

        List<Pictocodes>? lgcode = null, bool ignoreAutoOT = false, bool setEdited = false, bool isNonNative = false)
    {
        var user = trader;
        var userID = user.Id;
        var name = user.Username;

        // Generate unique trade ID first
        int uniqueTradeID = GenerateUniqueTradeID();

        var trainer = new PokeTradeTrainerInfo(trainerName, userID);
        var notifier = new DiscordTradeNotifier<T>(
            pk,
            trainer,
            code,
            trader,
            batchTradeNumber,
            totalBatchTrades,
            isMysteryEgg,
            lgcode: lgcode,
            queuedTradeID: uniqueTradeID
        );

        var detail = new PokeTradeDetail<T>(
            pk,
            trainer,
            notifier,
            t,
            code,
            sig == RequestSignificance.Favored,
            lgcode,
            batchTradeNumber,
            totalBatchTrades,
            isMysteryEgg,
            isHiddenTrade,
            uniqueTradeID,
            ignoreAutoOT,
            setEdited
        );
        var trade = new TradeEntry<T>(detail, userID, PokeRoutineType.LinkTrade, name, uniqueTradeID);
        var hub = SysCord<T>.Runner.Hub;
        var Info = hub.Queues.Info;
        var isSudo = sig == RequestSignificance.Owner;
        var added = Info.AddToTradeQueue(trade, userID, false, isSudo);

        // Start queue position updates for Discord notification
        if (added != QueueResultAdd.AlreadyInQueue && notifier is DiscordTradeNotifier<T> discordNotifier)
        {
            await discordNotifier.SendInitialQueueUpdate().ConfigureAwait(false);
        }
        int totalTradeCount = 0;
        TradeCodeStorage.TradeCodeDetails? tradeDetails = null;
        if (SysCord<T>.Runner.Config.Trade.TradeConfiguration.StoreTradeCodes)
        {
            var tradeCodeStorage = new TradeCodeStorage();
            totalTradeCount = tradeCodeStorage.GetTradeCount(trader.Id);
            tradeDetails = tradeCodeStorage.GetTradeDetails(trader.Id);
        }
        if (added == QueueResultAdd.AlreadyInQueue)
        {
            return new TradeQueueResult(false);
        }
        var embedData = DetailsExtractor<T>.ExtractPokemonDetails(
        pk, trader, isMysteryEgg, type == PokeRoutineType.Clone, type == PokeRoutineType.Dump,
        type == PokeRoutineType.FixOT, type == PokeRoutineType.SeedCheck, false, 1, 1
        );

        try
        {
            (string embedImageUrl, DiscordColor embedColor) = await PrepareEmbedDetails(pk);
            embedData.EmbedImageUrl = isMysteryEgg ? "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/mysteryegg3.png?raw=true&width=300&height=300" :
            type == PokeRoutineType.Dump ? "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/Dumping.png?raw=true&width=300&height=300" :
            type == PokeRoutineType.Clone ? "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/Cloning.png?raw=true&width=300&height=300" :
            type == PokeRoutineType.SeedCheck ? "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/Seeding.png?raw=true&width=300&height=300" :
            type == PokeRoutineType.FixOT ? "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/FixOTing.png?raw=true&width=300&height=300" :
            embedImageUrl;
            embedData.HeldItemUrl = string.Empty;
            if (!string.IsNullOrWhiteSpace(embedData.HeldItem))
            {
                string heldItemName = embedData.HeldItem.ToLower().Replace(" ", "");
                embedData.HeldItemUrl = $"https://serebii.net/itemdex/sprites/{heldItemName}.png";
            }
            embedData.IsLocalFile = File.Exists(embedData.EmbedImageUrl);

            var position = Info.CheckPosition(userID, uniqueTradeID, type);
            var botct = Info.Hub.Bots.Count;
            var baseEta = position.Position > botct ? Info.Hub.Config.Queues.EstimateDelay(position.Position, botct) : 0;
            var etaMessage = $"Estimated: {baseEta:F1} min(s) for trade {batchTradeNumber}/{totalBatchTrades}";
            string footerText = string.Empty;

            var userDetails = DetailsExtractor<T>.GetUserDetails(totalTradeCount, tradeDetails, etaMessage, (position.Position, totalBatchTrades)); // Pass etaMessage here

            footerText += !string.IsNullOrEmpty(userDetails) ? $"{userDetails}\n" : string.Empty; // Check if userDetails is not empty before appending
            footerText += $"ZE FusionBot {TradeBot.Version}";

            var embedBuilder = new EmbedBuilder()
                .WithColor(embedColor)
                .WithImageUrl(embedData.IsLocalFile ? $"attachment://{Path.GetFileName(embedData.EmbedImageUrl)}" : embedData.EmbedImageUrl)
                .WithFooter(footerText)
                .WithAuthor(new EmbedAuthorBuilder()
                .WithName(embedData.AuthorName)
                .WithIconUrl(trader.GetAvatarUrl() ?? trader.GetDefaultAvatarUrl())
                .WithUrl("https://genpkm.com/pokecreator.php"));
            DetailsExtractor<T>.AddAdditionalText(embedBuilder);
            if (!isMysteryEgg && type != PokeRoutineType.Clone && type != PokeRoutineType.Dump && type != PokeRoutineType.FixOT && type != PokeRoutineType.SeedCheck)
            {
                DetailsExtractor<T>.AddNormalTradeFields(embedBuilder, embedData, trader.Mention, pk);
            }
            else
            {
                DetailsExtractor<T>.AddSpecialTradeFields(embedBuilder, isMysteryEgg, type == PokeRoutineType.SeedCheck, type == PokeRoutineType.Clone, type == PokeRoutineType.FixOT, trader.Mention);
            }

            // If Auto-Corrected
            if (setEdited && Info.Hub.Config.Trade.AutoCorrectConfig.AutoCorrectEmbedIndicator)
            {
                embedBuilder.Footer.IconUrl = "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/setedited.png";
                embedBuilder.AddField("**__Notice__:** Your request was illegal.", "*Auto-Corrected to closest legal match.*");
            }

            // Check if the Pokemon is Non-Native and/or has a Home Tracker
            if (pk is IHomeTrack homeTrack)
            {
                if (homeTrack.HasTracker && isNonNative)
                {
                    // Both Non-Native and has Home Tracker
                    embedBuilder.Footer.IconUrl = "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/setedited.png";
                    embedBuilder.AddField("**__Notice__**: **This Pokemon is Non-Native & Has HOME Tracker.**", "*AutoOT not applied.*");
                }
                else if (homeTrack.HasTracker)
                {
                    // Only has Home Tracker
                    embedBuilder.Footer.IconUrl = "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/setedited.png";
                    embedBuilder.AddField("**__Notice__**: **Home Tracker Detected.**", "*AutoOT not applied.*");
                }
                else if (isNonNative)
                {
                    // Only Non-Native
                    embedBuilder.Footer.IconUrl = "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/setedited.png";
                    embedBuilder.AddField("**__Notice__**: **This Pokémon is Non-Native.**", "*Cannot enter HOME & AutoOT not applied.*");
                }
            }
            else if (isNonNative)
            {
                // Fallback for Non-Native Pokemon that don't implement IHomeTrack
                embedBuilder.Footer.IconUrl = "https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/setedited.png";
                embedBuilder.AddField("**__Notice__**: **This Pokémon is Non-Native.**", "*Cannot enter HOME & AutoOT not applied.*");
            }


            DetailsExtractor<T>.AddThumbnails(embedBuilder, type == PokeRoutineType.Clone, type == PokeRoutineType.SeedCheck, embedData.HeldItemUrl);
            if (!isHiddenTrade && SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.UseEmbeds)
            {
                var embed = embedBuilder.Build();
                if (embed == null)
                {
                    Console.WriteLine("Error: Embed is null.");
                    await context.Channel.SendMessageAsync("An error occurred while preparing the trade details.");
                    return new TradeQueueResult(false);
                }

                if (embedData.IsLocalFile)
                {
                    await context.Channel.SendFileAsync(embedData.EmbedImageUrl, embed: embed);
                    await ScheduleFileDeletion(embedData.EmbedImageUrl, 0);
                }
            else
            {
                await context.Channel.SendMessageAsync(embed: embed);
            }
        }
            else
            {
                var message = $"▹𝗦𝗨𝗖𝗖𝗘𝗦𝗦𝗙𝗨𝗟𝗟𝗬 𝗔𝗗𝗗𝗘𝗗◃\n" +
                 $"//【𝐔𝐒𝐄𝐑: Publicly Hidden User】\n" +
                 $"//【𝐐𝐔𝐄𝐔𝐄: LinkTrade】\n" +
                 $"//【𝐏𝐎𝐒𝐈𝐓𝐈𝐎𝐍: {position.Position}】\n";

                if (embedData.SpeciesName != "---")
                {
                    message += $"//【𝐏𝐎𝐊𝐄𝐌𝐎𝐍: {embedData.SpeciesName}】\n";
                }

                message += $"//【𝐄𝐓𝐀: {baseEta:F1} Min(s)】";
                await context.Channel.SendMessageAsync(message);
            }
        }
        catch (HttpException ex)
        {
            await HandleDiscordExceptionAsync(context, trader, ex);
            return new TradeQueueResult(false);
        }

        return new TradeQueueResult(true);
    }

    public static async Task AddBatchContainerToQueueAsync(SocketCommandContext context, int code, string trainer, T firstTrade, List<T> allTrades, RequestSignificance sig, SocketUser trader, int totalBatchTrades)
    {
        var userID = trader.Id;
        var name = trader.Username;
        var trainer_info = new PokeTradeTrainerInfo(trainer, userID);

        // Generate the unique trade ID FIRST!
        int uniqueTradeID = GenerateUniqueTradeID();

        // Pass it into the notifier constructor
        var notifier = new DiscordTradeNotifier<T>(
            firstTrade,
            trainer_info,
            code,
            trader,
            1,
            totalBatchTrades,
            false,
            lgcode: null,
            queuedTradeID: uniqueTradeID
        );

        var detail = new PokeTradeDetail<T>(
            firstTrade,
            trainer_info,
            notifier,
            PokeTradeType.Batch,
            code,
            sig == RequestSignificance.Favored,
            null,
            1,
            totalBatchTrades,
            false,
            uniqueTradeID: uniqueTradeID
        )
        {
            BatchTrades = allTrades
        };

        var trade = new TradeEntry<T>(detail, userID, PokeRoutineType.Batch, name, uniqueTradeID);
        var hub = SysCord<T>.Runner.Hub;
        var Info = hub.Queues.Info;
        var added = Info.AddToTradeQueue(trade, userID, false, sig == RequestSignificance.Owner);

        // Send trade code once
        await EmbedHelper.SendTradeCodeEmbedAsync(trader, code).ConfigureAwait(false);

        // Start queue position updates for Discord notification
        if (added != QueueResultAdd.AlreadyInQueue && notifier is DiscordTradeNotifier<T> discordNotifier)
        {
            await discordNotifier.SendInitialQueueUpdate().ConfigureAwait(false);
        }

        // Handle the display
        if (added == QueueResultAdd.AlreadyInQueue)
        {
            await context.Channel.SendMessageAsync("You are already in the queue!").ConfigureAwait(false);
            return;
        }
        var position = Info.CheckPosition(userID, uniqueTradeID, PokeRoutineType.Batch);
        var botct = Info.Hub.Bots.Count;
        var baseEta = position.Position > botct ? Info.Hub.Config.Queues.EstimateDelay(position.Position, botct) : 0;

        // Get user trade details for footer
        int totalTradeCount = 0;
        TradeCodeStorage.TradeCodeDetails? tradeDetails = null;
        if (SysCord<T>.Runner.Config.Trade.TradeConfiguration.StoreTradeCodes)
        {
            var tradeCodeStorage = new TradeCodeStorage();

            // Fetch current count
            totalTradeCount = tradeCodeStorage.GetTradeCount(trader.Id);

            // Always true in batch context
            bool isBatchTrade = true;
            int batchTradeNumber = 1; // This will get updated inside the loop

            // Adjust total trade count assuming first trade is being processed now
            if (isBatchTrade)
                totalTradeCount += batchTradeNumber;

            tradeDetails = tradeCodeStorage.GetTradeDetails(trader.Id);
        }

        // Create and send embeds for each Pokémon in the batch
        if (SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.UseEmbeds)
        {
            for (int i = 0; i < allTrades.Count; i++)
            {
                var pk = allTrades[i];
                var batchTradeNumber = i + 1;

                // Extract details for this Pokémon
                var embedData = DetailsExtractor<T>.ExtractPokemonDetails(
                pk, trader, false, false, false, false, false, true, batchTradeNumber, totalBatchTrades
                );
                try
                {

                    // Prepare embed details
                    (string embedImageUrl, DiscordColor embedColor) = await PrepareEmbedDetails(pk);
                    embedData.EmbedImageUrl = embedImageUrl;
                    embedData.HeldItemUrl = string.Empty;
                    if (!string.IsNullOrWhiteSpace(embedData.HeldItem))
                    {
                        string heldItemName = embedData.HeldItem.ToLower().Replace(" ", "");
                        embedData.HeldItemUrl = $"https://serebii.net/itemdex/sprites/{heldItemName}.png";
                    }

                    embedData.IsLocalFile = File.Exists(embedData.EmbedImageUrl);

                    // Build footer text with batch info
                    double tradeEta = baseEta + (batchTradeNumber - 1); // adds 1 min per Pokémon in batch (except first one)
                    string etaMessage = $"Estimated: {tradeEta:F1} min(s) for trade {batchTradeNumber}/{totalBatchTrades}";
                    string userDetailsText = DetailsExtractor<T>.GetUserDetails(
                        totalTradeCount,
                        tradeDetails,
                        etaMessage,
                        (position.Position, position.TotalBatchTrades)
                    );

                    // Build the footer text in a clean order
                    var sb = new StringBuilder();
                    if (!string.IsNullOrEmpty(userDetailsText))
                        sb.AppendLine(userDetailsText);

                    sb.Append($"ZE FusionBot {TradeBot.Version}");
                    string footerText = sb.ToString();

                    // Create embed
                    var embedBuilder = new EmbedBuilder()
                    .WithColor(embedColor)
                    .WithImageUrl(embedData.IsLocalFile ? $"attachment://{Path.GetFileName(embedData.EmbedImageUrl)}" : embedData.EmbedImageUrl)
                    .WithFooter(footerText)
                    .WithAuthor(new EmbedAuthorBuilder()
                    .WithName(embedData.AuthorName)
                    .WithIconUrl(trader.GetAvatarUrl() ?? trader.GetDefaultAvatarUrl())
                    .WithUrl("https://genpkm.com"));
                    DetailsExtractor<T>.AddAdditionalText(embedBuilder);
                    DetailsExtractor<T>.AddNormalTradeFields(embedBuilder, embedData, trader.Mention, pk);

                    // Check for Non-Native and Home Tracker
                    bool isNonNative = false; // You may need to pass this from the batch trade processing
                    if (pk is IHomeTrack homeTrack)
                    {
                        if (homeTrack.HasTracker)
                        {
                            embedBuilder.AddField("**__Notice__**: **Home Tracker Detected.**", "*AutoOT not applied.*");
                        }
                    }

                    DetailsExtractor<T>.AddThumbnails(embedBuilder, false, false, embedData.HeldItemUrl);
                    var embed = embedBuilder.Build();

                    // Send embed
                    if (embedData.IsLocalFile)
                    {
                        await context.Channel.SendFileAsync(embedData.EmbedImageUrl, embed: embed);
                        await ScheduleFileDeletion(embedData.EmbedImageUrl, 0);
                    }

                    else
                    {
                        await context.Channel.SendMessageAsync(embed: embed);
                    }

                    // Small delay between embeds to avoid rate limiting
                    if (i < allTrades.Count - 1)
                    {
                        await Task.Delay(500);
                    }
                }

                catch (HttpException ex)
                {
                    await HandleDiscordExceptionAsync(context, trader, ex);
                }
            }
        }
    }

    private static int GenerateUniqueTradeID()
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int randomValue = new Random().Next(1000);
        int uniqueTradeID = (int)(timestamp % int.MaxValue) * 1000 + randomValue;
        return uniqueTradeID;
    }

    private static string GetImageFolderPath()
    {
        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string imagesFolder = Path.Combine(baseDirectory, "Images");

        if (!Directory.Exists(imagesFolder))
        {
            Directory.CreateDirectory(imagesFolder);
        }

        return imagesFolder;
    }

    private static string SaveImageLocally(System.Drawing.Image image)
    {
        // Get the path to the images folder
        string imagesFolderPath = GetImageFolderPath();

        // Create a unique filename for the image
        string filePath = Path.Combine(imagesFolderPath, $"image_{Guid.NewGuid()}.png");

        // Check if the platform supports the required functionality
        if (OperatingSystem.IsWindowsVersionAtLeast(6, 1))
        {
            // Save the image to the specified path
            image.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
        }
        else
        {
            throw new PlatformNotSupportedException("Image saving is only supported on Windows 6.1 or later.");
        }

        return filePath;
    }

    private static async Task<(string, DiscordColor)> PrepareEmbedDetails(T pk)
    {
        string embedImageUrl;
        string speciesImageUrl;

        if (pk.IsEgg)
        {
            string eggImageUrl = GetEggTypeImageUrl(pk);
            speciesImageUrl = AbstractTrade<T>.PokeImg(pk, false, true, null);
            System.Drawing.Image? combinedImage = await OverlaySpeciesOnEgg(eggImageUrl, speciesImageUrl);
            if (combinedImage != null)
                embedImageUrl = SaveImageLocally(combinedImage);
            else
                embedImageUrl = speciesImageUrl;

            if (combinedImage == null)
            {
                throw new InvalidOperationException("Failed to create combined image for egg.");
            }

            embedImageUrl = SaveImageLocally(combinedImage);
        }
        else
        {
            bool canGmax = pk is PK8 pk8 && pk8.CanGigantamax;
            speciesImageUrl = AbstractTrade<T>.PokeImg(pk, canGmax, false, SysCord<T>.Runner.Config.Trade.TradeEmbedSettings.PreferredImageSize);
            embedImageUrl = speciesImageUrl;
        }

        var strings = GameInfo.GetStrings("en");
        string ballName = strings.balllist[pk.Ball];
        if (ballName.Contains("(LA)"))
        {
            ballName = "la" + ballName.Replace(" ", "").Replace("(LA)", "").ToLower();
        }
        else
        {
            ballName = ballName.Replace(" ", "").ToLower();
        }

        string ballImgUrl = $"https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/AltBallImg/28x28/{ballName}.png";

        // Check if embedImageUrl is a local file or a web URL
        if (Uri.TryCreate(embedImageUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeFile)
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
            {
                throw new PlatformNotSupportedException("This functionality is only supported on Windows 6.1 or later.");
            }

            using var localImage = await Task.Run(() => System.Drawing.Image.FromFile(uri.LocalPath));
            using var ballImage = await LoadImageFromUrl(ballImgUrl);
            if (ballImage != null)
            {
                using (var graphics = Graphics.FromImage(localImage))
                {
                    var ballPosition = new Point(localImage.Width - ballImage.Width, localImage.Height - ballImage.Height);
                    graphics.DrawImage(ballImage, ballPosition);
                }
                embedImageUrl = SaveImageLocally(localImage);
            }
        }
        else
        {
            (System.Drawing.Image? finalCombinedImage, bool ballImageLoaded) = await OverlayBallOnSpecies(speciesImageUrl, ballImgUrl);

            if (finalCombinedImage == null)
            {
                throw new InvalidOperationException("Failed to create combined image for species and ball.");
            }

            embedImageUrl = SaveImageLocally(finalCombinedImage);

            if (!ballImageLoaded)
            {
                Console.WriteLine($"Ball image could not be loaded: {ballImgUrl}");
            }
        }

        (int R, int G, int B) = await GetDominantColorAsync(embedImageUrl);
        return (embedImageUrl, new DiscordColor(R, G, B));
    }

    private static async Task<(System.Drawing.Image?, bool)> OverlayBallOnSpecies(string speciesImageUrl, string ballImageUrl)
    {
        var speciesImage = await LoadImageFromUrl(speciesImageUrl);
        if (speciesImage == null)
        {
            Console.WriteLine("Species image could not be loaded.");
            return (null!, false); // Use null! to explicitly indicate that null is not expected
        }

        var ballImage = await LoadImageFromUrl(ballImageUrl);
        if (ballImage == null)
        {
            Console.WriteLine($"Ball image could not be loaded: {ballImageUrl}");
            return (speciesImage, false);
        }

        try
        {
            using (speciesImage)
            using (ballImage)
            {
                // Ensure compatibility with supported platforms
                if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
                {
                    throw new PlatformNotSupportedException("This functionality is only supported on Windows 6.1 or later.");
                }

                var ballPosition = new System.Drawing.Point(
                    Math.Max(0, speciesImage.Width - ballImage.Width),
                    Math.Max(0, speciesImage.Height - ballImage.Height)
                );

                using (var graphics = System.Drawing.Graphics.FromImage(speciesImage))
                {
                    // Replace the problematic method with a platform-agnostic alternative
                    graphics.DrawImage(ballImage, new Rectangle(ballPosition.X, ballPosition.Y, ballImage.Width, ballImage.Height));
                }

                return (speciesImage, true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error while overlaying ball on species: {ex.Message}");
            return (speciesImage, false);
        }
    }
    private static async Task<System.Drawing.Image?> OverlaySpeciesOnEgg(string eggImageUrl, string speciesImageUrl)
    {
        System.Drawing.Image? eggImage = await LoadImageFromUrl(eggImageUrl);
        if (eggImage == null)
        {
            throw new InvalidOperationException($"Failed to load egg image from URL: {eggImageUrl}");
        }

        System.Drawing.Image? speciesImage = await LoadImageFromUrl(speciesImageUrl);
        if (speciesImage == null)
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(6, 1))
            {
                eggImage.Dispose(); // Dispose eggImage if speciesImage fails to load
            }
            throw new InvalidOperationException($"Failed to load species image from URL: {speciesImageUrl}");
        }

        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
        {
            throw new PlatformNotSupportedException("This functionality is only supported on Windows 6.1 or later.");
        }

        double scaleRatio = Math.Min((double)eggImage.Width / speciesImage.Width, (double)eggImage.Height / speciesImage.Height);
        Size newSize = new((int)(speciesImage.Width * scaleRatio), (int)(speciesImage.Height * scaleRatio));

        using System.Drawing.Image? resizedSpeciesImage = new Bitmap(speciesImage, newSize);

        using (Graphics g = Graphics.FromImage(eggImage))
        {
            int speciesX = (eggImage.Width - resizedSpeciesImage.Width) / 2;
            int speciesY = (eggImage.Height - resizedSpeciesImage.Height) / 2;
            g.DrawImage(resizedSpeciesImage, speciesX, speciesY, resizedSpeciesImage.Width, resizedSpeciesImage.Height);
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(6, 1))
        {
            speciesImage.Dispose();
        }

        double scale = Math.Min(128.0 / eggImage.Width, 128.0 / eggImage.Height);
        int newWidth = (int)(eggImage.Width * scale);
        int newHeight = (int)(eggImage.Height * scale);

        Bitmap finalImage = new(128, 128);
        using (Graphics g = Graphics.FromImage(finalImage))
        {
            int x = (128 - newWidth) / 2;
            int y = (128 - newHeight) / 2;
            g.DrawImage(eggImage, x, y, newWidth, newHeight);
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(6, 1))
        {
            eggImage.Dispose();
        }

        return finalImage;
    }

    private static async Task<System.Drawing.Image?> LoadImageFromUrl(string url)
    {
        using HttpClient client = new();
        HttpResponseMessage response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Failed to load image from {url}. Status code: {response.StatusCode}");
            return null;
        }

        Stream stream = await response.Content.ReadAsStreamAsync();
        if (stream == null || stream.Length == 0)
        {
            Console.WriteLine($"No data or empty stream received from {url}");
            return null;
        }

        try
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(6, 1))
            {
                return System.Drawing.Image.FromStream(stream);
            }
            else
            {
                throw new PlatformNotSupportedException("Image loading is only supported on Windows 6.1 or later.");
            }
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"Failed to create image from stream. URL: {url}, Exception: {ex}");
            return null;
        }
        finally
        {
            stream.Dispose();
        }
    }

    private static async Task ScheduleFileDeletion(string filePath, int delayInMilliseconds)
    {
            await Task.Delay(delayInMilliseconds);
            DeleteFile(filePath);
        }

    private static void DeleteFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Error deleting file: {ex.Message}");
            }
        }
    }

    public enum AlcremieDecoration
    {
        Strawberry = 0,
        Berry = 1,
        Love = 2,
        Star = 3,
        Clover = 4,
        Flower = 5,
        Ribbon = 6,
    }

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

    private static async Task HandleDiscordExceptionAsync(SocketCommandContext context, SocketUser trader, HttpException ex)
    {
        string message = string.Empty;
        switch (ex.DiscordCode)
        {
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
                        var app = await context.Client.GetApplicationInfoAsync().ConfigureAwait(false);
                        var owner = app.Owner.Id;
                        message = $"<@{owner}> You must grant me \"Manage Messages\" permissions!";
                    }
                }
                break;
            case DiscordErrorCode.CannotSendMessageToUser:
                {
                    // The user either has DMs turned off, or Discord thinks they do.
                    message = context.User == trader ? "You must enable private messages in order to be queued!" : "The mentioned user must enable private messages in order for them to be queued!";
                }
                break;
            default:
                {
                    // Send a generic error message.
                    message = ex.DiscordCode != null ? $"Discord error {(int)ex.DiscordCode}: {ex.Reason}" : $"Http error {(int)ex.HttpCode}: {ex.Message}";
                }
                break;
        }
        await context.Channel.SendMessageAsync(message).ConfigureAwait(false);
    }

    private static string GetEggTypeImageUrl(T pk)
    {
        var pi = pk.PersonalInfo;
        byte typeIndex = pi.Type1;
        string[] typeNames = [
        "Normal", "Fighting", "Flying", "Poison", "Ground", "Rock", "Bug", "Ghost",
        "Steel", "Fire", "Water", "Grass", "Electric", "Psychic", "Ice", "Dragon",
        "Dark", "Fairy"
        ];
        string typeName = (typeIndex >= 0 && typeIndex < typeNames.Length)
        ? typeNames[typeIndex]
        : "Normal";
        return $"https://raw.githubusercontent.com/Secludedly/ZE-FusionBot-Sprite-Images/main/Eggs/Egg_{typeName}.png";
    }

    public static (string, Embed) CreateLGLinkCodeSpriteEmbed(List<Pictocodes> lgcode)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
        {
            throw new PlatformNotSupportedException("This functionality is only supported on Windows 6.1 or later.");
        }

        int codecount = 0;
        List<System.Drawing.Image> spritearray = new List<System.Drawing.Image>();
        foreach (Pictocodes cd in lgcode)
        {
            var showdown = new ShowdownSet(cd.ToString());
            var sav = BlankSaveFile.Get(EntityContext.Gen7b, "pip");
            PKM pk = sav.GetLegalFromSet(showdown).Created;
            System.Drawing.Image png = pk.Sprite();
            var destRect = new Rectangle(-40, -65, 137, 130);
            var destImage = new Bitmap(137, 130);

            destImage.SetResolution(png.HorizontalResolution, png.VerticalResolution);

            using (var graphics = Graphics.FromImage(destImage))
            {
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                graphics.DrawImage(png, destRect, 0, 0, png.Width, png.Height, GraphicsUnit.Pixel);
            }
            png = destImage;
            spritearray.Add(png);
            codecount++;
        }

        int outputImageWidth = spritearray[0].Width + 20;
        int outputImageHeight = spritearray[0].Height - 65;

        Bitmap outputImage = new (outputImageWidth, outputImageHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        using (Graphics graphics = Graphics.FromImage(outputImage))
        {
            graphics.DrawImage(spritearray[0], new Rectangle(0, 0, spritearray[0].Width, spritearray[0].Height),
                new Rectangle(new Point(), spritearray[0].Size), GraphicsUnit.Pixel);
            graphics.DrawImage(spritearray[1], new Rectangle(50, 0, spritearray[1].Width, spritearray[1].Height),
                new Rectangle(new Point(), spritearray[1].Size), GraphicsUnit.Pixel);
            graphics.DrawImage(spritearray[2], new Rectangle(100, 0, spritearray[2].Width, spritearray[2].Height),
                new Rectangle(new Point(), spritearray[2].Size), GraphicsUnit.Pixel);
        }

        System.Drawing.Image finalembedpic = outputImage;
        var filename = $"{Directory.GetCurrentDirectory()}//finalcode.png";
        finalembedpic.Save(filename);
        filename = Path.GetFileName($"{Directory.GetCurrentDirectory()}//finalcode.png");
        Embed returnembed = new EmbedBuilder().WithTitle($"{lgcode[0]}, {lgcode[1]}, {lgcode[2]}").WithImageUrl($"attachment://{filename}").Build();
        return (filename, returnembed);
    }
}
