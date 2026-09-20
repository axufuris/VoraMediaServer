using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Vora.Api.Endpoints;
using Vora.Api.Hubs;
using Vora.Api.Middleware;
using Vora.Application.Settings;

namespace Vora.Api.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication UseVoraPipeline(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "Vora Media Server API v1");
                options.RoutePrefix = "swagger";
                options.DocumentTitle = "Vora Media Server API";
            });
        }

        app.UseVoraForwardedHeaders();

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        // Only redirect when an HTTPS port actually exists to redirect to.
        // The container listens on HTTP alone and terminates TLS at a reverse
        // proxy, so the middleware could never do anything there — it just
        // logged "Failed to determine the https port for redirect" on the
        // first request of every deployment. HSTS above is unaffected: its
        // middleware already skips non-HTTPS requests, so a plain-HTTP LAN
        // install is never told to upgrade.
        if (!string.IsNullOrWhiteSpace(app.Configuration["HTTPS_PORT"])
            || !string.IsNullOrWhiteSpace(app.Configuration["ASPNETCORE_HTTPS_PORTS"]))
        {
            app.UseHttpsRedirection();
        }
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseResponseCompression();
        app.UseStaticFiles();
        app.UseCors(ServiceRegistrationExtensions.CorsPolicy);
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<DeviceTrackingMiddleware>();

        return app;
    }

    private static WebApplication UseVoraForwardedHeaders(this WebApplication app)
    {
        var config = app.Services.GetRequiredService<IOptions<ForwardedHeadersConfigOptions>>().Value;
        if (!config.Enabled)
        {
            return app;
        }

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = Math.Max(1, config.ForwardLimit)
        };

        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var proxy in config.KnownProxies)
        {
            if (IPAddress.TryParse(proxy, out var ip))
            {
                options.KnownProxies.Add(ip);
            }
        }

        foreach (var network in config.KnownNetworks)
        {
            var parts = network.Split('/', 2);
            if (parts.Length == 2 && IPAddress.TryParse(parts[0], out var prefix) && int.TryParse(parts[1], out var prefixLength))
            {
                options.KnownIPNetworks.Add(new System.Net.IPNetwork(prefix, prefixLength));
            }
        }

        app.UseForwardedHeaders(options);
        return app;
    }

    public static WebApplication MapVoraEndpoints(this WebApplication app)
    {
        app.MapActorEndpoints();
        app.MapAdminEndpoints();
        app.MapBackupEndpoints();
        app.MapAdminNotificationEndpoints();
        app.MapArtworkEndpoints();
        app.MapAuthEndpoints();
        app.MapCalendarEndpoints();
        app.MapCollectionEndpoints();
        app.MapCollectionArtworkEndpoints();
        app.MapDeviceEndpoints();
        app.MapDiscoveryEndpoints();
        app.MapWatchlistEndpoints();
        app.MapDvrEndpoints();
        app.MapDvrPlaybackEndpoints();
        app.MapEmailEndpoints();
        app.MapFileSystemEndpoints();
        app.MapIptvAdminEndpoints();
        app.MapIptvClientEndpoints();
        app.MapIptvPassthroughEndpoints();
        app.MapLibraryEndpoints();
        app.MapLibraryMigrationEndpoints();
        app.MapLogEndpoints();
        app.MapMediaEndpoints();
        app.MapMusicEndpoints();
        app.MapOverlayTemplateEndpoints();
        app.MapPlaylistEndpoints();
        app.MapPluginEndpoints();
        app.MapPodcastEndpoints();
        app.MapProfileEndpoints();
        app.MapProviderEndpoints();
        app.MapRecommendationEndpoints();
        app.MapRemoteAccessEndpoints();
        app.MapRequestEndpoints();
        app.MapSearchEndpoints();
        app.MapSettingsEndpoints();
        app.MapSmartListEndpoints();
        app.MapSmartPlaylistEndpoints();
        app.MapStreamingEndpoints();
        app.MapSubtitleSearchEndpoints();
        app.MapStreamingAdminEndpoints();
        app.MapSyncEndpoints();
        app.MapTaskEndpoints();
        app.MapTemplateEndpoints();
        app.MapThemeEndpoints();
        app.MapSystemEndpoints();
        app.MapTimeshiftEndpoints();
        app.MapUserEndpoints();
        app.MapUserImageEndpoints();
        app.MapVideoThumbnailEndpoints();

        app.MapHub<VoraHub>("/hubs/Vora").RequireAuthorization();
        app.MapHealthChecks("/health").AllowAnonymous();
        app.MapFallbackToFile("index.html");

        return app;
    }
}
