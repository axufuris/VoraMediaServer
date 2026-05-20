using Vora.Api.Endpoints;
using Vora.Api.Hubs;
using Vora.Api.Middleware;

namespace Vora.Api.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication UseVoraPipeline(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "Vora Media Server API v1");
                options.RoutePrefix = string.Empty;
            });
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseCors(ServiceRegistrationExtensions.CorsPolicy);
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<DeviceTrackingMiddleware>();

        return app;
    }

    public static WebApplication MapVoraEndpoints(this WebApplication app)
    {
        app.MapActorEndpoints();
        app.MapAdminEndpoints();
        app.MapAdminNotificationEndpoints();
        app.MapArtworkEndpoints();
        app.MapAuthEndpoints();
        app.MapCalendarEndpoints();
        app.MapCollectionEndpoints();
        app.MapCollectionArtworkEndpoints();
        app.MapDeviceEndpoints();
        app.MapDiscoveryEndpoints();
        app.MapDvrEndpoints();
        app.MapDvrPlaybackEndpoints();
        app.MapFileSystemEndpoints();
        app.MapIptvAdminEndpoints();
        app.MapIptvClientEndpoints();
        app.MapIptvPassthroughEndpoints();
        app.MapLibraryEndpoints();
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
        app.MapStreamingAdminEndpoints();
        app.MapSyncEndpoints();
        app.MapTaskEndpoints();
        app.MapTemplateEndpoints();
        app.MapThemeEndpoints();
        app.MapTimeshiftEndpoints();
        app.MapUserEndpoints();
        app.MapUserImageEndpoints();

        app.MapHub<VoraHub>("/hubs/Vora");
        app.MapFallbackToFile("index.html");

        return app;
    }
}
