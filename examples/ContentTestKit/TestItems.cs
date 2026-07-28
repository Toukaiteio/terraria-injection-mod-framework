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
        public static int RegisteredType { get; private set; }
        public override string DisplayName => "TIMF Test Potion";
        public override IReadOnlyList<string> Tooltip => new[] { "Heals 50. Tests consumable paths." };
        public override void SetStaticDefaults() { RegisteredType = Type; }

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

    /// <summary>Validates expanded ItemID.Sets environmental arrays.</summary>
    public sealed class TestAmbientItem : TimfItem
    {
        public override string DisplayName => "TIMF Dropped-Item Environment Probe";
        public override string Texture => "Content/TestMaterial";
        public override IReadOnlyList<string> Tooltip => new[]
        {
            "Not placeable: drop it from the inventory into the world.",
            "The dropped item should float in place and survive contact with lava.",
            "Tests ItemID.Sets.ItemNoGravity and IsLavaImmuneRegardlessOfRarity."
        };
        public override void SetStaticDefaults()
        {
            ItemID.Sets.ItemNoGravity[Type] = true;
            ItemID.Sets.IsLavaImmuneRegardlessOfRarity[Type] = true;
        }
        public override void SetDefaults()
        {
            Item.width = 16; Item.height = 16; Item.maxStack = 99; Item.rare = Rare.Blue;
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

    public sealed class TestDecorItem : TimfItem
    {
        public static int RegisteredType;
        public override string DisplayName => "TIMF Decorative Cave Rock";
        public override string Texture => "Content/TestMaterial";
        public override void SetStaticDefaults() { RegisteredType = Type; }
        public override void SetDefaults() { Item.width=16; Item.height=16; Item.maxStack=99; Item.useTime=10; Item.useAnimation=15; Item.useStyle=ItemUseStyleID.Swing; Item.consumable=true; Item.createTile=TestDecorTile.RegisteredType; }
    }
    public sealed class TestDecorTile : TimfTile
    {
        public static int RegisteredType;
        public override string Texture => "Content/TestMaterial";
        public override bool BreaksInstantly => true;
        public override void SetStaticDefaults() { RegisteredType=Type; Main.tileFrameImportant[Type]=false; Main.tileLavaDeath[Type]=true; }
    }

    public sealed class TestSwitchItem : TimfItem
    {
        public static int RegisteredType; public override string DisplayName => "TIMF Wired Crystal"; public override string Texture => "Content/TestTorchItem";
        public override void SetStaticDefaults(){RegisteredType=Type;}
        public override void SetDefaults(){Item.width=16;Item.height=20;Item.maxStack=99;Item.useTime=10;Item.useAnimation=15;Item.useStyle=ItemUseStyleID.Swing;Item.consumable=true;Item.createTile=TestSwitchTile.RegisteredType;}
    }
    public sealed class TestSwitchTile : TimfTile
    {
        public static int RegisteredType; public override string Texture=>"Content/TestTorchTile"; public override int ItemDrop=>TestSwitchItem.RegisteredType;
        public override bool PreserveFrameData=>true;
        public override void SetStaticDefaults(){RegisteredType=Type;Main.tileFrameImportant[Type]=false;Main.tileLighted[Type]=true;}
        public override bool RightClick(int i,int j,Player player){HitWire(i,j);return true;}
        public override void HitWire(int i,int j){var t=Main.tile[i,j];t.frameX=(short)(t.frameX==0?18:0);}
        public override void ModifyLight(int i,int j,ref float r,ref float g,ref float b){if(Main.tile[i,j].frameX>0){r=.2f;g=.8f;b=1f;}}
    }

    public sealed class TestConveyorItem : TimfItem
    {
        public static int RegisteredType; public override string DisplayName=>"TIMF Test Conveyor";public override string Texture=>"Content/TestMaterial";public override void SetStaticDefaults(){RegisteredType=Type;}
        public override void SetDefaults(){Item.width=16;Item.height=16;Item.maxStack=99;Item.useTime=10;Item.useAnimation=15;Item.useStyle=ItemUseStyleID.Swing;Item.consumable=true;Item.createTile=TestConveyorTile.RegisteredType;}
    }
    public sealed class TestConveyorTile : TimfTile
    { public static int RegisteredType; public override string Texture=>"Content/TestMaterial";public override int ItemDrop=>TestConveyorItem.RegisteredType;public override float ConveyorVelocity=>1.5f;public override void SetStaticDefaults(){RegisteredType=Type;Main.tileSolid[Type]=true;} }

    public sealed class TestGrassTile : TimfGrassTile
    {
        public static int RegisteredType; public override string Texture=>"Content/TestMaterial";
        public override bool CanGrowOn(int substrateTileType)=>substrateTileType==TileID.Dirt||substrateTileType==TileID.Mud;
        public override int DefaultSubstrateTileType=>TileID.Dirt;
        public override bool CanSpreadAt(int i,int j)=>j>Main.worldSurface; public override void SetStaticDefaults(){RegisteredType=Type;Main.tileSolid[Type]=true;Main.tileBlockLight[Type]=true;}
    }
    public sealed class TestGrassItem : TimfGrassSeedItem
    {
        public static int RegisteredType;
        public override string DisplayName=>"TIMF Test Biome Grass Seeds"; public override string Texture=>"Content/TestMaterial";
        public override IReadOnlyList<string> Tooltip=>new[]{"Use on Dirt or Mud to grow the custom test grass.","The resulting grass can spread during normal world updates."};
        public override int GrassTileType=>TestGrassTile.RegisteredType;
        public override void SetStaticDefaults(){RegisteredType=Type;}
        public override void SetDefaults(){Item.width=16;Item.height=16;Item.maxStack=99;Item.useTime=10;Item.useAnimation=15;Item.useStyle=ItemUseStyleID.Swing;Item.useTurn=true;}
    }
    public sealed class TestCrystalBiome : TimfBiome
    {
        public override string DisplayName=>"TIMF Crystal Test Biome";
        public override bool IsActive(Player player,SceneMetrics scene,IContentLookup content)=>player!=null&&scene!=null&&scene.GetTileCount((ushort)content.TileType<TestGrassTile>())>=12;
        public override void Update(Player player)=>player.AddBuff(BuffID.NightOwl,2);
    }

    public sealed class TestPetWhistle : TimfPetItem
    {
        public override string DisplayName=>"TIMF Pet API Probe Whistle";public override string Texture=>"Content/TestTorchItem";public override IReadOnlyList<string> Tooltip=>new[]{"Activates Terraria's Baby Dinosaur through TimfPetItem."};
        public override int PetBuffType=>BuffID.BabyDinosaur;
        public override int PetProjectileType=>236;
        public override void SetDefaults(){Item.width=20;Item.height=20;Item.useTime=20;Item.useAnimation=20;Item.useStyle=ItemUseStyleID.HoldUp;}
    }

    public sealed class TestLightPetLantern : TimfPetItem
    {
        public override string DisplayName=>"TIMF Light Pet Probe Lantern";public override string Texture=>"Content/TestTorchItem";public override IReadOnlyList<string> Tooltip=>new[]{"Activates Terraria's Wisp light pet through TimfPetItem."};
        public override int PetBuffType=>BuffID.Wisp;
        public override TimfPetSlot PetSlot=>TimfPetSlot.LightPet;
        public override void SetDefaults(){Item.width=20;Item.height=20;Item.useTime=20;Item.useAnimation=20;Item.useStyle=ItemUseStyleID.HoldUp;}
    }

    /// <summary>Fires a framework-owned projectile without using ammunition.</summary>
    public sealed class TestProjectileWeapon : TimfItem
    {
        public override string DisplayName => "TIMF Projectile Probe";
        public override string Texture => "Content/TestSword";
        public override IReadOnlyList<string> Tooltip => new[]
        {
            "Fires a custom TIMF projectile; a hit applies vanilla On Fire.",
            "Tests SetDefaults, vanilla aiStyle, hit callbacks, kill and projectile textures."
        };
        public override void SetDefaults()
        {
            Item.width = 32; Item.height = 32; Item.damage = 18; Item.ranged = true;
            Item.noMelee = true; Item.useTime = 18; Item.useAnimation = 18;
            Item.useStyle = ItemUseStyleID.Shoot; Item.UseSound = SoundID.Item5;
            Item.shoot = TestBoltProjectile.RegisteredType; Item.shootSpeed = 11f;
            Item.rare = Rare.Green;
        }
    }

    public sealed class TestBoltProjectile : TimfProjectile
    {
        public static int RegisteredType { get; private set; }
        public override string DisplayName => "TIMF Test Bolt";
        public override string Texture => "Content/TestMaterial";
        public override bool RunVanillaAI => true;
        public override void SetStaticDefaults() { RegisteredType = Type; }
        public override void SetDefaults()
        {
            Projectile.width = 10; Projectile.height = 10;
            Projectile.friendly = true; Projectile.hostile = false;
            Projectile.ranged = true; Projectile.penetrate = 1;
            Projectile.timeLeft = 300; Projectile.tileCollide = true;
            Projectile.ignoreWater = false; Projectile.aiStyle = 1;
        }
        public override void OnHitNpc(NPC target) { target.AddBuff(BuffID.OnFire, 180); }
    }

    /// <summary>Applies both custom effect kinds without consuming the item.</summary>
    public sealed class TestStatusProbeItem : TimfItem
    {
        public override string DisplayName => "TIMF Status Effect Probe";
        public override string Texture => "Content/TestPotion";
        public override IReadOnlyList<string> Tooltip => new[]
        {
            "Use to apply a custom defense buff and movement debuff.",
            "Save and reload while active to test stable-key .timfbuffs persistence."
        };
        public override void SetDefaults()
        {
            Item.width = 16; Item.height = 24; Item.useTime = 20; Item.useAnimation = 20;
            Item.useStyle = ItemUseStyleID.HoldUp; Item.UseSound = SoundID.Item4; Item.rare = Rare.Orange;
        }
        public override void OnUseItem(Player player)
        {
            player.AddBuff(TestQuestBlessingBuff.RegisteredType, 60 * 60 * 3);
            player.AddBuff(TestQuestBurdenDebuff.RegisteredType, 60 * 30);
        }
    }

    public sealed class TestQuestBlessingBuff : TimfBuff
    {
        public static int RegisteredType { get; private set; }
        public override string DisplayName => "TIMF Quest Blessing";
        public override string Description => "+8 defense; persisted by stable content key.";
        public override string Texture => "Content/TestMaterial";
        public override void SetStaticDefaults() { RegisteredType = Type; }
        public override void Update(Player player, ref int buffIndex) { player.statDefense += 8; }
    }

    public sealed class TestQuestBurdenDebuff : TimfBuff
    {
        public static int RegisteredType { get; private set; }
        public override string DisplayName => "TIMF Quest Burden";
        public override string Description => "Movement speed reduced; the Nurse cannot remove it.";
        public override string Texture => "Content/TestMaterial";
        public override bool IsDebuff => true;
        public override bool CanBeCleared => false;
        public override void SetStaticDefaults() { RegisteredType = Type; }
        public override void Update(Player player, ref int buffIndex) { player.moveSpeed *= 0.75f; }
    }

    public sealed class TestMerchantNpc : TimfNpc
    {
        public override string DisplayName=>"TIMF Test Merchant";public override string Texture=>"Content/TestSword";public override bool IsTownNpc=>true;public override bool RunVanillaAI=>true;public override bool RunVanillaFrame=>true;
        public override void SetDefaults(){Npc.width=16;Npc.height=16;Npc.lifeMax=250;Npc.life=250;Npc.defense=10;Npc.aiStyle=7;Npc.friendly=true;Npc.townNPC=true;}
        public override string GetChat(Player player)=>"This dialogue and shop are owned by TIMF's custom NPC pipeline.";
        public override IReadOnlyList<TimfShopEntry> GetShop(Player player)=>new[]{new TimfShopEntry{ItemType=TestMaterial.RegisteredType,Stack=1,CustomPrice=100}};
        public override IReadOnlyList<TimfDailyQuest> GetDailyQuests(Player player)=>new[]
        {
            new TimfDailyQuest
            {
                InternalName="MaterialDelivery",
                Description="Bring me 3 TIMF Test Materials.",
                RequiredItemType=TestMaterial.RegisteredType,
                RequiredStack=3,
                Rewards=new[]{new TimfQuestReward{ItemType=TestPotion.RegisteredType,Stack=2}},
                StatusEffects=new[]{new TimfQuestStatusEffect{BuffType=TestQuestBlessingBuff.RegisteredType,Duration=60*60*3}}
            },
            new TimfDailyQuest
            {
                InternalName="TorchDelivery",
                Description="Bring me 2 TIMF Custom Test Torches.",
                RequiredItemType=TestTorchItem.RegisteredType,
                RequiredStack=2,
                Rewards=new[]{new TimfQuestReward{ItemType=TestMaterial.RegisteredType,Stack=5}},
                StatusEffects=new[]{new TimfQuestStatusEffect{BuffType=TestQuestBurdenDebuff.RegisteredType,Duration=60*30}}
            }
        };
    }

    /// <summary>
    /// Hostile ground monster (fighter AI, aiStyle 3). Exercises the custom-NPC pipeline for a
    /// non-town enemy: contact damage, taking damage / dying, loot value. Together with the boss
    /// below it also confirms SceneMetrics NPC scanning no longer crashes on modded ids.
    /// </summary>
    public sealed class TestMonsterNpc : TimfNpc
    {
        public override string DisplayName => "TIMF Test Monster";
        public override string Texture => "Content/TestMaterial";
        public override bool RunVanillaAI => true;
        public override bool RunVanillaFrame => true;
        public override void SetDefaults()
        {
            Npc.width = 24; Npc.height = 24;
            Npc.lifeMax = 120; Npc.life = 120;
            Npc.damage = 20; Npc.defense = 6;
            Npc.aiStyle = 3;               // fighter: walks and jumps toward the player
            Npc.knockBackResist = 0.5f;
            Npc.value = 500f;
            Npc.npcSlots = 1f;
            Npc.friendly = false; Npc.townNPC = false;
            Npc.HitSound = SoundID.NPCHit1; Npc.DeathSound = SoundID.NPCDeath2;
        }
    }

    /// <summary>
    /// Hostile flying boss (flying/charging AI, aiStyle 5) with <c>boss = true</c> so it counts as
    /// a boss for SceneMetrics and the minimap. High HP and contact damage; a direct stress test
    /// of the NPC-array expansion fix, since boss detection walks the type-indexed metrics arrays.
    /// </summary>
    public sealed class TestBossNpc : TimfNpc
    {
        public override string DisplayName => "TIMF Test Boss";
        public override string Texture => "Content/TestSword";
        public override bool RunVanillaAI => true;
        public override bool RunVanillaFrame => true;
        public override void SetDefaults()
        {
            Npc.width = 48; Npc.height = 48;
            Npc.lifeMax = 3000; Npc.life = 3000;
            Npc.damage = 40; Npc.defense = 14;
            Npc.aiStyle = 5;               // flying: drifts and charges the player
            Npc.noGravity = true; Npc.noTileCollide = true;
            Npc.knockBackResist = 0f;
            Npc.value = 20000f;
            Npc.npcSlots = 5f;
            Npc.boss = true;
            Npc.friendly = false; Npc.townNPC = false;
            Npc.HitSound = SoundID.NPCHit1; Npc.DeathSound = SoundID.NPCDeath1;
        }
    }
}
