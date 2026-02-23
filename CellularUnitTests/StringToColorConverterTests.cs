using System;
using System.Globalization;
using Xunit;
using CellularCore;
using Microsoft.Maui.Graphics;

namespace CellularUnitTests
{
    public class StringToColorConverterTests
    {
        private readonly StringToColorConverter _converter = new();

        [Fact]
        public void Convert_NamedColorRed_ReturnsColorsRed()
        {
            var result = _converter.Convert("Red", typeof(Color), null, CultureInfo.InvariantCulture);
            Assert.IsType<Color>(result);
            Assert.Equal(Colors.Red, (Color)result);
        }

        [Theory]
        [InlineData("blue")]
        [InlineData("Blue")]
        [InlineData("BLUE")]
        public void Convert_NamedColorCaseInsensitive_ReturnsColorsBlue(string input)
        {
            var result = _converter.Convert(input, typeof(Color), null, CultureInfo.InvariantCulture);
            Assert.IsType<Color>(result);
            Assert.Equal(Colors.Blue, (Color)result);
        }

        [Fact]
        public void Convert_HexWithHash_ReturnsParsedColor()
        {
            var result = _converter.Convert("#FF0000", typeof(Color), null, CultureInfo.InvariantCulture);
            Assert.IsType<Color>(result);
            Assert.Equal(Color.Parse("#FF0000"), (Color)result);
        }
    }
}