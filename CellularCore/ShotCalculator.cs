using System;

namespace CellularCore
{
    public static class ShotCalculator
    {
        // Counts downed pins for a specific shot.
        // Representation: bit=1 => pin standing, bit=0 => pin down.
        public static int GetDownedPinsForShot(short pinStates, short shot1PinStates, int shotNumber)
        {
            int count = 0;

            if (shotNumber == 1)
            {
                // Count pins that are down (bit is 0)
                for (int i = 0; i < 10; i++)
                {
                    if ((pinStates & (1 << i)) == 0) count++;
                }
            }
            else
            {
                // Count newly downed pins: were up in shot1, but now down
                for (int i = 0; i < 10; i++)
                {
                    bool wasStanding = (shot1PinStates & (1 << i)) != 0;
                    bool isNowDown = (pinStates & (1 << i)) == 0;

                    if (wasStanding && isNowDown)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        // Counts pins that were up in previousState and are down in currentState.
        public static int GetDownedPinsForFrameView(short previousState, short currentState)
        {
            int count = 0;
            for (int i = 0; i < 10; i++) // Only check first 10 bits
            {
                int prevPin = (previousState >> i) & 1;
                int currPin = (currentState >> i) & 1;

                if (prevPin == 1 && currPin == 0) // Pin was up before and is now down
                {
                    count++;
                }
            }
            return count;
        }

        // Count total downed pins in the currentState (bit 0 means down).
        public static int GetDownedPinsTotalFrame(short currentState)
        {
            int count = 0;
            for (int i = 0; i < 10; i++) // Only check first 10 bits
            {
                int currPin = (currentState >> i) & 1;

                if (currPin == 0) // Pin is down
                {
                    count++;
                }
            }
            return count;
        }

        public static short CalculateShotType(short pinstates, int shotNum)
        {
            // Local flags to keep logic clean
            bool allDown = (pinstates == 0);
            bool headpinStanding = (pinstates & (1 << 0)) != 0;
            bool isSplitPattern = IsSplit(pinstates);

            if (shotNum == 1)
            {
                if (allDown)
                {
                    // Set 12th bit for Strike
                    pinstates |= (1 << 11);
                }
                else if (!headpinStanding && isSplitPattern)
                {
                    // Set 13th bit for Split
                    pinstates |= (1 << 12);
                }
                else if (headpinStanding && isSplitPattern)
                {
                    // Set 14th bit for Washout
                    pinstates |= (1 << 13);
                }
            }
            else if (shotNum == 2)
            {
                if (allDown)
                {
                    // Set 12th bit for Spare
                    pinstates |= (1 << 11);
                }
            }

            return pinstates;
        }

        private static bool IsSplit(short pinstates)
        {
            // Logic remains the same as previous: 
            // Checks for at least one empty column between standing pins.
            bool[] cols = new bool[7];
            cols[0] = (pinstates & (1 << 6)) != 0; // Pin 7
            cols[1] = (pinstates & (1 << 3)) != 0; // Pin 4
            cols[2] = (pinstates & (1 << 1)) != 0 || (pinstates & (1 << 7)) != 0; // 2, 8
            cols[3] = (pinstates & (1 << 0)) != 0 || (pinstates & (1 << 4)) != 0; // 1, 5
            cols[4] = (pinstates & (1 << 2)) != 0 || (pinstates & (1 << 8)) != 0; // 3, 9
            cols[5] = (pinstates & (1 << 5)) != 0; // Pin 6
            cols[6] = (pinstates & (1 << 9)) != 0; // Pin 10

            int first = -1, last = -1;
            for (int i = 0; i < 7; i++)
            {
                if (cols[i])
                {
                    if (first == -1) first = i;
                    last = i;
                }
            }

            if (first == -1 || first == last) return false;

            for (int i = first; i < last; i++)
            {
                if (!cols[i]) return true;
            }
            return false;
        }
    }
}