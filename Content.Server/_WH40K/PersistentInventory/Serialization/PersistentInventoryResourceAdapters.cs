using System.Globalization;
using System.Linq;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WH40K.PersistentInventory.Serialization;

public sealed class CartridgeItemStateAdapter(IEntityManager entities, GunSystem guns) : IItemStateAdapter
{
    public string ComponentId => "CartridgeAmmo";
    public bool Handles(IComponent component) => component is CartridgeAmmoComponent;

    public bool TryCapture(EntityUid uid, IComponent component, out PersistentInventoryComponentState state, out string? error)
    {
        state = StackItemStateAdapter.State(ComponentId, ("spent", ((CartridgeAmmoComponent) component).Spent ? "1" : "0"));
        error = null;
        return true;
    }

    public bool TryRestore(EntityUid uid, PersistentInventoryComponentState state, out string? error)
    {
        error = "Invalid spent cartridge state.";
        if (state.Fields.Count != 1 || !state.Fields.TryGetValue("spent", out var spent) ||
            spent is not ("0" or "1") || !entities.TryGetComponent(uid, out CartridgeAmmoComponent? cartridge))
            return false;
        guns.SetPersistentCartridgeSpent(uid, cartridge, spent == "1");
        error = null;
        return true;
    }
}

public sealed class RevolverItemStateAdapter(IEntityManager entities, GunSystem guns) : IItemStateAdapter
{
    public string ComponentId => "RevolverAmmoProvider";
    public bool RestoreAfterChildren => true;
    public bool Handles(IComponent component) => component is RevolverAmmoProviderComponent;

    public bool TryCapture(EntityUid uid, IComponent component, out PersistentInventoryComponentState state, out string? error)
    {
        var ammo = (RevolverAmmoProviderComponent) component;
        state = StackItemStateAdapter.State(ComponentId);
        error = "Invalid revolver chamber layout.";
        if (ammo.Capacity <= 0 || ammo.Capacity > 256 || ammo.CurrentIndex < 0 || ammo.CurrentIndex >= ammo.Capacity ||
            ammo.AmmoSlots.Count != ammo.Capacity || ammo.Chambers.Length != ammo.Capacity)
            return false;
        var contents = ammo.AmmoContainer.ContainedEntities;
        var indices = new int[ammo.Capacity];
        var used = new HashSet<int>();
        for (var i = 0; i < ammo.Capacity; i++)
        {
            indices[i] = -1;
            if (ammo.AmmoSlots[i] is not { } item)
                continue;
            for (var j = 0; j < contents.Count; j++)
                if (contents[j] == item)
                    indices[i] = j;
            if (indices[i] < 0 || !used.Add(indices[i]))
                return false;
        }
        if (used.Count != contents.Count)
            return false;
        state = CreateState(ammo.CurrentIndex, ammo.Chambers, indices);
        error = null;
        return true;
    }

    internal static PersistentInventoryComponentState CreateState(int current, bool?[] chambers, int[] indices)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["capacity"] = chambers.Length.ToString(CultureInfo.InvariantCulture),
            ["current"] = current.ToString(CultureInfo.InvariantCulture),
        };
        for (var i = 0; i < chambers.Length; i++)
        {
            fields[$"{i}.chamber"] = chambers[i] switch { true => "live", false => "spent", null => "empty" };
            fields[$"{i}.entity"] = indices[i].ToString(CultureInfo.InvariantCulture);
        }
        return new PersistentInventoryComponentState("RevolverAmmoProvider", fields);
    }

    public bool TryRestore(EntityUid uid, PersistentInventoryComponentState state, out string? error)
    {
        error = "Invalid saved revolver chamber layout.";
        if (!entities.TryGetComponent(uid, out RevolverAmmoProviderComponent? ammo) ||
            !ReadInt(state, "capacity", out var count) || count != ammo.Capacity || count is <= 0 or > 256 ||
            !ReadInt(state, "current", out var current) || state.Fields.Count != 2 + count * 2)
            return false;
        var chambers = new bool?[count];
        var indices = new int[count];
        for (var i = 0; i < count; i++)
        {
            if (!ReadInt(state, $"{i}.entity", out indices[i]) ||
                !state.Fields.TryGetValue($"{i}.chamber", out var chamber) || chamber is not ("live" or "spent" or "empty"))
                return false;
            chambers[i] = chamber switch { "live" => true, "spent" => false, _ => null };
        }
        if (!guns.TrySetPersistentRevolverState(uid, ammo, current, chambers, indices))
            return false;
        error = null;
        return true;
    }

    private static bool ReadInt(PersistentInventoryComponentState state, string key, out int value)
    {
        value = 0;
        return state.Fields.TryGetValue(key, out var text) &&
               int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}

public sealed class GasTankItemStateAdapter(IEntityManager entities) : IItemStateAdapter
{
    public string ComponentId => "GasTank";
    public bool Handles(IComponent component) => component is GasTankComponent;

    public bool TryCapture(EntityUid uid, IComponent component, out PersistentInventoryComponentState state, out string? error)
    {
        var tank = (GasTankComponent) component;
        state = CreateState(tank);
        error = Validate(tank) ? null : "Invalid gas tank contents or pressure.";
        return error == null;
    }

    internal static PersistentInventoryComponentState CreateState(GasTankComponent tank, bool empty = false)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["volume"] = Bits(tank.Air.Volume),
            ["temperature"] = Bits(tank.Air.Temperature),
            ["output"] = Bits(tank.OutputPressure),
            ["integrity"] = tank.Integrity.ToString(CultureInfo.InvariantCulture),
            ["valve"] = !empty && tank.IsValveOpen ? "1" : "0",
        };
        for (var i = 0; i < Atmospherics.AdjustedNumberOfGases; i++)
            fields[$"gas.{i}"] = Bits(empty ? 0f : tank.Air.GetMoles(i));
        return new PersistentInventoryComponentState("GasTank", fields);
    }

    public bool TryRestore(EntityUid uid, PersistentInventoryComponentState state, out string? error)
    {
        error = "Invalid saved gas tank contents or pressure.";
        if (!entities.TryGetComponent(uid, out GasTankComponent? tank) ||
            state.Fields.Count != 5 + Atmospherics.AdjustedNumberOfGases ||
            !ReadFloat(state, "volume", out var volume) || volume <= 0 ||
            !ReadFloat(state, "temperature", out var temperature) || temperature < Atmospherics.TCMB || temperature > Atmospherics.Tmax ||
            !ReadFloat(state, "output", out var output) || output < 0 || output > tank.MaxOutputPressure ||
            !state.Fields.TryGetValue("integrity", out var text) ||
            !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integrity) || integrity < 0 ||
            !state.Fields.TryGetValue("valve", out var valve) || valve is not ("0" or "1"))
            return false;

        var air = new GasMixture(volume) { Temperature = temperature };
        for (var i = 0; i < Atmospherics.AdjustedNumberOfGases; i++)
        {
            if (!ReadFloat(state, $"gas.{i}", out var moles) || moles < 0)
                return false;
            air.SetMoles(i, moles);
        }
        if (!float.IsFinite(air.Pressure))
            return false;
        tank.Air = air;
        tank.OutputPressure = output;
        tank.Integrity = integrity;
        tank.IsValveOpen = valve == "1";
        entities.Dirty(uid, tank);
        error = null;
        return true;
    }

    private static bool Validate(GasTankComponent tank)
    {
        return float.IsFinite(tank.Air.Volume) && tank.Air.Volume > 0 &&
               float.IsFinite(tank.Air.Temperature) && tank.Air.Temperature >= Atmospherics.TCMB && tank.Air.Temperature <= Atmospherics.Tmax &&
               float.IsFinite(tank.OutputPressure) && tank.OutputPressure >= 0 && tank.OutputPressure <= tank.MaxOutputPressure &&
               tank.Integrity >= 0 && float.IsFinite(tank.Air.Pressure) &&
               Enumerable.Range(0, Atmospherics.AdjustedNumberOfGases).All(i =>
                   float.IsFinite(tank.Air.GetMoles(i)) && tank.Air.GetMoles(i) >= 0);
    }

    private static string Bits(float value) => BitConverter.SingleToInt32Bits(value).ToString(CultureInfo.InvariantCulture);

    private static bool ReadFloat(PersistentInventoryComponentState state, string key, out float value)
    {
        value = 0;
        if (!state.Fields.TryGetValue(key, out var text) ||
            !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bits))
            return false;
        value = BitConverter.Int32BitsToSingle(bits);
        return float.IsFinite(value);
    }
}

public sealed partial class PersistentInventorySnapshotSerializer
{
    private PersistentInventoryPayload UpgradeLegacyResources(PersistentInventoryPayload payload, List<string> actions)
    {
        var entities = payload.Entities.ToDictionary(entity => entity.EntityId);
        var upgraded = new List<PersistentInventoryEntityState>();
        foreach (var entity in payload.Entities)
        {
            if (!_prototypes.TryIndex<EntityPrototype>(entity.PrototypeId, out var prototype))
            {
                upgraded.Add(entity);
                continue;
            }
            var components = entity.Components.ToList();
            foreach (var id in new[] { "CartridgeAmmo", "GasTank", "RevolverAmmoProvider" })
            {
                if (!prototype.Components.TryGetValue(id, out var registration) || components.Any(component => component.ComponentId == id))
                    continue;
                PersistentInventoryComponentState state;
                switch (registration.Component)
                {
                    case CartridgeAmmoComponent:
                        state = StackItemStateAdapter.State(id, ("spent", "1"));
                        break;
                    case GasTankComponent tank:
                        state = GasTankItemStateAdapter.CreateState(tank, empty: true);
                        break;
                    case RevolverAmmoProviderComponent ammo:
                        if (ammo.Capacity is <= 0 or > 256)
                            throw new InvalidOperationException("Invalid legacy revolver capacity.");
                        var chambers = new bool?[ammo.Capacity];
                        var indices = Enumerable.Repeat(-1, ammo.Capacity).ToArray();
                        foreach (var child in entity.Children.Where(child => child.ContainerId == "revolver-ammo"))
                        {
                            if (child.Index < 0 || child.Index >= ammo.Capacity || !entities.TryGetValue(child.EntityId, out var childState))
                                throw new InvalidOperationException("Invalid legacy revolver contents.");
                            if (!_prototypes.TryIndex<EntityPrototype>(childState.PrototypeId, out var childPrototype))
                                throw new InvalidOperationException($"Legacy ammunition prototype {childState.PrototypeId} is unavailable.");
                            var cartridge = childState.Components.SingleOrDefault(component => component.ComponentId == "CartridgeAmmo");
                            chambers[child.Index] = childPrototype.Components.ContainsKey("CartridgeAmmo")
                                ? cartridge?.Fields.GetValueOrDefault("spent") == "0"
                                : true;
                            indices[child.Index] = child.Index;
                        }
                        state = RevolverItemStateAdapter.CreateState(0, chambers, indices);
                        break;
                    default:
                        continue;
                }
                components.Add(state);
                actions.Add($"legacy-resource-state:{entity.EntityId}:{id}:unknown-supply-cleared");
            }
            upgraded.Add(entity with { Components = components });
        }
        return payload with { Entities = upgraded };
    }
}
