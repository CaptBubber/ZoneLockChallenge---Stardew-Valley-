using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewValley;

namespace ZoneLockChallenge
{
    public class ZoneSaveData
    {
        public HashSet<string> UnlockedZones { get; set; } = new();
        /// <summary>Per-player tickets: zoneId -> (farmerId -> purchaseDay).</summary>
        public Dictionary<string, Dictionary<long, int>> ActiveTickets { get; set; } = new();
        /// <summary>Host-managed plate position overrides (synced to all players).</summary>
        public Dictionary<string, PlateTile> PlateOverrides { get; set; } = new();
        /// <summary>Host-managed zone config overrides (cost, items, rewards). Synced to all players.</summary>
        public Dictionary<string, ZoneConfigOverride> ZoneOverrides { get; set; } = new();
        /// <summary>Host-managed zone display order (list of zoneIds). Empty = use config order.</summary>
        public List<string> ZoneOrder { get; set; } = new();
        /// <summary>Host-managed mine level gate overrides. Null = use config defaults.</summary>
        public List<MineLevelGate> MineLevelGateOverrides { get; set; }
    }

    public class ZoneSyncMessage
    {
        public HashSet<string> UnlockedZones { get; set; } = new();
        public Dictionary<string, Dictionary<long, int>> ActiveTickets { get; set; } = new();
        public Dictionary<string, PlateTile> PlateOverrides { get; set; } = new();
        public Dictionary<string, ZoneConfigOverride> ZoneOverrides { get; set; } = new();
        public List<string> ZoneOrder { get; set; } = new();
        public List<MineLevelGate> MineLevelGateOverrides { get; set; }
    }

    public class ZonePurchaseRequest
    {
        public string ZoneId { get; set; }
        public long FarmerId { get; set; }
    }

    public class ZonePurchaseResponse
    {
        public string ZoneId { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
    }

    public class ZoneStateManager
    {
        private const string SaveDataKey = "ZoneLockChallenge_State";
        private const string SyncMessageType = "ZoneLockChallenge_Sync";
        private const string PurchaseRequestType = "ZoneLockChallenge_PurchaseReq";
        private const string PurchaseResponseType = "ZoneLockChallenge_PurchaseResp";

        private readonly IModHelper helper;
        private readonly IMonitor monitor;
        private readonly ModConfig config;

        public ZoneSaveData State { get; private set; } = new();

        /// <summary>Callback invoked after any purchase completes (host or farmhand). Used to refresh plates.</summary>
        public Action OnStateChanged;
        public Action<ZonePurchaseResponse> OnPurchaseResponse;

        public ZoneStateManager(IModHelper helper, IMonitor monitor, ModConfig config)
        {
            this.helper = helper;
            this.monitor = monitor;
            this.config = config;
            helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
        }

        public void LoadState()
        {
            if (Context.IsMainPlayer)
            {
                State = helper.Data.ReadSaveData<ZoneSaveData>(SaveDataKey) ?? new ZoneSaveData();
                monitor.Log($"Loaded zone state: {State.UnlockedZones.Count} unlocked, {State.ActiveTickets.Count} active tickets.", LogLevel.Info);
                BroadcastState();
            }
        }

        public void SaveState()
        {
            if (Context.IsMainPlayer)
                helper.Data.WriteSaveData(SaveDataKey, State);
        }

        public bool IsZoneAccessible(string zoneId, long farmerId)
        {
            if (State.UnlockedZones.Contains(zoneId))
                return true;
            if (State.ActiveTickets.TryGetValue(zoneId, out var playerTickets)
                && playerTickets.TryGetValue(farmerId, out int ticketDay))
                return ticketDay == Game1.Date.TotalDays;
            return false;
        }

        public bool IsZonePermanentlyUnlocked(string zoneId) => State.UnlockedZones.Contains(zoneId);

        /// <summary>Get the effective plate position for a zone (override from save data, or config default).</summary>
        public PlateTile GetEffectivePlate(ZoneDefinition zone)
        {
            if (State.PlateOverrides.TryGetValue(zone.ZoneId, out var overridePlate))
                return overridePlate;
            return zone.Plate;
        }

        /// <summary>Set a plate override (host only). Saves state and broadcasts to all players.</summary>
        public void SetPlateOverride(string zoneId, PlateTile plate)
        {
            State.PlateOverrides[zoneId] = plate;
            if (Context.IsMainPlayer)
            {
                SaveState();
                BroadcastState();
            }
        }

        public bool HasActiveTicket(string zoneId, long farmerId) =>
            State.ActiveTickets.TryGetValue(zoneId, out var playerTickets)
            && playerTickets.TryGetValue(farmerId, out int ticketDay)
            && ticketDay == Game1.Date.TotalDays;

        public ZoneDefinition GetZoneForLocation(string locationName)
        {
            if (string.IsNullOrEmpty(locationName)) return null;
            foreach (var zone in config.Zones)
            {
                if (zone.LocationNames.Contains(locationName))
                    return zone;
                if (zone.LocationPrefixes != null)
                    foreach (var prefix in zone.LocationPrefixes)
                        if (locationName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            return zone;
            }
            return null;
        }

        public bool ArePrerequisitesMet(ZoneDefinition zone)
        {
            if (!string.IsNullOrEmpty(zone.RequiresZone) && !State.UnlockedZones.Contains(zone.RequiresZone))
                return false;
            if (!string.IsNullOrEmpty(zone.RequiredSkill) && zone.RequiredSkillLevel > 0)
            {
                int collectiveLevel = GetCollectiveSkillLevel(zone.RequiredSkill);
                if (collectiveLevel < zone.RequiredSkillLevel)
                    return false;
            }
            return true;
        }

        /// <summary>Get the sum of a given skill level across all farmers (collective skill gate).</summary>
        public int GetCollectiveSkillLevel(string skillName)
        {
            int total = 0;
            foreach (var farmer in Game1.getAllFarmers())
            {
                total += skillName switch
                {
                    "Farming" => farmer.FarmingLevel,
                    "Mining" => farmer.MiningLevel,
                    "Fishing" => farmer.FishingLevel,
                    "Foraging" => farmer.ForagingLevel,
                    "Combat" => farmer.CombatLevel,
                    _ => 0
                };
            }
            return total;
        }

        /// <summary>Get the effective base gold cost for a zone (override or config default).</summary>
        public int GetEffectiveBaseCost(ZoneDefinition zone)
        {
            if (State.ZoneOverrides.TryGetValue(zone.ZoneId, out var ov) && ov.MoneyCost.HasValue)
                return ov.MoneyCost.Value;
            return zone.MoneyCost;
        }

        /// <summary>Get the effective item requirements for a zone (override or config default).</summary>
        public List<ItemCost> GetEffectiveItems(ZoneDefinition zone)
        {
            if (State.ZoneOverrides.TryGetValue(zone.ZoneId, out var ov) && ov.Items != null)
                return ov.Items;
            return zone.Items;
        }

        /// <summary>Get the rewards for a zone (from override data; empty if none configured).</summary>
        public List<ItemCost> GetRewards(ZoneDefinition zone)
        {
            if (State.ZoneOverrides.TryGetValue(zone.ZoneId, out var ov) && ov.Rewards != null)
                return ov.Rewards;
            return new List<ItemCost>();
        }

        /// <summary>Set a zone config override (host only). Saves state and broadcasts to all players.</summary>
        public void SetZoneOverride(string zoneId, ZoneConfigOverride zoneOverride)
        {
            State.ZoneOverrides[zoneId] = zoneOverride;
            if (Context.IsMainPlayer)
            {
                SaveState();
                BroadcastState();
            }
        }

        /// <summary>Get zones in the effective display order (save-data ZoneOrder first, then any config zones not yet in the order).</summary>
        public List<ZoneDefinition> GetOrderedZones()
        {
            var result = new List<ZoneDefinition>();
            var seen = new HashSet<string>();
            foreach (var zoneId in State.ZoneOrder)
            {
                var zone = config.Zones.FirstOrDefault(z => z.ZoneId == zoneId);
                if (zone != null && seen.Add(zoneId))
                    result.Add(zone);
            }
            foreach (var zone in config.Zones)
                if (seen.Add(zone.ZoneId))
                    result.Add(zone);
            return result;
        }

        /// <summary>Move a zone up (-1) or down (+1) in the display order. Host only.</summary>
        public bool MoveZoneInOrder(string zoneId, int delta)
        {
            if (!Context.IsMainPlayer || delta == 0) return false;
            var ordered = GetOrderedZones().Select(z => z.ZoneId).ToList();
            int idx = ordered.IndexOf(zoneId);
            if (idx < 0) return false;
            int newIdx = idx + delta;
            if (newIdx < 0 || newIdx >= ordered.Count) return false;
            ordered.RemoveAt(idx);
            ordered.Insert(newIdx, zoneId);
            State.ZoneOrder = ordered;
            SaveState();
            BroadcastState();
            return true;
        }

        /// <summary>Get the gold cost for a zone, scaled by number of already-unlocked zones.</summary>
        public int GetScaledMoneyCost(ZoneDefinition zone)
        {
            int baseCost = GetEffectiveBaseCost(zone);
            if (config.CostScalingPercent <= 0)
                return baseCost;
            int unlockedCount = State.UnlockedZones.Count;
            double multiplier = 1.0 + (config.CostScalingPercent / 100.0) * unlockedCount;
            return (int)(baseCost * multiplier);
        }

        // ── Mine level gates ─────────────────────────────────────────

        /// <summary>Get the effective mine level gates (overrides from save data, or config defaults).</summary>
        public List<MineLevelGate> GetEffectiveMineLevelGates()
        {
            return State.MineLevelGateOverrides ?? config.MineLevelGates ?? new List<MineLevelGate>();
        }

        /// <summary>Set mine level gate overrides (host only). Saves and broadcasts.</summary>
        public void SetMineLevelGateOverrides(List<MineLevelGate> gates)
        {
            State.MineLevelGateOverrides = gates?.Select(g => new MineLevelGate { FloorNumber = g.FloorNumber, RequiredMiningLevel = g.RequiredMiningLevel }).ToList();
            if (Context.IsMainPlayer)
            {
                SaveState();
                BroadcastState();
            }
        }

        /// <summary>Check if a specific mine floor is allowed based on collective mining level.</summary>
        public bool IsMineLevelAllowed(int floor)
        {
            var gates = GetEffectiveMineLevelGates();
            int collectiveMining = GetCollectiveSkillLevel("Mining");
            foreach (var gate in gates)
                if (floor >= gate.FloorNumber && collectiveMining < gate.RequiredMiningLevel)
                    return false;
            return true;
        }

        /// <summary>Get the required mining level for a specific floor (the highest gate at or below this floor). Returns 0 if no gate applies.</summary>
        public int GetRequiredMiningLevelForFloor(int floor)
        {
            var gates = GetEffectiveMineLevelGates();
            int required = 0;
            foreach (var gate in gates)
                if (floor >= gate.FloorNumber && gate.RequiredMiningLevel > required)
                    required = gate.RequiredMiningLevel;
            return required;
        }

        public bool TryPurchase(string zoneId, Farmer buyer)
        {
            if (Context.IsMainPlayer)
                return ExecutePurchase(zoneId, buyer);

            var request = new ZonePurchaseRequest { ZoneId = zoneId, FarmerId = buyer.UniqueMultiplayerID };
            helper.Multiplayer.SendMessage(request, PurchaseRequestType, modIDs: new[] { helper.ModRegistry.ModID });
            return false;
        }

        private bool ExecutePurchase(string zoneId, Farmer buyer)
        {
            var zone = config.Zones.FirstOrDefault(z => z.ZoneId == zoneId);
            if (zone == null) return false;
            if (!ArePrerequisitesMet(zone)) return false;
            if (zone.UnlockType == "permanent" && State.UnlockedZones.Contains(zoneId)) return false;

            var effectiveItems = GetEffectiveItems(zone);
            int scaledCost = GetScaledMoneyCost(zone);
            if (buyer.Money < scaledCost) return false;

            foreach (var itemCost in effectiveItems)
                if (CountItemInInventory(buyer, itemCost.ItemId) < itemCost.Count)
                    return false;

            // Only deduct money/items if buyer is the local player.
            // For remote farmhands, the host cannot modify their Money directly;
            // the farmhand will handle deduction locally when receiving the success response.
            bool isLocalBuyer = (buyer.UniqueMultiplayerID == Game1.player.UniqueMultiplayerID);
            if (isLocalBuyer)
            {
                buyer.Money -= scaledCost;
                foreach (var itemCost in effectiveItems)
                    RemoveItemsFromInventory(buyer, itemCost.ItemId, itemCost.Count);

                // Give rewards to local buyer
                GiveRewards(zone);
            }

            if (zone.UnlockType == "permanent")
            {
                State.UnlockedZones.Add(zoneId);
                monitor.Log($"Zone '{zoneId}' permanently unlocked by {buyer.Name}.", LogLevel.Info);
            }
            else if (zone.UnlockType == "ticket")
            {
                if (!State.ActiveTickets.ContainsKey(zoneId))
                    State.ActiveTickets[zoneId] = new Dictionary<long, int>();
                State.ActiveTickets[zoneId][buyer.UniqueMultiplayerID] = Game1.Date.TotalDays;
                monitor.Log($"Ticket for '{zoneId}' purchased by {buyer.Name} (ID {buyer.UniqueMultiplayerID}) for day {Game1.Date.TotalDays}.", LogLevel.Info);
            }

            BroadcastState();
            OnStateChanged?.Invoke();
            return true;
        }

        private int CountItemInInventory(Farmer farmer, string qualifiedItemId)
        {
            int count = 0;
            foreach (var item in farmer.Items)
                if (item != null && item.QualifiedItemId == qualifiedItemId)
                    count += item.Stack;
            return count;
        }

        /// <summary>Give reward items to the local player for completing a zone bundle.</summary>
        private void GiveRewards(ZoneDefinition zone)
        {
            var rewards = GetRewards(zone);
            foreach (var reward in rewards)
            {
                try
                {
                    var item = ItemRegistry.Create(reward.ItemId, reward.Count);
                    if (item != null)
                    {
                        if (!Game1.player.addItemToInventoryBool(item))
                            Game1.createItemDebris(item, Game1.player.getStandingPosition(), -1);
                    }
                }
                catch { monitor.Log($"Failed to create reward item '{reward.ItemId}'.", LogLevel.Warn); }
            }
        }

        private void RemoveItemsFromInventory(Farmer farmer, string qualifiedItemId, int amount)
        {
            int remaining = amount;
            for (int i = 0; i < farmer.Items.Count && remaining > 0; i++)
            {
                var item = farmer.Items[i];
                if (item != null && item.QualifiedItemId == qualifiedItemId)
                {
                    int take = Math.Min(item.Stack, remaining);
                    item.Stack -= take;
                    remaining -= take;
                    if (item.Stack <= 0)
                        farmer.Items[i] = null;
                }
            }
        }

        public void BroadcastState()
        {
            if (!Context.IsMainPlayer) return;
            var ticketsCopy = new Dictionary<string, Dictionary<long, int>>();
            foreach (var kv in State.ActiveTickets)
                ticketsCopy[kv.Key] = new Dictionary<long, int>(kv.Value);
            var plateOverridesCopy = new Dictionary<string, PlateTile>();
            foreach (var kv in State.PlateOverrides)
                plateOverridesCopy[kv.Key] = new PlateTile { LocationName = kv.Value.LocationName, X = kv.Value.X, Y = kv.Value.Y };
            var zoneOverridesCopy = new Dictionary<string, ZoneConfigOverride>();
            foreach (var kv in State.ZoneOverrides)
            {
                zoneOverridesCopy[kv.Key] = new ZoneConfigOverride
                {
                    MoneyCost = kv.Value.MoneyCost,
                    Items = kv.Value.Items?.Select(i => new ItemCost { ItemId = i.ItemId, DisplayName = i.DisplayName, Count = i.Count }).ToList(),
                    Rewards = kv.Value.Rewards?.Select(i => new ItemCost { ItemId = i.ItemId, DisplayName = i.DisplayName, Count = i.Count }).ToList()
                };
            }
            var mineGatesCopy = State.MineLevelGateOverrides?.Select(g => new MineLevelGate { FloorNumber = g.FloorNumber, RequiredMiningLevel = g.RequiredMiningLevel }).ToList();
            var message = new ZoneSyncMessage
            {
                UnlockedZones = new HashSet<string>(State.UnlockedZones),
                ActiveTickets = ticketsCopy,
                PlateOverrides = plateOverridesCopy,
                ZoneOverrides = zoneOverridesCopy,
                ZoneOrder = new List<string>(State.ZoneOrder),
                MineLevelGateOverrides = mineGatesCopy
            };
            helper.Multiplayer.SendMessage(message, SyncMessageType, modIDs: new[] { helper.ModRegistry.ModID });
        }

        public void RequestSync()
        {
            if (!Context.IsMainPlayer)
                helper.Multiplayer.SendMessage("ping", "ZoneLockChallenge_SyncRequest", modIDs: new[] { helper.ModRegistry.ModID });
        }

        private void OnModMessageReceived(object sender, StardewModdingAPI.Events.ModMessageReceivedEventArgs e)
        {
            if (e.FromModID != helper.ModRegistry.ModID) return;

            if (e.Type == PurchaseRequestType && Context.IsMainPlayer)
            {
                var request = e.ReadAs<ZonePurchaseRequest>();
                var buyer = Game1.getAllFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == request.FarmerId);
                if (buyer != null)
                {
                    bool success = ExecutePurchase(request.ZoneId, buyer);
                    var response = new ZonePurchaseResponse
                    {
                        ZoneId = request.ZoneId,
                        Success = success,
                        Message = success ? "Purchase successful!" : "Purchase failed. Check your funds and inventory."
                    };
                    helper.Multiplayer.SendMessage(response, PurchaseResponseType,
                        modIDs: new[] { helper.ModRegistry.ModID }, playerIDs: new[] { request.FarmerId });
                }
            }

            if (e.Type == SyncMessageType && !Context.IsMainPlayer)
            {
                var sync = e.ReadAs<ZoneSyncMessage>();
                State.UnlockedZones = sync.UnlockedZones;
                State.ActiveTickets = sync.ActiveTickets;
                State.PlateOverrides = sync.PlateOverrides ?? new Dictionary<string, PlateTile>();
                State.ZoneOverrides = sync.ZoneOverrides ?? new Dictionary<string, ZoneConfigOverride>();
                State.ZoneOrder = sync.ZoneOrder ?? new List<string>();
                State.MineLevelGateOverrides = sync.MineLevelGateOverrides;
                OnStateChanged?.Invoke();
            }

            if (e.Type == PurchaseResponseType && !Context.IsMainPlayer)
            {
                var response = e.ReadAs<ZonePurchaseResponse>();

                // Deduct money and items locally on the farmhand's side, and give rewards
                if (response.Success)
                {
                    var zone = config.Zones.FirstOrDefault(z => z.ZoneId == response.ZoneId);
                    if (zone != null)
                    {
                        int scaledCost = GetScaledMoneyCost(zone);
                        Game1.player.Money -= scaledCost;
                        var effectiveItems = GetEffectiveItems(zone);
                        foreach (var itemCost in effectiveItems)
                            RemoveItemsFromInventory(Game1.player, itemCost.ItemId, itemCost.Count);
                        GiveRewards(zone);
                    }
                }

                OnPurchaseResponse?.Invoke(response);
            }

            if (e.Type == "ZoneLockChallenge_SyncRequest" && Context.IsMainPlayer)
                BroadcastState();
        }

        /// <summary>Expire old tickets. Returns list of zone IDs where the local player's ticket expired (for HUD messages).</summary>
        public List<string> CleanupExpiredTickets()
        {
            if (!Context.IsMainPlayer)
                return new List<string>();

            int today = Game1.Date.TotalDays;
            long localId = Game1.player.UniqueMultiplayerID;
            var expiredForLocal = new List<string>();
            bool anyRemoved = false;

            foreach (var zoneId in State.ActiveTickets.Keys.ToList())
            {
                var playerTickets = State.ActiveTickets[zoneId];
                var expiredPlayers = playerTickets.Where(kv => kv.Value < today).Select(kv => kv.Key).ToList();

                foreach (var farmerId in expiredPlayers)
                {
                    if (farmerId == localId)
                        expiredForLocal.Add(zoneId);
                    playerTickets.Remove(farmerId);
                    anyRemoved = true;
                }

                // Remove zone entry entirely if no players have tickets left
                if (playerTickets.Count == 0)
                    State.ActiveTickets.Remove(zoneId);
            }

            if (anyRemoved)
                BroadcastState();

            return expiredForLocal;
        }
    }
}
