using FileManager.TestKit;
using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.Infrastructure.Tests
{
    public class AzuriteContainerFixtureTests : IClassFixture<AzuriteContainerFixture>
    {
        private readonly AzuriteContainerFixture _fixture;

        public AzuriteContainerFixtureTests(AzuriteContainerFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task UploadAndDownloadBlob_ContentMatches()
        {
            Assert.SkipWhen(
                !_fixture.IsAzuriteAvailable,
                "Azurite is not running locally.");

            // Arrange
            string blobName = $"test-blob-{Guid.NewGuid()}.txt";
            string expectedContent = "Hello from Azurite fixture test!";
            var blobClient = _fixture.ContainerClient.GetBlobClient(blobName);

            // Act
            using var uploadStream = new MemoryStream(
                Encoding.UTF8.GetBytes(expectedContent));

            await blobClient.UploadAsync(uploadStream, overwrite: true, cancellationToken: TestContext.Current.CancellationToken);

            var downloadResponse = await blobClient.DownloadContentAsync(cancellationToken: TestContext.Current.CancellationToken);
            string actualContent = downloadResponse.Value.Content.ToString();

            // Assert
            Assert.Equal(expectedContent, actualContent);
        }
    }
}
