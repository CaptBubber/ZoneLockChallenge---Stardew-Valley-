using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace ZoneLockChallenge
{
    public class ContentProvider
    {
        private const string SpriteAssetName = "Mods/ZoneLockChallenge/Sprites";
        private const string ZoneDataAssetName = "Mods/ZoneLockChallenge/ZoneData";
        private const string RewardsAssetName = "Mods/ZoneLockChallenge/Rewards";
        private const string MineGatesAssetName = "Mods/ZoneLockChallenge/MineGates";

        private readonly IModHelper helper;
        private readonly ModConfig config;

        private Texture2D spritesTexture;

        public ContentProvider(IModHelper helper, ModConfig config)
        {
            this.helper = helper;
            this.config = config;
            helper.Events.Content.AssetRequested += OnAssetRequested;
        }

        public Texture2D GetSprites()
        {
            spritesTexture ??= helper.GameContent.Load<Texture2D>(SpriteAssetName);
            return spritesTexture;
        }

        public void InvalidateCache()
        {
            spritesTexture = null;
            helper.GameContent.InvalidateCache(SpriteAssetName);
            helper.GameContent.InvalidateCache(ZoneDataAssetName);
            helper.GameContent.InvalidateCache(RewardsAssetName);
            helper.GameContent.InvalidateCache(MineGatesAssetName);
        }

        public Dictionary<string, ZoneContentData> LoadZoneData()
        {
            return helper.GameContent.Load<Dictionary<string, ZoneContentData>>(ZoneDataAssetName);
        }

        public Dictionary<string, RewardContentData> LoadRewards()
        {
            return helper.GameContent.Load<Dictionary<string, RewardContentData>>(RewardsAssetName);
        }

        public List<MineLevelGate> LoadMineGates()
        {
            return helper.GameContent.Load<List<MineLevelGate>>(MineGatesAssetName);
        }

        private void OnAssetRequested(object sender, AssetRequestedEventArgs e)
        {
            if (e.NameWithoutLocale.IsEquivalentTo(SpriteAssetName))
            {
                e.LoadFromModFile<Texture2D>("assets/sprites.png", AssetLoadPriority.Medium);
            }
            else if (e.NameWithoutLocale.IsEquivalentTo(ZoneDataAssetName))
            {
                e.LoadFrom(() => BuildDefaultZoneData(), AssetLoadPriority.Low);
            }
            else if (e.NameWithoutLocale.IsEquivalentTo(RewardsAssetName))
            {
                e.LoadFrom(() => BuildDefaultRewards(), AssetLoadPriority.Low);
            }
            else if (e.NameWithoutLocale.IsEquivalentTo(MineGatesAssetName))
            {
                e.LoadFrom(() => BuildDefaultMineGates(), AssetLoadPriority.Low);
            }
        }

        private Dictionary<string, ZoneContentData> BuildDefaultZoneData()
        {
            var data = new Dictionary<string, ZoneContentData>();
            foreach (var zone in config.Zones)
            {
                data[zone.ZoneId] = new ZoneContentData
                {
                    DisplayName = zone.DisplayName,
                    BundleName = zone.BundleName,
                    Description = zone.Description,
                    UnlockType = zone.UnlockType,
                    MoneyCost = zone.MoneyCost,
                    Items = zone.Items,
                    LocationNames = zone.LocationNames,
                    LocationPrefixes = zone.LocationPrefixes,
                    RequiresZone = zone.RequiresZone,
                    RequiredSkill = zone.RequiredSkill,
                    RequiredSkillLevel = zone.RequiredSkillLevel,
                    PlateLocation = zone.Plate?.LocationName,
                    PlateX = zone.Plate?.X ?? 0,
                    PlateY = zone.Plate?.Y ?? 0
                };
            }
            return data;
        }

        private Dictionary<string, RewardContentData> BuildDefaultRewards()
        {
            var data = new Dictionary<string, RewardContentData>();
            foreach (var zone in config.Zones)
            {
                if (zone.Rewards != null && zone.Rewards.Count > 0)
                    data[zone.ZoneId] = new RewardContentData { Items = zone.Rewards };
            }
            return data;
        }

        private List<MineLevelGate> BuildDefaultMineGates()
        {
            return config.MineLevelGates?.Select(g => new MineLevelGate
            {
                FloorNumber = g.FloorNumber,
                RequiredMiningLevel = g.RequiredMiningLevel
            }).ToList() ?? new List<MineLevelGate>();
        }

        public void OnAssetInvalidated(object sender, AssetsInvalidatedEventArgs e)
        {
            foreach (var name in e.NamesWithoutLocale)
            {
                if (name.IsEquivalentTo(SpriteAssetName))
                    spritesTexture = null;
            }
        }
    }

    public class ZoneContentData
    {
        public string DisplayName { get; set; }
        public string BundleName { get; set; }
        public string Description { get; set; }
        public string UnlockType { get; set; } = "permanent";
        public int MoneyCost { get; set; }
        public List<ItemCost> Items { get; set; } = new();
        public List<string> LocationNames { get; set; } = new();
        public List<string> LocationPrefixes { get; set; } = new();
        public string RequiresZone { get; set; }
        public string RequiredSkill { get; set; }
        public int RequiredSkillLevel { get; set; }
        public string PlateLocation { get; set; }
        public int PlateX { get; set; }
        public int PlateY { get; set; }
    }

    public class RewardContentData
    {
        public List<ItemCost> Items { get; set; } = new();
    }
}
