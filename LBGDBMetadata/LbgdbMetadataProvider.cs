using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LBGDBMetadata.Extensions;
using LBGDBMetadata.LaunchBox;
using LBGDBMetadata.LaunchBox.Metadata;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using Game = LBGDBMetadata.LaunchBox.Metadata.Game;

namespace LBGDBMetadata
{
    public class LbgdbMetadataProvider : OnDemandMetadataProvider
    {
        private readonly MetadataRequestOptions _options;
        private readonly LbgdbMetadataPlugin _plugin;
        private Game _game;
        private Dictionary<string, int> _regionPriority = new Dictionary<string, int>();

        public LbgdbMetadataProvider(MetadataRequestOptions options, LbgdbMetadataPlugin plugin)
        {
            _options = options;
            _plugin = plugin;
        }

        private int GetWeightedRating(double communityRatingCount, double communityRating)
        {
            double positiveVotes = Math.Floor((communityRating / 100) * communityRatingCount);
            double negativeVotes = communityRatingCount - positiveVotes;

            double totalVotes = positiveVotes + negativeVotes;
            double average = totalVotes < 1 ? 0 : positiveVotes / totalVotes;
            double score = average - (average - 0.5) * Math.Pow(2, -Math.Log10(totalVotes + 1));

            return (int)(score * 100);
        }

        private GameImage GetBestImage(List<GameImage> images, HashSet<string> imageTypes)
        {
            if (images.Count < 1)
            {
                return null;
            }

            foreach (var coverType in imageTypes)
            {
                if (images.All(image => image.Type != coverType))
                {
                    continue;
                }

                return images
                    .Where(image => image.Type == coverType && _regionPriority.ContainsKey(image.Region ?? ""))
                    .OrderBy((n) =>
                    {
                        if (_regionPriority.ContainsKey(n.Region ?? ""))
                        {
                            return _regionPriority[n.Region ?? ""];
                        }

                        return int.MaxValue;
                    }).FirstOrDefault();
            }
            return images.FirstOrDefault();
        }

        private Game GetGame()
        {
            if (_game is null)
            {
                var gameSearchName = "";
                if (!string.IsNullOrWhiteSpace(_options?.GameData?.Name))
                {
                    gameSearchName = _options.GameData.Name.Sanitize();
                }

                if (!string.IsNullOrWhiteSpace(gameSearchName))
                {
                    if (_options?.GameData != null && _regionPriority.Count < 1)
                    {
                        if (_options.GameData.Regions != null && !string.IsNullOrWhiteSpace(_options.GameData.Regions[0].Name))
                        {
                            _regionPriority = _options.GameData.Regions[0]?.Name.GetRegionPriorityList();
                        }
                        else
                        {
                            if (!string.IsNullOrWhiteSpace(_options.GameData.Roms[0]?.Path))
                            {
                                var noIntoRegion = _options.GameData.Roms[0]?.Path.GetRegionNoIntro();
                                if (!string.IsNullOrWhiteSpace(noIntoRegion))
                                {
                                    _regionPriority = noIntoRegion.GetRegionPriorityList();
                                }
                            }
                        }
                    }

                    var platformSearchName = "";
                    if (!string.IsNullOrWhiteSpace(_options?.GameData?.Platforms[0]?.SpecificationId))
                    {
                        var sanitizedPlatform = _options.GameData.Platforms[0].SpecificationId;
                        platformSearchName = _plugin.PlatformTranslationTable.ContainsKey(sanitizedPlatform)
                            ? _plugin.PlatformTranslationTable[sanitizedPlatform]
                            : sanitizedPlatform;
                    }

                    using (var context = new MetaDataContext(_plugin.GetPluginUserDataPath()))
                    {
                        /* Can't tell which region the actual game name is from in the game object...
                        if (_regionPriority.Count > 0)
                        {
                            var alternateNames = context.GameAlternateName.Where(game =>
                                game.Game.PlatformSearch == platformSearchName && (game.NameSearch == gameSearchName || game.Game.NameSearch == gameSearchName)).ToList();

                            var regionGameName = alternateNames.Where(game => _regionPriority.ContainsKey(game.Region ?? "")).OrderBy((n) =>
                            {
                                if (_regionPriority.ContainsKey(n.Region ?? ""))
                                {
                                    return _regionPriority[n.Region ?? ""];
                                }

                                return int.MaxValue;
                            }).FirstOrDefault();

                            if (regionGameName != null)
                            {
                                _game = context.Games.FirstOrDefault(
                                    game => game.DatabaseID == regionGameName.DatabaseID);

                                if (_game != null)
                                {
                                    if (!string.IsNullOrWhiteSpace(regionGameName.AlternateName))
                                    {
                                        _game.Name = regionGameName.AlternateName;
                                    }
                                }
                            }
                        }
                        */
                        if (_game is null)
                        {
                            _game = context.Games.FirstOrDefault(game =>
                                game.PlatformSearch == platformSearchName && (game.NameSearch == gameSearchName ||
                                                                              game.AlternateNames.Any(alternateName =>
                                                                                  alternateName.NameSearch ==
                                                                                  gameSearchName)));

                            if (_game?.NameSearch != null && _game?.NameSearch != gameSearchName)
                            {
                                var alternateGameNames = context.GameAlternateName.Where(alternateName =>
                                    alternateName.DatabaseID == _game.DatabaseID && alternateName.NameSearch ==
                                    gameSearchName);

                                var numberOfNames = alternateGameNames.Count();

                                if (numberOfNames > 0)
                                {
                                    var gameName = alternateGameNames.First();
                                    if (!string.IsNullOrWhiteSpace(gameName.AlternateName))
                                    {
                                        _game.Name = gameName.AlternateName;
                                    }

                                    if (numberOfNames < 2 && !string.IsNullOrWhiteSpace(gameName.Region))
                                    {
                                        if (_regionPriority.Count < 1)
                                        {
                                            _regionPriority = gameName.Region.GetRegionPriorityList();
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (_game != null && _regionPriority.Count < 1)
                    {
                        _regionPriority = LaunchBox.Region.GetRegionPriorityList(null);
                    }
                }
            }

            return _game;
        }

        public override string GetName(GetMetadataFieldArgs args)
        {
            var game = GetGame();

            if (game != null)
            {
                if (!string.IsNullOrWhiteSpace(game.Name))
                {
                    return game.Name;
                }
            }

            return base.GetName(args);
        }

        public override IEnumerable<MetadataProperty> GetGenres(GetMetadataFieldArgs args)
        {
            var game = GetGame();

            if (game != null)
            {
                if (!string.IsNullOrWhiteSpace(game.Genres))
                {
                    return game.Genres.Split(';').Select(genre => genre.Trim())
                        .OrderBy(genre => genre.Trim()).ToList()
                        .Select(a => new MetadataNameProperty(a)).ToList(); ;
                }
            }

            return base.GetGenres(args);
        }

        public override ReleaseDate? GetReleaseDate(GetMetadataFieldArgs args)
        {
            var game = GetGame();

            if (game?.ReleaseDate != null)
            {
                return new ReleaseDate(game.ReleaseDate.Value);
            }

            return base.GetReleaseDate(args);
        }

        public override IEnumerable<MetadataProperty> GetDevelopers(GetMetadataFieldArgs args)
        {
            var game = GetGame();

            if (game != null)
            {
                if (!string.IsNullOrWhiteSpace(game.Developer))
                {
                    return game.Developer.Split(';').Select(developer => developer.Trim())
                        .OrderBy(developer => developer.Trim()).ToList()
                        .Select(a => new MetadataNameProperty(a)).ToList(); ;
                }
            }

            return base.GetDevelopers(args);
        }

        public override IEnumerable<MetadataProperty> GetPublishers(GetMetadataFieldArgs args)
        {
            var game = GetGame();

            if (game != null)
            {
                if (!string.IsNullOrWhiteSpace(game.Publisher))
                {
                    return game.Publisher.Split(';').Select(publisher => publisher.Trim())
                        .OrderBy(publisher => publisher.Trim()).ToList()
                        .Select(a => new MetadataNameProperty(a)).ToList(); ;
                }
            }

            return base.GetPublishers(args);
        }


        public override string GetDescription(GetMetadataFieldArgs args)
        {
            var game = GetGame();

            if (game != null)
            {
                if (!string.IsNullOrWhiteSpace(game.Overview))
                {
                    return game.Overview;
                }
            }

            return base.GetDescription(args);
        }

        public override int? GetCommunityScore(GetMetadataFieldArgs args)
        {
            var game = GetGame();

            if (game != null)
            {
                if (game.CommunityRating != null && game.CommunityRatingCount > 0)
                {
                    return GetWeightedRating(game.CommunityRatingCount, (double)game.CommunityRating);
                }
            }

            return base.GetCommunityScore(args);
        }

        public override MetadataFile GetCoverImage(GetMetadataFieldArgs args)
        {
            var game = GetGame();

            if (game != null)
            {
                using (var context = new MetaDataContext(_plugin.GetPluginUserDataPath()))
                {
                    var coverImage = GetBestImage(context.GameImages.Where(image => image.DatabaseID == game.DatabaseID && LaunchBox.Image.ImageType.Cover.Contains(image.Type)).ToList(), LaunchBox.Image.ImageType.Cover);
                    if (coverImage != null)
                    {
                        return new MetadataFile("https://images.launchbox-app.com/" + coverImage.FileName);
                    }
                }
            }

            return base.GetCoverImage(args);
        }

        public override MetadataFile GetBackgroundImage(GetMetadataFieldArgs args)
        {
            var game = GetGame();

            if (game != null)
            {
                using (var context = new MetaDataContext(_plugin.GetPluginUserDataPath()))
                {
                    var backgroundImage = GetBestImage(context.GameImages.Where(image => image.DatabaseID == game.DatabaseID && LaunchBox.Image.ImageType.Background.Contains(image.Type)).ToList(), LaunchBox.Image.ImageType.Background);
                    if (backgroundImage != null)
                    {
                        return new MetadataFile("https://images.launchbox-app.com/" + backgroundImage.FileName);
                    }
                }
            }

            return base.GetBackgroundImage(args);
        }

        public override IEnumerable<Link> GetLinks(GetMetadataFieldArgs args)
        {
            var game = GetGame();

            if (game != null)
            {
                var links = new List<Link>
                {
                    new Link("LaunchBox", "https://gamesdb.launchbox-app.com/games/dbid/" + game.DatabaseID)
                };

                if (!string.IsNullOrWhiteSpace(game.WikipediaURL))
                {
                    links.Add(new Link("Wikipedia", game.WikipediaURL));
                }

                if (!string.IsNullOrWhiteSpace(game.VideoURL))
                {
                    links.Add(new Link("Video", game.VideoURL));
                }

                return links;
            }

            return base.GetLinks(args);
        }

        public override IEnumerable<MetadataProperty> GetFeatures(GetMetadataFieldArgs args)
        {
            return base.GetFeatures(args);
        }

        public override List<MetadataField> AvailableFields
        {
            get
            {
                return _plugin.SupportedFields;
            }
        }

        public override MetadataFile GetIcon(GetMetadataFieldArgs args)
        {
            var game = GetGame();

            if (game != null)
            {
                using (var context = new MetaDataContext(_plugin.GetPluginUserDataPath()))
                {
                    var icon =
                        GetBestImage(
                            context.GameImages.Where(image =>
                                image.DatabaseID == game.DatabaseID &&
                                LaunchBox.Image.ImageType.Icon.Contains(image.Type)).ToList(),
                            LaunchBox.Image.ImageType.Icon);
                    if (icon != null)
                    {
                        var imageData = _plugin.HttpClient.GetByteArrayAsync("https://images.launchbox-app.com/" + icon.FileName).Result;

                        using (Image image = Image.Load(imageData))
                        {
                            image.Mutate(x =>
                            {
                                x.Resize(new ResizeOptions
                                {
                                    Size = new Size(256, 256),
                                    Mode = ResizeMode.Pad
                                });
                            });

                            using (MemoryStream ms = new MemoryStream())
                            {
                                image.Save(ms, new PngEncoder());
                                return new MetadataFile(icon.FileName, ms.ToArray(), "https://images.launchbox-app.com/" + icon.FileName);
                            }
                        }
                    }
                }
            }

            return base.GetIcon(args);
        }

        public override int? GetCriticScore(GetMetadataFieldArgs args)
        {
            return base.GetCriticScore(args);
        }

        public override IEnumerable<MetadataProperty> GetTags(GetMetadataFieldArgs args)
        {
            return base.GetTags(args);
        }
    }
}
