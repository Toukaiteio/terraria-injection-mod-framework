using System;
using Terraria;
using Terraria.ID;

namespace TIMF.Content
{
    /// <summary>
    /// Small builder for recipes registered after Terraria has finished creating its vanilla
    /// recipe table. Item and tile ids may be either vanilla ids or allocated TIMF ids.
    /// </summary>
    public sealed class TimfRecipe
    {
        private readonly Recipe _recipe = new Recipe();
        private int _ingredientCount;
        private bool _registered;

        private TimfRecipe(int resultType, int resultStack)
        {
            if (resultType <= 0) throw new ArgumentOutOfRangeException(nameof(resultType));
            _recipe.createItem.SetDefaults(resultType);
            _recipe.createItem.stack = Math.Max(1, resultStack);
            // Vanilla shimmer/decrafting tables were already finalized before TIMF recipes are
            // appended. Disable decrafting unless a future dedicated API rebuilds those tables.
            _recipe.notDecraftable = true;
        }

        public static TimfRecipe Create(int resultType, int resultStack = 1)
        {
            return new TimfRecipe(resultType, resultStack);
        }

        public TimfRecipe AddIngredient(int itemType, int stack = 1)
        {
            EnsureMutable();
            if (itemType <= 0) throw new ArgumentOutOfRangeException(nameof(itemType));
            if (_ingredientCount >= Recipe.maxRequirements)
                throw new InvalidOperationException("A recipe cannot have more than "
                                                    + Recipe.maxRequirements + " ingredients");
            var item = _recipe.requiredItem[_ingredientCount];
            item.SetDefaults(itemType);
            item.stack = Math.Max(1, stack);
            _ingredientCount++;
            return this;
        }

        public TimfRecipe AddTile(int tileType)
        {
            EnsureMutable();
            if (tileType < 0) throw new ArgumentOutOfRangeException(nameof(tileType));
            _recipe.requiredTile = tileType;
            return this;
        }

        /// <summary>Append the completed recipe to Terraria's live recipe table.</summary>
        public int Register()
        {
            EnsureMutable();
            if (_ingredientCount == 0)
                throw new InvalidOperationException("A TIMF recipe needs at least one ingredient");
            if (Main.recipe == null || Recipe.numRecipes < 0
                || Recipe.numRecipes >= Main.recipe.Length)
                throw new InvalidOperationException("Terraria's recipe table has no free slot");
            if (_recipe.requiredTile >= 0
                && (_recipe.requiredTile >= Recipe.TileUsedInRecipes.Length
                    || _recipe.requiredTile >= TileID.Count))
                throw new InvalidOperationException("Required tile id is outside the expanded tile space: "
                                                    + _recipe.requiredTile);

            for (var i = 0; i < _ingredientCount; i++)
            {
                var item = _recipe.requiredItem[i];
                _recipe.requiredItemQuickLookup[i] =
                    new Recipe.RequiredItemEntry(item.type, item.stack);
                if (item.type >= 0 && item.type < ItemID.Sets.IsAMaterial.Length)
                    ItemID.Sets.IsAMaterial[item.type] = true;
            }

            var index = Recipe.numRecipes;
            Main.recipe[index] = _recipe;
            if (_recipe.requiredTile >= 0)
                Recipe.TileUsedInRecipes[_recipe.requiredTile] = true;
            if (_recipe.createItem.type >= 0
                && _recipe.createItem.type < ItemID.Sets.IsCrafted.Length)
                ItemID.Sets.IsCrafted[_recipe.createItem.type] = index;
            Recipe.numRecipes++;
            _registered = true;
            return index;
        }

        private void EnsureMutable()
        {
            if (_registered) throw new InvalidOperationException("This recipe is already registered");
        }
    }
}
