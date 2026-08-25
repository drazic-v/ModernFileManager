using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using FileManager.Core.Models;

namespace FileManager.Core.Tests.Models
{
    public class StoragePathTests
    {
        [Fact]
        public void Combine_AppendsChildSegment()
        {
            var parent = new StoragePath { ProviderId = "local-windows", Value = "C:/Users/me" };
            var child = parent.Combine("file.txt");
            Assert.Equal("C:/Users/me/file.txt", child.Value);
        }

        [Fact]
        public void Name_ReturnsLastSegment()
        {
            var path = new StoragePath { ProviderId = "local-windows", Value = "C:/Users/me/file.txt" };
            Assert.Equal("file.txt", path.Name);
        }

        [Theory]
        [InlineData("C:/Users/me/file.txt", "C:/Users/me")]
        [InlineData("C:/Users/me/Documents/", "C:/Users/me")]
        public void Parent_OnNestedPath_ReturnsContaingFolder(string value, string expectedParentValue)
        {
            var path = new StoragePath { ProviderId = "local-windows", Value = value };
            var parent = path.Parent();
            Assert.NotNull(parent);
            Assert.Equal(expectedParentValue, parent!.Value);
        }

        [Fact]
        public void Parent_OnRootPath_ReturnsNull()
        {
            var path = new StoragePath { ProviderId = "local-windows", Value = "C:/" };
            var parent = path.Parent();
            Assert.Null(parent);
        }
    }
}
