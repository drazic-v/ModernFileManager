using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace AzuriteSpike
{
    internal class Program
    {
        private const string ConnectionString = "UseDevelopmentStorage=true";
        private const string ContainerName = "spike-test";
        private const string TextBlobName = "hello.txt";

        static async Task Main(string[] args)
        {
            var blobServiceClient = new BlobServiceClient(ConnectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);

            // CancellationToken for network operations
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            try
            {
                // 1. Ensure container exists cleanly
                await containerClient.CreateIfNotExistsAsync(cancellationToken: cts.Token);
                Console.WriteLine($"Container '{ContainerName}' initialized.\n");

                // 2. Upload text blob
                var textBlobClient = containerClient.GetBlobClient(TextBlobName);
                await textBlobClient.UploadAsync(
                    BinaryData.FromString("Hello from my .NET console app!"),
                    overwrite: true,
                    cancellationToken: cts.Token);

                Console.WriteLine($"Uploaded blob: {TextBlobName}");
                Console.WriteLine($"Blob URI: {textBlobClient.Uri}\n");

                // 3. Download hello.txt and print its text
                Console.WriteLine("--- Downloading Text Blob Content ---");
                var downloadResult = await textBlobClient.DownloadContentAsync(cancellationToken: cts.Token);
                string textContent = downloadResult.Value.Content.ToString();
                Console.WriteLine($"Downloaded Content: {textContent}\n");

                // 4. Upload photo blobs into virtual folder hierarchy
                Console.WriteLine("--- Uploading Photo Hierarchy ---");
                var photoAClient = containerClient.GetBlobClient("photos/2025/a.jpg");
                var photoBClient = containerClient.GetBlobClient("photos/b.jpg");

                await photoAClient.UploadAsync(BinaryData.FromString("Fake JPEG Data A"), overwrite: true, cancellationToken: cts.Token);
                await photoBClient.UploadAsync(BinaryData.FromString("Fake JPEG Data B"), overwrite: true, cancellationToken: cts.Token);
                Console.WriteLine("Uploaded 'photos/2025/a.jpg' and 'photos/b.jpg'\n");

                // 5. Flat Listing (GetBlobsAsync)
                Console.WriteLine("--- Flat Blob Listing (GetBlobsAsync) ---");
                await foreach (var blobItem in containerClient.GetBlobsAsync(cancellationToken: cts.Token))
                {
                    Console.WriteLine($" - {blobItem.Name}");
                }
                Console.WriteLine();

                // 6. Hierarchical Listing at Root Level (GetBlobsByHierarchyAsync)
                Console.WriteLine("--- Hierarchical Listing at Root (delimiter: '/') ---");
                var hierarchyOptions = new GetBlobsByHierarchyOptions();
                hierarchyOptions.Delimiter = "/";

                await foreach (var hierarchyItem in containerClient.GetBlobsByHierarchyAsync(hierarchyOptions, cts.Token))
                {
                    if (hierarchyItem.IsPrefix)
                    {
                        Console.WriteLine($" [Virtual Folder] {hierarchyItem.Prefix}");
                    }
                    else
                    {
                        Console.WriteLine($" [Blob File]      {hierarchyItem.Blob.Name}");
                    }
                }
                Console.WriteLine();

                // 7. Hierarchical Listing inside 'photos/' prefix
                hierarchyOptions.Prefix = "photos/";
                Console.WriteLine("--- Hierarchical Listing inside 'photos/' prefix ---");
                await foreach (var hierarchyItem in containerClient.GetBlobsByHierarchyAsync(hierarchyOptions, cts.Token))
                {
                    if (hierarchyItem.IsPrefix)
                    {
                        Console.WriteLine($" [Virtual Sub-Folder] {hierarchyItem.Prefix}");
                    }
                    else
                    {
                        Console.WriteLine($" [Blob File]          {hierarchyItem.Blob.Name}");
                    }
                }
                Console.WriteLine();
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[Cancelled] Request timed out via CancellationToken.");
            }
            catch (RequestFailedException ex)
            {
                Console.WriteLine($"[Azure Error] Status: {ex.Status}, ErrorCode: {ex.ErrorCode}");
            }
            finally
            {
                // 8. Always cleanup the container so every run starts clean
                // Safe cleanup: suppress cleanup errors if Azurite is unreachable
                try
                {
                    using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await containerClient.DeleteIfExistsAsync(cancellationToken: cleanupCts.Token);
                    Console.WriteLine($"Container '{ContainerName}' deleted successfully.");
                }
                catch
                {
                    Console.WriteLine("Skipped container cleanup (Azurite unreachable).");
                }
            }
        }
    }
}