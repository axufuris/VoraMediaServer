using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Vora.Api.Middleware;

namespace Vora.Api.Tests;

public class GlobalExceptionHandlerTests
{
    private readonly IProblemDetailsService _problemDetails = Substitute.For<IProblemDetailsService>();
    private readonly ILogger<VoraGlobalExceptionHandler> _logger = Substitute.For<ILogger<VoraGlobalExceptionHandler>>();

    private async Task<(int Status, ProblemDetailsContext Written)> HandleAsync(Exception exception)
    {
        ProblemDetailsContext? written = null;
        _problemDetails.TryWriteAsync(Arg.Do<ProblemDetailsContext>(context => written = context)).Returns(true);
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns("Production");
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/media/1";

        var handled = await new VoraGlobalExceptionHandler(_problemDetails, environment, _logger)
            .TryHandleAsync(context, exception, TestContext.Current.CancellationToken);

        handled.Should().BeTrue();
        return (context.Response.StatusCode, written!);
    }

    private LogLevel LoggedLevel() => (LogLevel)_logger.ReceivedCalls().Single().GetArguments()[0]!;

    [Fact]
    public async Task A_folder_the_server_cannot_write_to_is_a_server_error_not_a_refusal()
    {
        var denied = new UnauthorizedAccessException(
            "Access to the path '/app/video-thumbnails' is denied.", new IOException("Permission denied"));

        var (status, written) = await HandleAsync(denied);

        status.Should().Be(StatusCodes.Status500InternalServerError);
        written.ProblemDetails.Detail.Should().NotContain("/app/video-thumbnails");
        LoggedLevel().Should().Be(LogLevel.Error);
    }

    [Fact]
    public async Task A_media_server_Vora_cannot_reach_is_a_bad_gateway_that_keeps_its_explanation()
    {
        var unreachable = new Vora.Plugins.Dtos.RemoteServerUnreachableException(
            "Vora couldn't reach the Plex server \"Home\". Tried the local address 192.168.0.10:32400 (connection refused).");

        var (status, written) = await HandleAsync(unreachable);

        status.Should().Be(StatusCodes.Status502BadGateway);
        written.ProblemDetails.Detail.Should().Be(unreachable.Message);
    }

    [Fact]
    public async Task A_caller_who_is_not_allowed_is_still_refused()
    {
        var (status, written) = await HandleAsync(new UnauthorizedAccessException("You can't edit this playlist."));

        status.Should().Be(StatusCodes.Status403Forbidden);
        written.ProblemDetails.Detail.Should().Be("You can't edit this playlist.");
        LoggedLevel().Should().Be(LogLevel.Warning);
    }
}
