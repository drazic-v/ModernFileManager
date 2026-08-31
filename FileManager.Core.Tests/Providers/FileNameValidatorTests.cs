using FileManager.Core.Providers;
using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.Core.Tests.Providers
{
    public class FileNameValidatorTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(".")]
        [InlineData("..")]
        public void IsValid_OnEmptyOrSpecialNames_ReturnsFalse(string? name) =>
        Assert.False(FileNameValidator.IsValid(name));

        [Theory]
        [InlineData("report<final>.txt")]
        [InlineData("cost:report.txt")]
        [InlineData("a/b")]
        [InlineData("pipe|file.txt")]
        [InlineData("star*.txt")]
        public void IsValid_OnForbiddenCharacters_ReturnsFalse(string name) =>
            Assert.False(FileNameValidator.IsValid(name));

        [Theory]
        [InlineData("trailing space ")]
        [InlineData("trailing.dot.")]
        public void IsValid_OnTrailingSpaceOrDot_ReturnsFalse(string name) =>
            Assert.False(FileNameValidator.IsValid(name));

        [Theory]
        [InlineData("CON")]
        [InlineData("con")]
        [InlineData("NUL.txt")]
        [InlineData("COM1")]
        [InlineData("lpt9.log")]
        public void IsValid_OnWindowsReservedNames_ReturnsFalse(string name) =>
            Assert.False(FileNameValidator.IsValid(name));

        [Theory]
        [InlineData("COM10")]     // two digits - not an actual reserved device name
        [InlineData("COMPANY")]   // starts with "COM" but isn't the device name itself
        [InlineData("report.txt")]
        [InlineData(".gitignore")]
        [InlineData("résumé.pdf")]
        public void IsValid_OnLegitimateNames_ReturnsTrue(string name) =>
            Assert.True(FileNameValidator.IsValid(name));

        [Theory]
        [InlineData(254)]
        [InlineData(255)]
        public void IsValid_OnNamesWithinMaximumLength_ReturnsTrue(int length)
        {
            var name = new string('a', length);

            Assert.True(FileNameValidator.IsValid(name));
        }

        [Fact]
        public void IsValid_OnNameExceedingMaximumLength_ReturnsFalse()
        {
            var name = new string('a', 256);

            Assert.False(FileNameValidator.IsValid(name));
        }

    }
}
