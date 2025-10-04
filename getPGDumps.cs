using System;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs;

namespace backupPostgreSQLDatabase;

public class getPGDumps
{
    private readonly ILogger _logger;

    public getPGDumps(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<getPGDumps>();
    }

    private readonly string pgHost = Environment.GetEnvironmentVariable("PG_HOST");
    private readonly string pgPort = Environment.GetEnvironmentVariable("PG_PORT") ?? "5432";
    private readonly string pgUser = Environment.GetEnvironmentVariable("PG_USER");
    private readonly string pgPassword = Environment.GetEnvironmentVariable("PG_PASSWORD");
    private readonly string pgDatabase = Environment.GetEnvironmentVariable("PG_DATABASE");
    private readonly string userAssignedClientId = Environment.GetEnvironmentVariable("UAMI_CLIENT_ID");

    private readonly string storageAccountUrl = Environment.GetEnvironmentVariable("STORAGE_ACCOUNT_URL");
    private readonly string containerName = Environment.GetEnvironmentVariable("STORAGE_CONTAINER");

    [Function("PostgresDailyBackup")]
    public async Task Run([TimerTrigger("0 0 18 * * *")] TimerInfo myTimer)
    {
        _logger.LogInformation($"PostgreSQL backup function started at: {DateTime.UtcNow}");
        string backupFile = $"/tmp/{pgDatabase}_{DateTime.UtcNow:yyyyMMddHHmm}.dump";

        if (myTimer.ScheduleStatus is not null)
        {
            _logger.LogInformation("Next timer schedule at: {nextSchedule}", myTimer.ScheduleStatus.Next);
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pg_dump",
                Arguments = $"-h {pgHost} -p {pgPort} -U {pgUser} -Fc {pgDatabase} -f {backupFile}",
                RedirectStandardError = true,
                UseShellExecute = false,
                Environment =
                {
                    ["PGPASSWORD"] = pgPassword
                }
            };

            using (var process = Process.Start(psi))
            {
                string error = await process.StandardError.ReadToEndAsync();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new Exception($"pg_dump failed: {error}");
                }
            }

            var blobService = new BlobServiceClient(new Uri(storageAccountUrl), new DefaultAzureCredential(
                new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = userAssignedClientId
                }));
            var containerClient = blobService.GetBlobContainerClient(containerName);
            await containerClient.CreateIfNotExistsAsync();

            string blobName = Path.GetFileName(backupFile);
            var blobClient = containerClient.GetBlobClient(blobName);

            await using (FileStream fs = File.OpenRead(backupFile))
            {
                await blobClient.UploadAsync(fs, overwrite: true);
            }

            _logger.LogInformation($"Backup uploaded to storage: {blobName}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Backup failed: {ex.Message}");
            throw;
        }
        finally
        {
            // Clean up local file
            if (File.Exists(backupFile))
            {
                File.Delete(backupFile);
            }
        }

    }
}