using System.Net;
using GraphEngineering.Api.Http;
using GraphEngineering.Api.Persistence;
using GraphEngineering.Api.Providers;
using GraphEngineering.Api.Security;
using GraphEngineering.Api.Runs;
using GraphEngineering.Api.Notifications;
using GraphEngineering.Core.Documents;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
// A local user must be able to report failures without Windows Event Log write privileges.
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
var urls = builder.Configuration["urls"] ?? "http://127.0.0.1:5080";
foreach (var url in urls.Split(';', StringSplitOptions.RemoveEmptyEntries))
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
        !(uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip)))
        throw new InvalidOperationException("Graph Engineering must bind to a loopback address.");
}
builder.WebHost.UseUrls(urls);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = DocumentJson.MaximumBytes);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = false;
});
builder.Services.AddProblemDetails();
builder.Services.AddLocalSecurity();
builder.Services.AddProviders();
builder.Services.AddRuns();
builder.Services.AddRunNotifications();
builder.Services.AddDbContext<WorkflowDbContext>((services, options) =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var dataDirectory = configuration["GRAPH_ENGINEERING_DATA_DIR"] ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GraphEngineering");
    Directory.CreateDirectory(dataDirectory);
    var connection = new SqliteConnectionStringBuilder
    {
        DataSource = Path.Combine(Path.GetFullPath(dataDirectory), "workflows.db"),
        ForeignKeys = true,
        DefaultTimeout = 30
    }.ToString();
    options.UseSqlite(connection);
});
await using var app = builder.Build();
try
{
var launch = app.Services.GetRequiredService<LocalLaunch>();
app.Logger.LogInformation("Pair this browser using the local file {PairingFile}. Its contents must not be shared or logged.", launch.TokenPath);
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var result = error switch
    {
        BadHttpRequestException { StatusCode: 413 } => Problems.Create(413, "Payload too large", "The maximum request size is 1 MiB."),
        SqliteException or DbUpdateException => Problems.Create(503, "Persistence unavailable", "The workflow database could not complete this request. Your changes have not been acknowledged as saved."),
        _ => Problems.Create(500, "Request failed", "The server could not complete this request. Your changes have not been acknowledged as saved.")
    };
    await result.ExecuteAsync(context);
}));
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
    await db.Database.MigrateAsync();
}
await app.Services.RecoverRunsAsync();
app.UseLocalSecurity();
app.UseAuthorization();
app.MapLocalSession();
app.MapWorkflowEndpoints();
app.MapProviderEndpoints();
app.MapRunEndpoints();
app.MapRunNotifications();
app.Run();
}
catch (Exception error)
{
    // Expected local startup failures must exit, rather than enter a Windows
    // unhandled-exception dialog while retaining ownership or listener handles.
    var detail = error is IOException && error.Message == "Another Graph Engineering backend owns this data directory."
        ? error.Message : $"Check the local data-directory access, migrations and configured loopback ports ({error.GetType().Name}).";
    Console.Error.WriteLine($"Local startup failed. {detail}");
    Environment.ExitCode = 1;
}

public partial class Program;
