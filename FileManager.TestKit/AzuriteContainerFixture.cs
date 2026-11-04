using System;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Xunit;

namespace FileManager.TestKit
{
    public sealed class AzuriteContainerFixture : IAsyncLifetime
    {
        public BlobServiceClient BlobServiceClient { get; private set; }
        public BlobContainerClient ContainerClient { get; private set; }
        public bool IsAzuriteAvailable { get; private set; }

        public async ValueTask InitializeAsync()
        {
            IsAzuriteAvailable = false;

            string connectionString = Environment.GetEnvironmentVariable("FILEMANAGER_AZURE_CONNECTION");
            if (string.IsNullOrWhiteSpace(connectionString))
                connectionString = "UseDevelopmentStorage=true";

            string containerName = $"fm-test-{Guid.NewGuid()}";
            BlobServiceClient = new BlobServiceClient(connectionString);
            ContainerClient = BlobServiceClient.GetBlobContainerClient(containerName);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await ContainerClient.CreateIfNotExistsAsync(cancellationToken: cts.Token);
                IsAzuriteAvailable = true;
            }
            catch (OperationCanceledException)
            {
                // Azurite is not running — swallow exception so test fixture initializes cleanly
                // Tests will inspect IsAzuriteAvailable and skip dynamically.
                IsAzuriteAvailable = false;
            }
            catch (RequestFailedException ex)
            {
                // Real Azure error (e.g. invalid container name)
                throw new Exception($"[Azure Error] Request failed with status {ex.Status}.", ex);
            }
        }

        public async ValueTask DisposeAsync()
        {
            // Only clean up if Azurite was successfully connected and container created
            if (!IsAzuriteAvailable || ContainerClient == null)
                return;

            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await ContainerClient.DeleteIfExistsAsync(cancellationToken: cleanupCts.Token);
            }
            catch
            {
                // Prevent teardown exceptions from hiding primary test failures
            }
        }
    }
}