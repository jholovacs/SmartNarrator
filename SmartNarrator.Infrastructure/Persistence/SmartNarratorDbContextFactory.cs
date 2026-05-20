using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace SmartNarrator.Infrastructure.Persistence;

/// <summary>Design-time factory for EF Core CLI migrations.</summary>
public sealed class SmartNarratorDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<SmartNarratorDbContext>
{
    public SmartNarratorDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        var cwd = Directory.GetCurrentDirectory();
        var exeDir = string.IsNullOrEmpty(Environment.ProcessPath)
            ? null
            : Path.GetDirectoryName(Environment.ProcessPath);

        // Published Docker image: appsettings.json sits next to SmartNarrator.Api.dll under WORKDIR (/app).
        // Local dev: cwd may be repo root or Infrastructure project with sibling SmartNarrator.Api.
        string? apiDir = null;
        foreach (var candidate in new[]
                 {
                     cwd,
                     exeDir ?? "",
                     Path.Combine(cwd, "SmartNarrator.Api"),
                     Path.Combine(cwd, "..", "SmartNarrator.Api"),
                 })
        {
            if (string.IsNullOrEmpty(candidate))
                continue;

            var full = Path.GetFullPath(candidate);
            if (File.Exists(Path.Combine(full, "appsettings.json")))
            {
                apiDir = full;
                break;
            }
        }

        if (apiDir is null)
        {
            throw new InvalidOperationException(
                "Could not locate SmartNarrator.Api appsettings.json for EF design-time. " +
                $"Checked cwd '{cwd}', exe dir '{exeDir}', and sibling SmartNarrator.Api paths.");
        }
        var config = new ConfigurationBuilder()
            .SetBasePath(apiDir)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var options = new DbContextOptionsBuilder<SmartNarratorDbContext>()
            .UseNpgsql(config.GetConnectionString("Default"))
            .Options;

        return new SmartNarratorDbContext(options);
    }
}
