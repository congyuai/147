using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dustline
{
    [Serializable]
    public sealed class LootEntry
    {
        public string ItemId;
        public int Min = 1;
        public int Max = 1;
        public int Weight = 1;
    }

    [CreateAssetMenu(menuName = "Dustline/Loot Table")]
    public sealed class LootTable : ScriptableObject
    {
        public List<LootEntry> Entries = new List<LootEntry>();
        public int Rolls = 3;

        public void Fill(GridInventory target, System.Random random)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            if (Entries == null || Rolls <= 0)
                return;

            var eligible = new List<LootEntry>();
            long totalWeight = 0;
            foreach (LootEntry entry in Entries)
            {
                if (entry == null || entry.Weight <= 0 || entry.Min < 1 || entry.Max < entry.Min
                    || target.Catalog.Find(entry.ItemId) == null)
                    continue;

                eligible.Add(entry);
                totalWeight += entry.Weight;
            }
            if (totalWeight == 0)
                return;

            for (int roll = 0; roll < Rolls; roll++)
            {
                double ticket = random.NextDouble() * totalWeight;
                LootEntry selected = eligible[eligible.Count - 1];
                long cumulative = 0;
                foreach (LootEntry entry in eligible)
                {
                    cumulative += entry.Weight;
                    if (ticket < cumulative)
                    {
                        selected = entry;
                        break;
                    }
                }

                long range = (long)selected.Max - selected.Min + 1;
                int quantity = (int)(selected.Min + (long)(random.NextDouble() * range));
                target.TryAdd(selected.ItemId, quantity, out _);
            }
        }
    }
}
