using System.Collections.Generic;
using System.Linq;

namespace CellularCore
{
    /// <summary>
    /// Determines whether a spare leave (pins standing after the 1st shot) is a split, washout,
    /// or a normal spare leave.
    ///
    /// Pin layout:
    ///     7  8  9  10
    ///      4  5  6
    ///       2  3
    ///        1
    ///
    /// A SPLIT   – headpin (1) is down and the remaining pins form two or more disconnected groups.
    /// A WASHOUT – same disconnected-group condition BUT the headpin is still standing.
    ///
    /// Algorithm source: "How to Determine that a Spare Leave is a Split or a Washout"
    /// </summary>
    public static class SplitWashoutDetector
    {
        // ── Adjacency matrix ────────────────────────────────────────────────────────
        // Indexed by pin number (rows 0–6).  Row 0 is unused.
        // Each row lists the pins that are "adjacent" (directly below / behind) the
        // given pin.  Zeros pad unused columns — a '0' pin can never appear in the
        // standing-pin list so zeros are safe sentinels.
        //
        // Pins 7–10 sit in the back row and have NO adjacent pins, so they are not
        // included (the loop stops when the standing pin > 6).
        //
        //  Row  Pin  Adjacent pins
        //   0    –   (unused)
        //   1    1   2  3  5  8  9
        //   2    2   4  5  8  0  0
        //   3    3   5  6  9  0  0
        //   4    4   7  8  0  0  0
        //   5    5   8  9  0  0  0
        //   6    6   9 10  0  0  0
        private static readonly int[,] AdjacencyMatrix =
        {
            {  0,  0,  0,  0,  0 }, // row 0 – unused
            {  2,  3,  5,  8,  9 }, // row 1 – pin 1
            {  4,  5,  8,  0,  0 }, // row 2 – pin 2
            {  5,  6,  9,  0,  0 }, // row 3 – pin 3
            {  7,  8,  0,  0,  0 }, // row 4 – pin 4
            {  8,  9,  0,  0,  0 }, // row 5 – pin 5
            {  9, 10,  0,  0,  0 }, // row 6 – pin 6
        };

        // ── Public API ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns <c>'s'</c> (split), <c>'w'</c> (washout), or <c>' '</c> (normal leave).
        /// </summary>
        /// <param name="standingPins">
        /// Pin numbers 1–10 that are still standing after the 1st shot.
        /// The list need not be sorted — this method sorts internally.
        /// </param>
        public static char IsSplitOrWashout(IEnumerable<int> standingPins)
        {
            if (standingPins == null) return ' ';

            var pins = standingPins
                .Where(p => p >= 1 && p <= 10)
                .OrderBy(p => p)
                .ToList();

            // Need at least 2 standing pins for a split/washout to be possible.
            if (pins.Count < 2) return ' ';

            // ── Build the two-row adjacency array ────────────────────────────────
            // Columns 0–10; column 0 is unused (pins are 1-based).
            // row0[p] = 1  →  pin p is standing
            // row1[p] = 1  →  pin p was "reached" (it is adjacent to at least one
            //                 other standing pin that is ≤ 6)
            int[] row0 = new int[11];
            int[] row1 = new int[11];

            foreach (var p in pins)
                row0[p] = 1;

            // For every standing pin that is in the front three rows (1–6),
            // mark each of its standing neighbours in row1.
            // Stop as soon as we hit a pin > 6 (back row pins have no adjacency).
            foreach (var p in pins)
            {
                if (p > 6) break;

                for (int col = 0; col < 5; col++)
                {
                    int adj = AdjacencyMatrix[p, col];
                    if (adj == 0) break;            // no more adjacent pins in this row
                    if (row0[adj] == 1)             // adjacent pin is standing
                        row1[adj] = 1;              // mark it as "reached"
                }
            }

            // ── Determine disconnected groups ────────────────────────────────────
            // Count standing pins that were NOT reached by any neighbour.
            // The original description says "sum the bottom row; if total > 1 → split/washout"
            // but that sum counts REACHED pins.  The correct test — consistent with the
            // conceptual definition "two or more standing pins not marked" — is:
            //
            //   unreachedCount = standingCount - sum(row1)  ≥ 2
            //
            // Example: 3-10 split — neither pin is adjacent to the other, so row1 is all
            // zeros, sum = 0, unreached = 2 → correctly flagged as a split.
            int sumRow1 = 0;
            for (int i = 1; i <= 10; i++)
                sumRow1 += row1[i];

            int unreachedCount = pins.Count - sumRow1;

            if (unreachedCount < 2) return ' ';     // all groups connected — normal leave

            // ── Split vs. Washout ────────────────────────────────────────────────
            // Washout = headpin (pin 1) is still standing; split = headpin is down.
            return pins[0] == 1 ? 'w' : 's';
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Extracts the list of standing pins from a LeaveType bitmask.
        /// Assumes bits 0–9 correspond to pins 1–10 respectively
        /// (bit 0 = pin 1 standing, bit 1 = pin 2 standing, …, bit 9 = pin 10 standing).
        /// Bits 12 and 13 (split / washout flags) are ignored.
        /// </summary>
        public static List<int> GetStandingPinsFromLeaveType(short leaveType)
        {
            var pins = new List<int>();
            for (int i = 0; i < 10; i++)
            {
                if ((leaveType & (1 << i)) != 0)
                    pins.Add(i + 1);   // bit i → pin (i+1)
            }
            return pins;
        }

        /// <summary>
        /// Convenience overload: derives standing pins from a LeaveType bitmask
        /// and immediately returns the split/washout result.
        /// </summary>
        public static char IsSplitOrWashout(short leaveType)
            => IsSplitOrWashout(GetStandingPinsFromLeaveType(leaveType));
    }
}
