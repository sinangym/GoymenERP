using System.Text.Json;
using GoymenERP.Api.Security;
using GoymenERP.Api.Startup;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Identity.Application.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// ID-2 fence and operator command without a host: a session flagged <c>PasswordChangeRequired</c> reaches only the
/// endpoints marked <see cref="LocalAccountPasswordChangeFence.Allowed"/>; every other request is the 403
/// <c>identity.password_change_required</c> problem. The <c>identity bootstrap-founder</c> command accepts exactly two
/// arguments and needs the LocalAccount mode. SENTETIK.
/// </summary>
public sealed class LocalAccountPasswordChangeFenceTests
{
    [Fact]
    public void A_request_without_the_flag_is_never_fenced()
    {
        var httpContext = Context(flagged: false, allowed: false);

        Assert.False(LocalAccountPasswordChangeFence.IsFenced(httpContext));
    }

    [Fact]
    public void A_flagged_request_is_fenced_unless_its_endpoint_is_allowed()
    {
        Assert.True(LocalAccountPasswordChangeFence.IsFenced(Context(flagged: true, allowed: false)));
        Assert.False(LocalAccountPasswordChangeFence.IsFenced(Context(flagged: true, allowed: true)));
    }

    [Fact]
    public async Task The_fence_answers_403_with_the_password_change_code_and_does_not_call_the_next_handler()
    {
        var httpContext = Context(flagged: true, allowed: false);
        httpContext.Response.Body = new MemoryStream();
        var nextCalled = false;
        var middleware = new LocalAccountPasswordChangeFence.Middleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(httpContext);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, httpContext.Response.StatusCode);
        Assert.Equal("private, no-store", httpContext.Response.Headers.CacheControl.ToString());
        httpContext.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(httpContext.Response.Body);
        Assert.Equal(
            IdentitySessionProblemCodesV1.PasswordChangeRequired,
            document.RootElement.GetProperty("code").GetString());
        Assert.Equal(CorrelationId, document.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task An_allowed_endpoint_passes_through_the_fence()
    {
        var httpContext = Context(flagged: true, allowed: true);
        var nextCalled = false;
        var middleware = new LocalAccountPasswordChangeFence.Middleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(httpContext);

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData(new object[] { new[] { "identity" } })]
    [InlineData(new object[] { new[] { "identity", "bootstrap" } })]
    [InlineData(new object[] { new[] { "identity", "bootstrap-founder", "fazla" } })]
    public async Task The_command_refuses_any_other_argument_list_with_the_usage_exit_code(string[] args)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await IdentityCommandLine.RunAsync(
            services, args, new NeverReadConsole(), output, error, CancellationToken.None);

        Assert.Equal(IdentityCommandLine.ExitUsage, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains(FounderBootstrapTexts.Usage, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_command_refuses_outside_the_local_account_mode_without_running_the_bootstrap()
    {
        var bootstrap = new RecordingBootstrapService(FounderBootstrapResult.Of(FounderBootstrapStatus.Completed));
        var services = new ServiceCollection()
            .AddScoped<ICorrelationContext, CorrelationContext>()
            .AddSingleton<IFounderBootstrapService>(bootstrap)
            .BuildServiceProvider();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await IdentityCommandLine.RunAsync(
            services,
            [IdentityCommandLine.CommandGroup, IdentityCommandLine.BootstrapFounderCommand],
            new NeverReadConsole(),
            output,
            error,
            CancellationToken.None);

        Assert.Equal(IdentityCommandLine.ExitRefused, exitCode);
        Assert.Equal(0, bootstrap.Calls);
        Assert.Contains(FounderBootstrapTexts.LocalAccountModeRequired, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_command_maps_completion_to_exit_0_and_a_refusal_to_exit_1()
    {
        var founderUserId = Guid.NewGuid();
        var completed = await RunBootstrapAsync(
            new FounderBootstrapResult(FounderBootstrapStatus.Completed, founderUserId, [Guid.NewGuid()], []));
        var refused = await RunBootstrapAsync(FounderBootstrapResult.Of(FounderBootstrapStatus.AlreadyBootstrapped));

        Assert.Equal(IdentityCommandLine.ExitCompleted, completed.ExitCode);
        Assert.Contains(founderUserId.ToString("D"), completed.Output, StringComparison.Ordinal);
        Assert.Equal(IdentityCommandLine.ExitRefused, refused.ExitCode);
        Assert.Equal(string.Empty, refused.Output);
        Assert.Contains(
            FounderBootstrapTexts.Refusal(FounderBootstrapStatus.AlreadyBootstrapped),
            refused.Error,
            StringComparison.Ordinal);
    }

    private const string CorrelationId = "fence-korelasyon-sentetik";

    private static async Task<(int ExitCode, string Output, string Error)> RunBootstrapAsync(FounderBootstrapResult result)
    {
        var bootstrap = new RecordingBootstrapService(result);
        var services = new ServiceCollection()
            .AddScoped<ICorrelationContext, CorrelationContext>()
            .AddSingleton<IFounderBootstrapService>(bootstrap)
            .AddSingleton(new LocalAccountAntiforgery.EnabledMarker())
            .BuildServiceProvider();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await IdentityCommandLine.RunAsync(
            services,
            [IdentityCommandLine.CommandGroup, IdentityCommandLine.BootstrapFounderCommand],
            new NeverReadConsole(),
            output,
            error,
            CancellationToken.None);

        Assert.Equal(1, bootstrap.Calls);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static DefaultHttpContext Context(bool flagged, bool allowed)
    {
        var correlation = new CorrelationContext();
        correlation.Set(CorrelationId, causationId: null, SourceChannels.System, clientIpAddress: null, userAgent: null);
        var services = new ServiceCollection()
            .AddSingleton<ICorrelationContext>(correlation)
            .AddLogging()
            .AddProblemDetails()
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        if (flagged)
        {
            httpContext.Items[LocalAccountSession.PasswordChangeRequiredItem] = true;
        }

        httpContext.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            allowed
                ? new EndpointMetadataCollection(LocalAccountPasswordChangeFence.Allowed)
                : EndpointMetadataCollection.Empty,
            "sentetik-uc"));
        return httpContext;
    }

    private sealed class RecordingBootstrapService(FounderBootstrapResult result) : IFounderBootstrapService
    {
        public int Calls { get; private set; }

        public Task<FounderBootstrapResult> RunAsync(IFounderBootstrapConsole console, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    /// <summary>The command itself never reads the terminal; the bootstrap service does.</summary>
    private sealed class NeverReadConsole : IFounderBootstrapConsole
    {
        public bool IsInteractive => true;

        public string? ReadUserName() => throw new InvalidOperationException();

        public string? ReadDisplayName() => throw new InvalidOperationException();

        public string? ReadNewPassword() => throw new InvalidOperationException();

        public string? ReadNewPasswordConfirmation() => throw new InvalidOperationException();
        public FounderRecoveryOperatorDeclaration? ReadOperatorDeclaration() => throw new InvalidOperationException();
    }
}
