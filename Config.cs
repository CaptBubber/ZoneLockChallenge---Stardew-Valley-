using System.Collections.Generic;

namespace ZoneLockChallenge
{
    public class ModConfig
    {
        public string OpenMenuKey { get; set; } = "K";
        public bool ShowBlockedMessage { get; set; } = true;

        /// <summary>Extra percentage added to zone gold cost per already-unlocked zone. 0 = no scaling.</summary>
        public int CostScalingPercent { get; set; } = 10;

        public MinecartConfig BeachMinecart { get; set; } = new();

        /// <summary>Secondary optional beach bypass. Lets hosts add a second travel sign between the beach and any other location, unlocked once the Beach zone is purchased.</summary>
        public BypassWarpConfig SecondaryBeachBypass { get; set; } = new();

        public List<ZoneDefinition> Zones { get; set; } = new()
        {
            new ZoneDefinition
            {
                ZoneId = "Forest", DisplayName = "Cindersap Forest", BundleName = "Forest Bundle",
                Description = "Marnie's ranch, Leah's cottage, the Wizard's tower, and the Secret Woods.",
                UnlockType = "permanent", MoneyCost = 5000, Items = new(),
                LocationNames = new() { "Forest", "AnimalShop", "WizardHouse", "WizardHouseBasement", "LeahHouse" },
                Plate = new PlateTile { LocationName = "Farm", X = 40, Y = 64 }
            },
            new ZoneDefinition
            {
                ZoneId = "BusStop", DisplayName = "Bus Stop", BundleName = "Road Bundle",
                Description = "The road east of the farm. Gateway to the town and desert.",
                UnlockType = "permanent", MoneyCost = 2000, Items = new(),
                LocationNames = new() { "BusStop" },
                Plate = new PlateTile { LocationName = "Farm", X = 79, Y = 13 }
            },
            new ZoneDefinition
            {
                ZoneId = "Town", DisplayName = "Pelican Town", BundleName = "Town Access Pass",
                Description = "The heart of the valley. Shops, villagers, and the Community Center.",
                UnlockType = "ticket", MoneyCost = 5000, Items = new(),
                LocationNames = new() { "Town", "SeedShop", "Saloon", "Hospital", "HarveyRoom", "ManorHouse", "ArchaeologyHouse", "JojaMart", "Trailer", "Trailer_Big", "HaleyHouse", "SamHouse", "Blacksmith", "JoshHouse", "CommunityCenter", "MovieTheater", "AbandonedJojaMart", "Sewer" },
                Plate = new PlateTile { LocationName = "BusStop", X = 23, Y = 27 }
            },
            new ZoneDefinition
            {
                ZoneId = "Beach", DisplayName = "The Beach", BundleName = "Ocean Bundle",
                Description = "Sandy shores, Willy's fish shop, and Elliott's cabin.",
                UnlockType = "permanent", MoneyCost = 7500, Items = new(),
                LocationNames = new() { "Beach", "FishShop", "ElliottHouse" },
                Plate = new PlateTile { LocationName = "Mountain", X = 124, Y = 12 }
            },
            new ZoneDefinition
            {
                ZoneId = "Mountain", DisplayName = "The Mountain", BundleName = "Mountain Bundle",
                Description = "Robin's shop, the Adventurer's Guild, the quarry, and the lake.",
                UnlockType = "permanent", MoneyCost = 10000,
                Items = new() { new ItemCost { ItemId = "(O)388", DisplayName = "Wood", Count = 100 }, new ItemCost { ItemId = "(O)390", DisplayName = "Stone", Count = 100 } },
                LocationNames = new() { "Mountain", "ScienceHouse", "SebastianRoom", "AdventureGuild", "Quarry", "Tent" },
                Plate = new PlateTile { LocationName = "Backwoods", X = 25, Y = 0 }
            },
            new ZoneDefinition
            {
                ZoneId = "Mine", DisplayName = "The Mines", BundleName = "Spelunker's Bundle",
                Description = "All 120 floors of the mines. Requires Mountain unlocked and collective Mining skill.",
                UnlockType = "permanent", MoneyCost = 15000,
                Items = new() { new ItemCost { ItemId = "(O)334", DisplayName = "Copper Bar", Count = 5 } },
                LocationNames = new() { "Mine" }, LocationPrefixes = new() { "UndergroundMine" },
                RequiresZone = "Mountain",
                RequiredSkill = "Mining", RequiredSkillLevel = 5,
                Plate = new PlateTile { LocationName = "Mountain", X = 54, Y = 5 }
            },
            new ZoneDefinition
            {
                ZoneId = "Railroad", DisplayName = "Railroad & Spa", BundleName = "Railroad Bundle",
                Description = "The railroad, bathhouse, and Witch's hut area.",
                UnlockType = "permanent", MoneyCost = 12000, Items = new(),
                LocationNames = new() { "Railroad", "BathHouse_Entry", "BathHouse_Pool", "BathHouse_MensLocker", "BathHouse_WomensLocker", "WitchHut", "WitchSwamp", "WitchWarpCave" },
                RequiresZone = "Mountain",
                Plate = new PlateTile { LocationName = "Mountain", X = 2, Y = 2 }
            },
            new ZoneDefinition
            {
                ZoneId = "Desert", DisplayName = "Calico Desert", BundleName = "Desert Bundle",
                Description = "The desert, Sandy's shop, and the Skull Cavern.",
                UnlockType = "permanent", MoneyCost = 25000,
                Items = new() { new ItemCost { ItemId = "(O)337", DisplayName = "Iridium Bar", Count = 5 } },
                LocationNames = new() { "Desert", "SandyHouse", "Club" }, LocationPrefixes = new() { "SkullCave" },
                RequiresZone = "BusStop",
                Plate = new PlateTile { LocationName = "BusStop", X = 0, Y = 22 }
            },
            new ZoneDefinition
            {
                ZoneId = "Island", DisplayName = "Ginger Island", BundleName = "Island Bundle",
                Description = "The tropical island. Volcano dungeon, farm, and resort.",
                UnlockType = "permanent", MoneyCost = 50000,
                Items = new() { new ItemCost { ItemId = "(O)337", DisplayName = "Iridium Bar", Count = 10 }, new ItemCost { ItemId = "(O)787", DisplayName = "Battery Pack", Count = 5 } },
                LocationNames = new() { "IslandSouth", "IslandNorth", "IslandWest", "IslandEast", "IslandFarmHouse", "IslandShrine", "IslandSouthEast", "IslandSouthEastCave", "IslandFieldOffice", "IslandFarmCave", "CaptainRoom", "IslandHut", "QiNutRoom", "Caldera" },
                LocationPrefixes = new() { "VolcanoDungeon" }, RequiresZone = "Beach",
                Plate = new PlateTile { LocationName = "Beach", X = 20, Y = 35 }
            },
            new ZoneDefinition
            {
                ZoneId = "Backwoods", DisplayName = "Backwoods", BundleName = "Backwoods Bundle",
                Description = "The path north of the farm leading to the mountain.",
                UnlockType = "permanent", MoneyCost = 1000, Items = new(),
                LocationNames = new() { "Backwoods" },
                Plate = new PlateTile { LocationName = "Farm", X = 40, Y = 0 }
            }
        };
    }

    public class ZoneDefinition
    {
        public string ZoneId { get; set; }
        public string DisplayName { get; set; }
        public string BundleName { get; set; } = null;
        public string Description { get; set; }
        public string UnlockType { get; set; } = "permanent";
        public int MoneyCost { get; set; }
        public List<ItemCost> Items { get; set; } = new();
        public List<string> LocationNames { get; set; } = new();
        public List<string> LocationPrefixes { get; set; } = new();
        public string RequiresZone { get; set; } = null;
        public PlateTile Plate { get; set; } = null;

        /// <summary>Optional skill name required (e.g. "Mining", "Farming", "Fishing", "Foraging", "Combat"). Collective level across all players is checked.</summary>
        public string RequiredSkill { get; set; } = null;

        /// <summary>The collective skill level required (sum of all players' levels for the given skill).</summary>
        public int RequiredSkillLevel { get; set; } = 0;
    }

    public class ItemCost
    {
        public string ItemId { get; set; }
        public string DisplayName { get; set; }
        public int Count { get; set; }
    }

    public class PlateTile
    {
        public string LocationName { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
    }

    public class ZoneConfigOverride
    {
        public int? MoneyCost { get; set; }
        public List<ItemCost> Items { get; set; }
        public List<ItemCost> Rewards { get; set; }
    }

    public class MinecartConfig
    {
        public bool Enabled { get; set; } = true;
        public string MountainLocation { get; set; } = "Mountain";
        public int MountainSignX { get; set; } = 126;
        public int MountainSignY { get; set; } = 12;
        public int BeachArrivalX { get; set; } = 26;
        public int BeachArrivalY { get; set; } = 4;
        public string BeachLocation { get; set; } = "Beach";
        public int BeachSignX { get; set; } = 27;
        public int BeachSignY { get; set; } = 4;
        public int MountainArrivalX { get; set; } = 125;
        public int MountainArrivalY { get; set; } = 12;
    }

    /// <summary>Generic two-sign bypass warp, unlocked once the Beach zone is purchased.</summary>
    public class BypassWarpConfig
    {
        public bool Enabled { get; set; } = false;
        public string BeachLocation { get; set; } = "Beach";
        public int BeachSignX { get; set; } = 4;
        public int BeachSignY { get; set; } = 4;
        public int BeachArrivalX { get; set; } = 4;
        public int BeachArrivalY { get; set; } = 4;
        public string OtherLocation { get; set; } = "Backwoods";
        public int OtherSignX { get; set; } = 10;
        public int OtherSignY { get; set; } = 10;
        public int OtherArrivalX { get; set; } = 10;
        public int OtherArrivalY { get; set; } = 10;
    }
}
