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
        public Dictionary<string, int> ActiveTickets { get; set; } = new();
    }

    public class ZoneSyncMessage
    {
        public HashSet<string> UnlockedZones { get; set; } = new();
        public Dictionary<string, int> ActiveTickets { get; set; } = new();
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

        public bool IsZoneAccessible(string zoneId)
        {
            if (State.UnlockedZones.Contains(zoneId))
                return true;
            if (State.ActiveTickets.TryGetValue(zoneId, out int ticketDay))
                return ticketDay == Game1.Date.TotalDays;
            return false;
        }

        public bool IsZonePermanentlyUnlocked(string zoneId) => State.UnlockedZones.Contains(zoneId);

        public bool HasActiveTicket(string zoneId) =>
            State.ActiveTickets.TryGetValue(zoneId, out int ticketDay) && ticketDay == Game1.Date.TotalDays;

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
            if (string.IsNullOrEmpty(zone.RequiresZone)) return true;
            return State.UnlockedZones.Contains(zone.RequiresZone);
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
            if (buyer.Money < zone.MoneyCost) return false;

            foreach (var itemCost in zone.Items)
                if (CountItemInInventory(buyer, itemCost.ItemId) < itemCost.Count)
                    return false;

            buyer.Money -= zone.MoneyCost;
            foreach (var itemCost in zone.Items)
                RemoveItemsFromInventory(buyer, itemCost.ItemId, itemCost.Count);

            if (zone.UnlockType == "permanent")
            {
                State.UnlockedZones.Add(zoneId);
                monitor.Log($"Zone '{zoneId}' permanently unlocked by {buyer.Name}.", LogLevel.Info);
            }
            else if (zone.UnlockType == "ticket")
            {
                State.ActiveTickets[zoneId] = Game1.Date.TotalDays;
                monitor.Log($"Ticket for '{zoneId}' purchased by {buyer.Name} for day {Game1.Date.TotalDays}.", LogLevel.Info);
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
            var message = new ZoneSyncMessage
            {
                UnlockedZones = new HashSet<string>(State.UnlockedZones),
                ActiveTickets = new Dictionary<string, int>(State.ActiveTickets)
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
                OnStateChanged?.Invoke();
            }

            if (e.Type == PurchaseResponseType && !Context.IsMainPlayer)
            {
                var response = e.ReadAs<ZonePurchaseResponse>();
                OnPurchaseResponse?.Invoke(response);
            }

            if (e.Type == "ZoneLockChallenge_SyncRequest" && Context.IsMainPlayer)
                BroadcastState();
        }

        /// <summary>Expire old tickets. Returns list of zone IDs whose tickets just expired (for HUD messages).</summary>
        public List<string> CleanupExpiredTickets()
        {
            if (!Context.IsMainPlayer)
                return new List<string>();

            int today = Game1.Date.TotalDays;
            var expired = State.ActiveTickets
                .Where(kv => kv.Value < today)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in expired)
                State.ActiveTickets.Remove(key);

            if (expired.Count > 0)
                BroadcastState();

            return expired;
        }
    }
}
