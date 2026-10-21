using System;
using System.Collections.Generic;

namespace Bretaheim.Data
{
    /// <summary>
    /// The player's inventory in Britannia (MAPPING_DECISIONS 2026-10-22): 8 columns × 14 rows,
    /// a slot for each kind of Ultima item with room to spare, shown a few rows at a time and
    /// scrolled. The size is never smaller than the rows that hold something, so a character
    /// leaving Britannia for a Valheim world keeps every item (Valheim drops what lies past its
    /// rows).
    /// </summary>
    public static class InventorySize
    {
        public const int Columns = 8, Rows = 14, VanillaRows = 4;
        /// <summary>Rows shown at once; more scroll.</summary>
        public const int Visible = 6;

        /// <summary>
        /// The inventory's rows: Valheim's own (4, or what was bought from Haldor), 14 in
        /// Britannia, and never fewer than the last row holding an item.
        /// </summary>
        public static int RowsFor(bool britannia, int vanillaRows, IEnumerable<int> occupiedRows)
        {
            int rows = Math.Max(1, vanillaRows);
            if (britannia) rows = Math.Max(rows, Rows);
            if (occupiedRows != null) foreach (int y in occupiedRows) rows = Math.Max(rows, y + 1);
            return rows;
        }

        /// <summary>How many rows the inventory window shows (Valheim's own size up to <see cref="Visible"/>; the rest scroll).</summary>
        public static int VisibleFor(int rows, int vanillaRows) => Math.Min(rows, Math.Max(Math.Max(1, vanillaRows), Visible));
    }
}
