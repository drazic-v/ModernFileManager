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

        [Fact]
        public void PathsEqual_SameValueDifferentProvider_IsFalse()
        {
            var local = new StoragePath { ProviderId = "local", Value = "/Docs" };
            var azure = new StoragePath { ProviderId = "azure", Value = "/Docs" };

            Assert.False(StoragePath.PathsEqual(local, azure));
        }

        [Fact]
        public void PathsEqual_SameProviderAndValue_IsTrue()
        {
            var a = new StoragePath { ProviderId = "local", Value = "/Docs" };
            var b = new StoragePath { ProviderId = "local", Value = "/Docs" };

            Assert.True(StoragePath.PathsEqual(a, b));
        }

        [Fact]
        public void IsSameOrDescendant_AcrossProviders_IsFalse()
        {
            var ancestor = new StoragePath { ProviderId = "local", Value = "/Docs" };
            var candidate = new StoragePath { ProviderId = "azure", Value = "/Docs/sub" };

            Assert.False(StoragePath.IsSameOrDescendant(candidate, ancestor));
        }

        [Fact]
        public void Comparer_DedupesEqualPaths_ButKeepsDifferentProvidersApart()
        {
            var set = new HashSet<StoragePath>(StoragePathComparer.Instance)
    {
        new() { ProviderId = "local", Value = "/Docs" },
        new() { ProviderId = "local", Value = "/Docs" },   // separate instance, same path
        new() { ProviderId = "azure", Value = "/Docs" },   // same value, other provider
    };

            Assert.Equal(2, set.Count);
        }

        [Fact]
        public void Comparer_HandlesNulls()
        {
            var path = new StoragePath { ProviderId = "local", Value = "/Docs" };

            Assert.True(StoragePathComparer.Instance.Equals(null, null));
            Assert.False(StoragePathComparer.Instance.Equals(path, null));
            Assert.False(StoragePathComparer.Instance.Equals(null, path));
        }
    }
}
