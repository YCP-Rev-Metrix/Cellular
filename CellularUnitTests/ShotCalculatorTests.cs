using CellularCore;
using Xunit;

namespace CellularUnitTests
{
    public class ShotCalculatorTests
    {
        [Fact]
        public void GetDownedPinsForShot_Shot1_AllDown_Returns10()
        {
            short pinStatesAllDown = 0; // all bits 0 => all pins down
            short shot1PinStates = 0;
            int result = ShotCalculator.GetDownedPinsForShot(pinStatesAllDown, shot1PinStates, 1);
            Assert.Equal(10, result);
        }

        [Fact]
        public void GetDownedPinsForShot_Shot1_AllUp_Returns0()
        {
            short pinStatesAllUp = 0b11_1111_1111; // 1023 decimal => all pins standing
            int result = ShotCalculator.GetDownedPinsForShot(pinStatesAllUp, 0, 1);
            Assert.Equal(0, result);
        }

        [Fact]
        public void GetDownedPinsForShot_Shot2_NewlyDowned_ReturnsCorrectCount()
        {
            // shot1 all up (1023), shot2: pins 1-3 down => subtract bits 0..2
            short shot1 = 0b11_1111_1111; // 1023 all up
            short shot2 = (short)(shot1 & ~((1 << 0) | (1 << 1) | (1 << 2))); // make bits 0..2 zero (down)
            int result = ShotCalculator.GetDownedPinsForShot(shot2, shot1, 2);
            Assert.Equal(3, result);
        }

        [Fact]
        public void GetDownedPinsForFrameView_CountsTransitionsCorrectly()
        {
            short previous = 0b11_1111_1111; // all up
            short current = (short)(previous & ~((1 << 4) | (1 << 7))); // pins 5 and 8 down
            int result = ShotCalculator.GetDownedPinsForFrameView(previous, current);
            Assert.Equal(2, result);
        }

        [Fact]
        public void GetDownedPinsTotalFrame_CountsDownPins()
        {
            // two pins down (bits 2 and 5)
            short state = (short)(0b11_1111_1111 & ~((1 << 2) | (1 << 5)));
            int result = ShotCalculator.GetDownedPinsTotalFrame(state);
            Assert.Equal(2, result);
        }

        [Fact]
        public void GetDownedPinsForShot_Shot2_IgnoresPreviouslyDownPins_ReturnsOnlyNewlyDown()
        {
            short shot1 = (short)(0b11_1111_1111 & ~(1 << 0)); // pin 1 (bit 0) already down
            short current = (short)(shot1 & ~(1 << 1)); // pin 2 (bit 1) newly down
            int result = ShotCalculator.GetDownedPinsForShot(current, shot1, 2);
            // Only pin 2 is newly down; pin 1 was already down so shouldn't be counted.
            Assert.Equal(1, result);
        }

        [Fact]
        public void GetDownedPinsForShot_ShotNumberOtherThan1_TreatedAsSubsequent()
        {
            short shot1 = 0b11_1111_1111; // all up
            // current: pins 1..5 down (bits 0..4)
            short current = (short)(shot1 & ~((1 << 0) | (1 << 1) | (1 << 2) | (1 << 3) | (1 << 4)));
            int result = ShotCalculator.GetDownedPinsForShot(current, shot1, 3); // shotNumber 3 should act like subsequent shot
            Assert.Equal(5, result);
        }

        [Fact]
        public void GetDownedPinsTotalFrame_AllUp_Returns0()
        {
            short allUp = 0b11_1111_1111;
            int result = ShotCalculator.GetDownedPinsTotalFrame(allUp);
            Assert.Equal(0, result);
        }

        [Fact]
        public void GetDownedPinsTotalFrame_AllDown_Returns10()
        {
            short allDown = 0;
            int result = ShotCalculator.GetDownedPinsTotalFrame(allDown);
            Assert.Equal(10, result);
        }

        [Fact]
        public void GetDownedPinsForFrameView_NoTransitions_Returns0()
        {
            short state = (short)(0b11_1111_1111 & ~((1 << 2) | (1 << 6))); // some pins down
            int result = ShotCalculator.GetDownedPinsForFrameView(state, state);
            Assert.Equal(0, result);
        }

        [Fact]
        public void GetDownedPinsForFrameView_IgnoresBitsBeyond10()
        {
            // Bit 10 (index 10) toggles between previous and current, but first 10 bits remain same.
            short previous = (short)(0b11_1111_1111 | (1 << 10)); // set bit 10
            short current = (short)(previous & ~(1 << 10)); // clear bit 10
            // Function only checks bits 0..9, so transition on bit 10 should be ignored.
            int result = ShotCalculator.GetDownedPinsForFrameView(previous, current);
            Assert.Equal(0, result);
        }
    }
}