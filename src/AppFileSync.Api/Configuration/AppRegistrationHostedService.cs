using AppFileSync.Api.Data;
using AppFileSync.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AppFileSync.Api.Configuration;

public sealed class AppRegistrationHostedService(
    IServiceProvider services,
    IOptions<AppFileSyncOptions> options,
    ILogger<AppRegistrationHostedService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.MigrateOnStartup && options.Value.Apps.Count == 0)
        {
            return;
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppFileSyncDbContext>();

        if (options.Value.MigrateOnStartup)
        {
            await db.Database.MigrateAsync(cancellationToken);
        }

        foreach (var configuredApp in options.Value.Apps)
        {
            if (string.IsNullOrWhiteSpace(configuredApp.AppId) ||
                string.IsNullOrWhiteSpace(configuredApp.DisplayName))
            {
                logger.LogWarning("Skipping incomplete AppFileSync app registration for {AppId}.", configuredApp.AppId);
                continue;
            }

            var app = await db.Apps.FirstOrDefaultAsync(
                candidate => candidate.AppId == configuredApp.AppId,
                cancellationToken);

            if (app is null)
            {
                app = new RegisteredApp
                {
                    AppId = configuredApp.AppId,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                db.Apps.Add(app);
            }

            app.OidcClientId = string.IsNullOrWhiteSpace(configuredApp.OidcClientId)
                ? configuredApp.AppId
                : configuredApp.OidcClientId;
            app.DisplayName = configuredApp.DisplayName;
            app.MaxPlaintextBytes = configuredApp.MaxPlaintextBytes;
            app.IsEnabled = configuredApp.IsEnabled;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
