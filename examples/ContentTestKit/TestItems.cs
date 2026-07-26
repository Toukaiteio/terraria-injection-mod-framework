using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using TIMF.Content;

namespace ContentTestKit
{
    /// <summary>
    /// Rarity values as vanilla stores them: <c>Item.rare</c> is a plain int, and 1.4.5 has no
    /// ItemRarityID constants class to name them.
    /// </summary>
    internal static class Rare
    {
        public const int Blue = 1;
        public const int Green = 2;
        public const int Orange = 3;
        public const int LightRed = 4;
    }

    /// <summary>
    /// Melee weapon. Verifies: SetDefaults reaches the item, damage/knockback apply,
    /// swing animation works, and a real .png loads from disk.
    /// </summary>
    public sealed class TestSword : TimfItem
    {
        public override string DisplayName => "TIMF Test Sword";
        public override IReadOnlyList<string> Tooltip => new[] { "If you can swing this, SetDefaults works." };

        public override void SetDefaults()
        {
            Item.width = 32;
            Item.height = 32;
            Item.damage = 25;
            Item.melee = true;
            Item.knockBack = 5f;
            Item.useTime = 20;
            Item.useAnimation = 20;
            Item.useStyle = ItemUseStyleID.Swing;
            Item.autoReuse = true;
            Item.UseSound = SoundID.Item1;
            Item.value = Terraria.Item.sellPrice(0, 0, 50, 0);
            Item.rare = Rare.Green;
        }
    }

    /// <summary>
    /// Stackable material. Verifies: maxStack is honoured and stacking/splitting behaves,
    /// exercising the id through different vanilla code paths than a weapon does.
    /// </summary>
    public sealed class TestMaterial : TimfItem
    {
        public static int RegisteredType { get; private set; }
        public override string DisplayName => "TIMF Test Shard";
        public override IReadOnlyList<string> Tooltip => new[] { "Stack me to 99 to test stacking." };
        public override void SetStaticDefaults() { RegisteredType = Type; }

        public override void SetDefaults()
        {
            Item.width = 16;
            Item.height = 16;
            Item.maxStack = 99;
            Item.value = Terraria.Item.sellPrice(0, 0, 0, 25);
            Item.rare = Rare.Blue;
            Item.material = true;
        }
    }

    /// <summary>
    /// Healing consumable. Verifies: consumption, potion sickness and healLife plumbing all
    /// accept a modded id.
    /// </summary>
    public sealed class TestPotion : TimfItem
    {
        public override string DisplayName => "TIMF Test Potion";
        public override IReadOnlyList<string> Tooltip => new[] { "Heals 50. Tests consumable paths." };

        public override void SetDefaults()
        {
            Item.width = 16;
            Item.height = 24;
            Item.maxStack = 30;
            Item.consumable = true;
            Item.useTime = 17;
            Item.useAnimation = 17;
            Item.useStyle = ItemUseStyleID.DrinkLong;
            Item.UseSound = SoundID.Item3;
            Item.healLife = 50;
            Item.potion = true;
            Item.rare = Rare.Orange;
            Item.value = Terraria.Item.sellPrice(0, 0, 1, 0);
        }
    }

    /// <summary>
    /// Accessory. Verifies equip slots accept a modded id, and that it survives being written
    /// into <c>Player.armor</c> — one of the containers the save layer will have to walk.
    ///
    /// Deliberately ships NO .png: this one should render as a magenta square, proving the
    /// missing-texture fallback works instead of crashing the draw pass.
    /// </summary>
    public sealed class TestAccessory : TimfItem
    {
        public override string DisplayName => "TIMF Test Charm (no texture on purpose)";
        public override IReadOnlyList<string> Tooltip => new[]
        {
            "Equip me: +20 max life and permanent Shine.",
            "Magenta square is the missing-texture fallback, not a bug.",
        };

        public override void SetDefaults()
        {
            Item.width = 24;
            Item.height = 24;
            Item.accessory = true;
            Item.rare = Rare.LightRed;
            Item.value = Terraria.Item.sellPrice(0, 1, 0, 0);
        }

        /// <summary>
        /// Two deliberately obvious effects: a stat bump that shows in the life counter, and a
        /// buff that is visible on the character. Both re-apply every tick, which is how a
        /// vanilla accessory works — so if the hook stops firing the effect disappears at once
        /// rather than lingering and hiding the failure.
        /// </summary>
        public override void UpdateAccessory(Terraria.Player player, bool hideVisual)
        {
            player.statLifeMax2 += 20;
            player.AddBuff(Terraria.ID.BuffID.Shine, 2);
        }
    }

    /// <summary>
    /// Placeable item for the custom tile below. This deliberately exercises the complete
    /// item → custom tile → TextureAssets.Tile → world-sidecar path.
    /// </summary>
    public sealed class TestPlaceable : TimfItem
    {
        public static int RegisteredType { get; private set; }

        public override string DisplayName => "TIMF Custom Test Tile";
        public override IReadOnlyList<string> Tooltip => new[]
        {
            "Places ContentTestKit's own tile id (not a vanilla tile).",
            "Breaking the tile should drop this placeable item back.",
            "Place several, save, reload, and verify the .timf-tiles sidecar restores them.",
        };

        public override string Texture => "Content/TestMaterial";   // reuse the shard sprite

        public override void SetStaticDefaults()
        {
            RegisteredType = Type;
        }

        public override void SetDefaults()
        {
            Item.width = 16;
            Item.height = 16;
            Item.maxStack = 99;
            Item.useTime = 10;
            Item.useAnimation = 15;
            Item.useStyle = ItemUseStyleID.Swing;
            Item.useTurn = true;
            Item.autoReuse = true;
            Item.consumable = true;
            Item.createTile = TestTorchTile.RegisteredType;
            Item.rare = Rare.Blue;
            Item.value = Terraria.Item.sellPrice(0, 0, 0, 10);
        }
    }

    /// <summary>
    /// A deliberately simple 1x1 luminous test block. It reuses the mod's shard PNG so the
    /// visual still comes from ContentTestKit while keeping the sample asset set small.
    /// </summary>
    public sealed class TestTorchTile : TimfTile
    {
        public static int RegisteredType { get; private set; }

        public override string DisplayName => "TIMF Custom Test Tile";
        public override string Texture => "Content/TestMaterial";
        public override int ItemDrop => TestPlaceable.RegisteredType;

        public override void SetStaticDefaults()
        {
            RegisteredType = Type;
            Main.tileSolid[Type] = true;
            Main.tileSolidTop[Type] = false;
            Main.tileFrameImportant[Type] = false;
            Main.tileNoAttach[Type] = false;
            Main.tileLighted[Type] = true;
            Main.tileBlockLight[Type] = true;
        }
    }


    public sealed class TestTorchItem : TimfItem
    {
        public static int RegisteredType { get; private set; }
        public override string DisplayName => "TIMF Custom Torch";
        public override string Texture => "Content/TestTorchItem";
        public override IReadOnlyList<string> Tooltip => new[]
        {
            "Custom Tile ID + torch TileObjectData anchors.",
            "Test floor, left/right side, wall placement, light, save/reload, and self-drop.",
        };
        public override void SetStaticDefaults() { RegisteredType = Type; }
        public override void SetDefaults()
        {
            Item.width = 16; Item.height = 20; Item.maxStack = 99;
            Item.useTime = 10; Item.useAnimation = 15;
            Item.useStyle = ItemUseStyleID.Swing; Item.useTurn = true; Item.autoReuse = true;
            Item.consumable = true; Item.createTile = TestSpecialTorchTile.RegisteredType;
            Item.rare = Rare.Blue;
        }
    }

    public sealed class TestSpecialTorchTile : TimfTile
    {
        public static int RegisteredType { get; private set; }
        public override string DisplayName => "TIMF Custom Torch";
        public override string Texture => "Content/TestTorchTile";
        public override int ItemDrop => TestTorchItem.RegisteredType;
        public override int PlacementTemplateTile => TileID.Torches;
        public override void SetStaticDefaults()
        {
            RegisteredType = Type;
            Main.tileFrameImportant[Type] = true;
            Main.tileLighted[Type] = true;
            Main.tileNoAttach[Type] = true;
            Main.tileLavaDeath[Type] = true;
            TileID.Sets.Torches[Type] = true;
        }
        public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
        {
            r = 1f; g = 0.55f; b = 0.2f;
        }
    }

    public sealed class TestWallItem : TimfItem
    {
        public static int RegisteredType { get; private set; }
        public override string DisplayName => "TIMF Custom Test Wall";
        public override string Texture => "Content/TestWallItem";
        public override IReadOnlyList<string> Tooltip => new[]
        {
            "Places a real custom Wall ID, not a vanilla wall.",
            "Test placement, hammer drop, painting, and save/reload.",
        };
        public override void SetStaticDefaults() { RegisteredType = Type; }
        public override void SetDefaults()
        {
            Item.width = 16; Item.height = 16; Item.maxStack = 999;
            Item.useTime = 7; Item.useAnimation = 15;
            Item.useStyle = ItemUseStyleID.Swing; Item.useTurn = true; Item.autoReuse = true;
            Item.consumable = true; Item.createWall = TestWall.RegisteredType;
            Item.rare = Rare.Blue;
        }
    }

    public sealed class TestWall : TimfWall
    {
        public static int RegisteredType { get; private set; }
        public override string DisplayName => "TIMF Custom Test Wall";
        public override string Texture => "Content/TestWallTile";
        public override int ItemDrop => TestWallItem.RegisteredType;
        public override void SetStaticDefaults()
        {
            RegisteredType = Type;
            Main.wallHouse[Type] = true;
        }
    }

    /// <summary>A 2x1 furniture object used to verify multi-cell placement/framing/drop/save.</summary>
    public sealed class TestWorkbenchItem : TimfItem
    {
        public static int RegisteredType { get; private set; }
        public override string DisplayName => "TIMF Test Workbench";
        public override string Texture => "Content/TestWorkbenchItem";
        public override IReadOnlyList<string> Tooltip => new[]
        {
            "Custom 2x1 furniture using the vanilla workbench placement template.",
            "Place, use for crafting, break, save and reload; exactly one item should drop.",
        };
        public override void SetStaticDefaults() { RegisteredType = Type; }
        public override void AddRecipes()
        {
            // This recipe must not appear away from the custom workbench. It simultaneously
            // tests a mod ingredient, mod result and mod crafting-station tile id.
            TimfRecipe.Create(TestTorchItem.RegisteredType, 5)
                .AddIngredient(TestMaterial.RegisteredType, 1)
                .AddTile(TestWorkbenchTile.RegisteredType)
                .Register();
        }
        public override void SetDefaults()
        {
            Item.width = 28; Item.height = 16; Item.maxStack = 99;
            Item.useTime = 10; Item.useAnimation = 15;
            Item.useStyle = ItemUseStyleID.Swing; Item.useTurn = true; Item.autoReuse = true;
            Item.consumable = true; Item.createTile = TestWorkbenchTile.RegisteredType;
            Item.rare = Rare.Green;
        }
    }

    public sealed class TestWorkbenchTile : TimfTile
    {
        public static int RegisteredType { get; private set; }
        public override string DisplayName => "TIMF Test Workbench";
        public override string Texture => "Content/TestWorkbenchTile";
        public override int ItemDrop => TestWorkbenchItem.RegisteredType;
        public override int PlacementTemplateTile => TileID.WorkBenches;
        public override void SetStaticDefaults()
        {
            RegisteredType = Type;
            Main.tileFrameImportant[Type] = true;
            Main.tileSolidTop[Type] = true;
            Main.tileNoAttach[Type] = true;
            Main.tileTable[Type] = true;
            Main.tileLavaDeath[Type] = true;
            TileID.Sets.RoomNeeds.CountsAsTable[Type] = true;
        }
    }

    /// <summary>Placeable counterpart for the custom, world-owned 40-slot container.</summary>
    public sealed class TestChestItem : TimfItem
    {
        public static int RegisteredType { get; private set; }
        public override string DisplayName => "TIMF Reliable Test Chest";
        public override string Texture => "Content/TestChestItem";
        public override IReadOnlyList<string> Tooltip => new[]
        {
            "Custom 2x2 container persisted entirely in .timf-chests by content key.",
            "Put vanilla and modded items inside, rename it, save/reload, then verify all slots.",
            "A non-empty chest must refuse to break; an empty chest must drop exactly one item.",
        };
        public override void SetStaticDefaults() { RegisteredType = Type; }
        public override void SetDefaults()
        {
            Item.width = 30; Item.height = 24; Item.maxStack = 99;
            Item.useTime = 10; Item.useAnimation = 15;
            Item.useStyle = ItemUseStyleID.Swing; Item.useTurn = true; Item.autoReuse = true;
            Item.consumable = true; Item.createTile = TestChestTile.RegisteredType;
            Item.rare = Rare.Orange;
        }
    }

    public sealed class TestChestTile : TimfContainerTile
    {
        public static int RegisteredType { get; private set; }
        public override string DisplayName => "TIMF Reliable Test Chest";
        public override string Texture => "Content/TestChestTile";
        public override int ItemDrop => TestChestItem.RegisteredType;
        public override void SetStaticDefaults()
        {
            RegisteredType = Type;
            Main.tileFrameImportant[Type] = true;
            Main.tileNoAttach[Type] = true;
            Main.tileContainer[Type] = true;
            Main.tileLavaDeath[Type] = true;
            TileID.Sets.BasicChest[Type] = true;
            TileID.Sets.HasOutlines[Type] = true;
        }
    }
}
