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
    }
}