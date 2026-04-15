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
            if (shotNum == 1)
            {
                if (pinstates == 0)
                {
                    // All pins down — Strike (bit 11)
                    pinstates |= (1 << 11);
                }
                else
                {
                    // Determine whether the leave is a split or washout using the
                    // graph-based adjacency algorithm in SplitWashoutDetector.
                    char leaveType = SplitWashoutDetector.IsSplitOrWashout(pinstates);

                    if (leaveType == 's')
                        pinstates |= (1 << 12);     // Split (bit 12)
                    else if (leaveType == 'w')
                        pinstates |= (1 << 13);     // Washout (bit 13)
                }
            }
            else if (shotNum == 2)
            {
                if (pinstates == 0)
                {
                    // All pins down — Spare (bit 11)
                    pinstates |= (1 << 11);
                }
            }

            return pinstates;
        }
    }
}