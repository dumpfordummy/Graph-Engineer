using GraphEngineering.Api.Runs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;

namespace GraphEngineering.Api.Notifications;

[Authorize]
public sealed class RunHub : Hub;

public sealed class SignalRRunNotifier(IHubContext<RunHub> hub) : IRunNotifier
{
    public Task PublishAsync(Guid runId, long eventSequence, CancellationToken cancellation) =>
        hub.Clients.All.SendAsync("RunChanged", new { runId, eventSequence }, cancellation);
}

public static class RunNotifications
{
    public static IServiceCollection AddRunNotifications(this IServiceCollection services)
    {
        services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = false;
            options.MaximumReceiveMessageSize = 1024;
        });
        services.AddSingleton<IRunNotifier, SignalRRunNotifier>();
        return services;
    }

    public static void MapRunNotifications(this WebApplication app) =>
        app.MapHub<RunHub>("/hubs/runs", options =>
        {
            options.CloseOnAuthenticationExpiration = true;
            options.Transports = HttpTransportType.WebSockets;
        }).RequireAuthorization();
}
