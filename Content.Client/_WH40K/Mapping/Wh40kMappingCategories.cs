using Content.Shared.Decals;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Mapping;

internal static class Wh40kMappingCategories
{
    public static string Entity(EntityPrototype entity)
    {
        var components = entity.Components;
        var lineage = entity.Parents is { Length: > 0 } parents ? $"{entity.ID} {string.Join(' ', parents)}" : entity.ID;
        bool Has(params string[] names) => Array.Exists(names, components.ContainsKey);
        bool Named(params string[] names) => ContainsAny(lineage, names);

        if (Has("EntityTableSpawner", "RandomSpawner", "SpawnPoint") || Named("SpawnPoint", "RandomSpawner", "MappingMarker", "Debug", "Waypoint", "StationBeacon"))
            return "mapping";
        if (Has("Projectile", "Hitscan") || Named("Effect", "MuzzleFlash", "ImpactEffect", "GibVisual"))
            return "effects";
        if (Has("Vehicle", "ShuttleConsole", "VehicleKey") || Named("Vehicle", "ShuttleThruster", "WarpDrive", "Hoverbike", "CargoCarrier", "MechEquipment"))
            return "vehicles";
        if (Has("MobState", "Ghost", "HumanoidAppearance") || Named("BaseMob"))
            return "mobs";
        if (Has("BodyPart", "Organ", "Hypospray", "Healing", "MedicalItem", "Defibrillator") ||
            Named("Medipen", "Medkit", "Bandage", "Syringe", "FirstAid", "Pill", "Ointment", "BrutePack"))
            return "medical";
        if (Named("Anomaly", "Artifact", "Research", "Beaker", "ChemistryBottle", "ChemicalBarrel", "SolutionScanner", "Vial"))
            return "science";
        if (Has("Paper", "FaxMachine") || Named("Book", "Blueprint", "Paper", "Folder", "Clipboard"))
            return "documents";
        if (Named("Plushie", "Figurine", "BoardGame", "PlayingCard", "Dice", "Balloon", "Toy"))
            return "fun";
        if (Has("Seed", "PlantHolder", "Produce") || Named("Hydroponics", "SeedBase", "ProduceBase", "Seeds", "Fruit", "Flower"))
            return "botany";
        if (Has("Food", "Drink") || Named("FoodBase", "BaseFood", "Food", "DrinkBase", "Drink", "Cake", "Burger"))
            return "food";
        if (Has("GasTank", "PipeNetDevice", "AtmosDevice") || Named("GasCanister", "GasPipe", "GasVent", "GasFilter", "GasMixer", "AirAlarm", "Scrubber", "Atmospheric"))
            return "atmospherics";
        if (Has("Cable", "PowerCell", "Generator", "PowerSupplier") && !Has("Gun") ||
            Named("PowerCell", "Cable", "Substation", "SMES", "Generator", "SolarPanel", "APC"))
            return "power";
        if (Has("Circuitboard", "MachineBoard", "Material") || Named("Electronics", "Circuitboard", "MatterBin", "Capacitor", "SteelSheet", "SheetSteel", "Ingot", "OreBase", "OreStack", "Scrap"))
            return "materials";
        if (Has("Tool", "Welder", "PryTool") && !Named("Weapon", "Axe", "Sword") || Named("Crowbar", "Wrench", "Wirecutter", "Screwdriver", "Multitool", "WeldingTool"))
            return "engineering";
        if (Has("PDA", "IdCard", "RadioMicrophone", "RadioSpeaker", "Instrument") || Named("Handheld", "Headset", "EncryptionKey", "Flashlight", "Device", "Tracker"))
            return "devices";
        if (Has("CartridgeAmmo", "BallisticAmmoProvider", "BasicEntityAmmoProvider", "HitscanBatteryAmmoProvider") && !Has("Gun") ||
            Named("Magazine", "AmmoBox", "SpeedLoader", "Cartridge", "AmmoCrate"))
            return "ammunition";
        if (Has("Gun", "Grenade") || Named("Weapon", "Grenade", "Dynamite", "Sword", "Machete", "Katana", "Spear"))
            return "weapons";
        if (Has("Clothing") && Named("Clothing", "Jumpsuit", "Armor", "Helmet", "Suit", "Uniform", "Gloves", "Shoes", "Belt", "Backpack", "Pouch"))
            return "clothing";
        if (Has("Door", "Airtight") || Named("Window", "Girder", "Airlock", "Wallmount", "Wall", "Railing", "Fence", "Barricade"))
            return "structures";
        if (Has("DoorBolt", "VendingMachine", "DisposalUnit", "Computer", "Machine") || Named("Computer", "Machine", "Fabricator", "Lathe", "Autolathe"))
            return "machines";
        if (Has("EntityStorage", "StackStorage") || Named("Locker", "Crate", "Shelf", "Container", "Closet", "Toolbox", "Rack"))
            return "storage";
        if (Has("PoweredLight") || Named("Chandelier", "StreetLamp", "Floodlight", "LightFixture", "Torch", "Candle"))
            return "lighting";
        if (Has("Strap") || Named("Chair", "Table", "Stool", "Sofa", "Bench", "Bed", "Bunk", "Desk"))
            return "furniture";
        if (Named("Sign", "Poster", "Statue", "Decoration", "Banner", "Potted", "Carpet", "Curtain", "Prop", "Scenery"))
            return "decor";
        if (components.TryGetValue("MeleeWeapon", out var melee) && melee.Component is MeleeWeaponComponent weapon && weapon.Damage?.GetTotal() >= 14)
            return "weapons";
        if (Has("Clothing") && !Has("Item"))
            return "clothing";
        return Has("Item") ? "items" : Has("Construction", "WallMount") ? "structures" : "other";
    }

    public static string Tile(ITileDefinition tile)
    {
        var id = tile.ID;
        var path = tile.Sprite?.ToString() ?? string.Empty;
        if (tile.TileId == 0 || ContainsAny(id, "Space", "Lattice", "Grille", "Catwalk") && !ContainsAny(id, "Plating"))
            return "space";
        if (ContainsAny(id, "Glass", "Transparent"))
            return "glass";
        if (ContainsAny(id, "Plating", "Foundation", "Subfloor"))
            return "plating";
        if (ContainsAny(id, "Snow", "Ice", "Permafrost", "Glacier"))
            return "snow";
        if (ContainsAny(id, "Water", "River", "Lava", "Swamp"))
            return "liquids";
        if (ContainsAny(id, "Grass", "Moss", "Jungle", "Astro") || ContainsAny(path, "/Grass/", "/Jungle/"))
            return "vegetation";
        if (ContainsAny(id, "Wood", "Carpet", "Marble", "Mosaic", "Parquet"))
            return "finish";
        if (ContainsAny(id, "Road", "Concrete", "Pavement", "Asphalt", "Cobble", "Sidewalk"))
            return "streets";
        if (ContainsAny(id, "Rusted", "Dirty", "Damaged", "Burnt", "Rust"))
            return "weathering";
        if (ContainsAny(id, "Techmaint", "Reinforced", "Grating", "Industrial", "Freezer", "Brig", "Hull"))
            return "industrial";
        if (ContainsAny(id, "FloorSteel", "FloorMetal", "TileFloor"))
            return "floors";
        if (ContainsAny(id, "Dirt", "Sand", "Rock", "Asteroid", "Planet") || ContainsAny(path, "/Planet/", "/Asteroid/"))
            return "terrain";
        return "floors";
    }

    public static string Decal(DecalPrototype decal)
    {
        var id = decal.ID;
        if (decal.Tags.Contains("dirty") || ContainsAny(id, "Dirt", "Rust", "Grime", "Scorch", "Blood", "Crack", "Moss"))
            return "weathering";
        if (decal.Tags.Contains("flora") || decal.Tags.Contains("rock") || ContainsAny(id, "Grass", "Plant", "Reed", "Lichen", "Pebble"))
            return "nature";
        if (ContainsAny(id, "Warn", "Hazard", "Caution", "Safety", "Striped"))
            return "safety";
        if (ContainsAny(id, "Road", "Crosswalk", "Asphalt", "Runway", "Landing"))
            return "streets";
        if (decal.Tags.Contains("crayon") || ContainsAny(id, "Graffiti", "Crayon", "PaintStroke"))
            return "graffiti";
        if (ContainsAny(id, "Marble", "BrickTile", "Mosaic", "Filigree", "Carpet", "Ornament", "Insignia"))
            return "decor";
        if (ContainsAny(id, "Stencil", "Alphabet", "Number", "Sign", "Label", "Letter") || id.Length == 1)
            return "stencils";
        if (ContainsAny(id, "Trim", "Line", "Border", "Edge", "Corner"))
            return "lines";
        if (decal.Tags.Contains("markings"))
            return "markings";
        return decal.Tags.Contains("overlay") ? "overlays" : "other";
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        foreach (var term in terms)
        {
            if (value.Contains(term, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
