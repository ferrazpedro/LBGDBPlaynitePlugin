﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Xml.Serialization;
using EFCore.BulkExtensions;
using LBGDBMetadata.Extensions;
using LBGDBMetadata.LaunchBox.Api;
using LBGDBMetadata.LaunchBox.Metadata;
using Microsoft.EntityFrameworkCore;
using Playnite.SDK;
using Playnite.SDK.Plugins;
using Game = Playnite.SDK.Models.Game;

namespace LBGDBMetadata
{
    public class LbgdbMetadataPlugin : MetadataPlugin
    {
        private readonly LbgdbApi _lbgdbApi;
        internal readonly LbgdbMetadataSettings Settings;
        public HttpClient HttpClient { get; private set; } = new HttpClient();

        public Dictionary<string, string> PlatformTranslationTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) 
        {
            {"3dointeractivemultiplayer","3dointeractivemultiplayer"},
            {"adobeflash","webbrowser"},
            {"amstradcpc","amstradcpc"},
            {"appleii","appleii"},
            {"appleiigs","appleiigs"},
            {"atari2600","atari2600"},
            {"atari5200","atari5200"},
            {"atari7800","atari7800"},
            {"atari800","atari800"},
            {"atarijaguar","atarijaguar"},
            {"atarilynx","atarilynx"},
            {"atariststettfalcon","atarist"},
            {"bandaiwonderswan","wonderswan"},
            {"bandaiwonderswancolor","wonderswancolor"},
            {"capcomcpsystemi","arcade"},
            {"capcomcpsystemii","arcade"},
            {"capcomcpsystemiii","arcade"},
            {"cavecv1000","arcade"},
            {"colecocolecovision","colecovision"},
            {"colecovision","colecovision"},
            {"commodore64","commodore64"},
            {"commodoreamiga","commodoreamiga"},
            {"commodoreamigacd32","commodoreamigacd32"},
            {"commodorec128","commodore128"},
            {"commodorepet","commodorepet"},
            {"commodoreplus4","commodoreplus4"},
            {"commodorevic20","commodorevic20"},
            {"daphne","arcade"},
            {"dos","msdos"},
            {"gcevectrex","gcevectrex"},
            {"magnavoxodyssey2","magnavoxodyssey2"},
            {"mame2003plus","arcade"},
            {"mattelintellivision","mattelintellivision"},
            {"microsoftmsx","microsoftmsx"},
            {"microsoftmsx2","microsoftmsx2"},
            {"microsoftxbox","microsoftxbox"},
            {"microsoftxbox360","microsoftxbox360"},
            {"necpc9801","necpc9801"},
            {"necpcfx","necpcfx"},
            {"necturbografx16","necturbografx16"},
            {"necturbografxcd","necturbografxcd"},
            {"nintendo3ds","nintendo3ds"},
            {"nintendo64","nintendo64"},
            {"nintendo64dd","nintendo64dd"},
            {"nintendods","nintendods"},
            {"nes","nintendoentertainmentsystem"},
            {"nintendoentertainmentsystem","nintendoentertainmentsystem"},
            {"nintendofamilycomputerdisksystem","nintendofamicomdisksystem"},
            {"nintendogameboy","nintendogameboy"},
            {"nintendogameboyadvance","nintendogameboyadvance"},
            {"nintendogameboycolor","nintendogameboycolor"},
            {"nintendogamecube","nintendogamecube"},
            {"nintendosatellaview","nintendosatellaview"},
            {"nintendoswitch","nintendoswitch"},
            {"nintendovirtualboy","nintendovirtualboy"},
            {"nintendowii","nintendowii"},
            {"nintendowiiu","nintendowiiu"},
            {"pc","windows"},
            {"pcenginesupergrafx","pcenginesupergrafx"},
            {"philipscdi","philipscdi"},
            {"phillipsvideopac","philipsvideopac"},
            {"rpgmaker","windows"},
            {"sammyatomiswave","sammyatomiswave"},
            {"sega32x","sega32x"},
            {"segacd","segacd"},
            {"segadreamcast","segadreamcast"},
            {"segagamegear","segagamegear"},
            {"segagenesis","segagenesis"},
            {"segahikaru","segahikaru"},
            {"segamastersystem","segamastersystem"},
            {"segamodel2","segamodel2"},
            {"seganaomi","seganaomi"},
            {"segapico","segapico"},
            {"segasaturn","segasaturn"},
            {"segasg1000","segasg1000"},
            {"segastv","segastv"},
            {"sharpx68000","sharpx68000"},
            {"sinclairzx81","sinclairzx81"},
            {"sinclairzxspectrum","sinclairzxspectrum"},
            {"snkneogeo","snkneogeoaes"},
            {"snkneogeocd","snkneogeocd"},
            {"snkneogeopocket","snkneogeopocket"},
            {"snkneogeopocketcolor","snkneogeopocketcolor"},
            {"sonyplaystation","sonyplaystation"},
            {"sonyplaystation2","sonyplaystation2"},
            {"sonyplaystation3","sonyplaystation3"},
            {"sonyplaystationvita","sonyplaystationvita"},
            {"sonypsp","sonypsp"},
            {"supernintendoentertainmentsystem","supernintendoentertainmentsystem"},
            {"snes","supernintendoentertainmentsystem"}
        };

        public LbgdbMetadataPlugin(IPlayniteAPI playniteAPI) : base(playniteAPI)
        {
            using (var metadataContext = new MetaDataContext(GetPluginUserDataPath()))
            {
                metadataContext.Database.Migrate();
            }
            Settings = new LbgdbMetadataSettings(this);
            var apiOptions = new Options
            {
                MetaDataFileName = Settings.MetaDataFileName,
                MetaDataURL = Settings.MetaDataURL
            };
            _lbgdbApi = new LbgdbApi(apiOptions);
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return Settings;
        }

        public override UserControl GetSettingsView(bool firstRunView)
        {
            return new LbgdbMetadataSettingsView(this);
        }

        public async Task<bool> NewMetadataAvailable()
        {
            var newMetadataHash = await _lbgdbApi.GetMetadataHash();
            return !Settings.OldMetadataHash.Equals(newMetadataHash, StringComparison.OrdinalIgnoreCase);
        }

        private async Task<bool> ImportXml<T>(Stream metaDataStream, int bufferSize = 10000) where T : class
        {
            var xElementList = metaDataStream.AsEnumerableXml(typeof(T).Name);
            XmlSerializer xmlSerializer = new XmlSerializer(typeof(T));
            using (var context = new MetaDataContext(GetPluginUserDataPath()))
            {
                context.ChangeTracker.AutoDetectChangesEnabled = false;
                var objectList = new List<T>(bufferSize);
                foreach (var xElement in xElementList)
                {
                    T deserializedObject;
                    using (var reader = xElement.CreateReader())
                    {
                        deserializedObject = (T)xmlSerializer.Deserialize(reader);
                    }

                    switch (deserializedObject)
                    {
                        case LaunchBox.Metadata.Game game:
                            game.NameSearch = game.Name.Sanitize();
                            game.PlatformSearch = game.Platform.Sanitize();
                            if (game.CommunityRating != null)
                            {
                                game.CommunityRating = Math.Round(((decimal)game.CommunityRating / 5) * 100, 0);
                            }
                            break;
                        case GameAlternateName game:
                            game.NameSearch = game.AlternateName.Sanitize();
                            break;
                    }

                    objectList.Add(deserializedObject);

                    if (objectList.Count >= bufferSize)
                    {
                        await context.BulkInsertAsync(objectList);
                        objectList.Clear();
                    }

                }

                if (objectList.Count > 0)
                {
                    await context.BulkInsertAsync(objectList);
                }
            }

            return true;
        }

        public bool HasData()
        {
            using (var metaDataContext = new MetaDataContext(GetPluginUserDataPath()))
            {
                if (metaDataContext.Games.Any())
                {
                    return true;
                }
            }

            return false;
        }


        public async Task<string> UpdateMetadata(GlobalProgressActionArgs progress)
        {
            var newMetadataHash = await _lbgdbApi.GetMetadataHash();
            var zipFile = await _lbgdbApi.DownloadMetadata();

            await Task.Run(async () =>
            {
                using (var zipArchive = new ZipArchive(zipFile, ZipArchiveMode.Read))
                {
                    var metaData = zipArchive.Entries.FirstOrDefault(entry =>
                        entry.Name.Equals(Settings.MetaDataFileName, StringComparison.OrdinalIgnoreCase));

                    if (metaData != null)
                    {
                        //progress.Text = "Updating database...";
                        using (var context = new MetaDataContext(GetPluginUserDataPath()))
                        {
                            await context.Database.EnsureDeletedAsync();
                            await context.Database.MigrateAsync();
                        }
                        progress.CurrentProgressValue++;

                        //progress.Text = "Importing games...";
                        using (var metaDataStream = metaData.Open())
                        {
                            await ImportXml<LaunchBox.Metadata.Game>(metaDataStream);
                        }
                        progress.CurrentProgressValue++;

                        //progress.Text = "Importing alternate game names...";
                        using (var metaDataStream = metaData.Open())
                        {
                            await ImportXml<GameAlternateName>(metaDataStream);
                        }
                        progress.CurrentProgressValue++;

                        //progress.Text = "Importing media...";
                        using (var metaDataStream = metaData.Open())
                        {
                            await ImportXml<GameImage>(metaDataStream);
                        }
                        progress.CurrentProgressValue++;
                    }
                }
            });
            Settings.OldMetadataHash = newMetadataHash;
            Settings.EndEdit();

            return newMetadataHash;
        }

        public void UpdateMetadata(string filename)
        {
            using (var zipArchive = ZipFile.Open(filename, ZipArchiveMode.Read))
            {
                var metaData = zipArchive.Entries.FirstOrDefault(entry =>
                    entry.Name.Equals("MetaData.xml", StringComparison.OrdinalIgnoreCase));

                if (metaData != null)
                {
                    using (var metaDataStream = metaData.Open())
                    {
                        var games = metaDataStream.AsEnumerableXml("Game");
                        var xmlSerializer = new XmlSerializer(typeof(LaunchBox.Metadata.Game));

                        var i = 0;
                        var context = new MetaDataContext(GetPluginUserDataPath());
                        context.ChangeTracker.AutoDetectChangesEnabled = false;

                        foreach (var xElement in games)
                        {
                            var gameMetaData =
                                (LaunchBox.Metadata.Game) xmlSerializer.Deserialize(xElement.CreateReader());
                            i++;
                            if (i++ > 1000)
                            {
                                context.SaveChanges();
                                i = 0;
                                context.Dispose();
                                context = new MetaDataContext(GetPluginUserDataPath());
                                context.ChangeTracker.AutoDetectChangesEnabled = false;
                            }

                            context.Games.Add(gameMetaData);
                        }

                        context.Dispose();
                    }
                }
            }
        }

        public override Guid Id { get; } = Guid.Parse("000001D9-DBD1-46C6-B5D0-B1BA559D10E4");
        public override OnDemandMetadataProvider GetMetadataProvider(MetadataRequestOptions options)
        {
            return new LbgdbMetadataProvider(options, this);
        }

        public override string Name { get; } = "Launchbox";
        public override List<MetadataField> SupportedFields { get; } = new List<MetadataField>
        {
            MetadataField.Name,
            MetadataField.Genres,
            MetadataField.ReleaseDate,
            MetadataField.Developers,
            MetadataField.Publishers,
            MetadataField.Description,
            MetadataField.Links,
            MetadataField.CriticScore,
            MetadataField.CommunityScore,
            MetadataField.Icon,
            MetadataField.CoverImage,
            MetadataField.BackgroundImage

        };
    }
}
