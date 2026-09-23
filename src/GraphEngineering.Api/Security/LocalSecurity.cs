using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace GraphEngineering.Api.Security;

public static class LocalSecurity
{
    private const string Scheme = "LocalBrowser";

    public static IServiceCollection AddLocalSecurity(this IServiceCollection services)
    {
        services.AddSingleton<LocalLaunch>();
        services.AddDataProtection().SetApplicationName("GraphEngineering.Local");
        services.AddOptions<KeyManagementOptions>().Configure<IConfiguration, ILoggerFactory>((options, configuration, logger) =>
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows protected storage is required.");
            var directory = LocalLaunch.RestrictedDirectory(Path.Combine(LocalLaunch.DataDirectory(configuration), "session-keys"));
            options.XmlRepository = new FileSystemXmlRepository(directory, logger);
            options.XmlEncryptor = new DpapiXmlEncryptor(protectToLocalMachine: false, logger);
        });
        services.AddAuthentication(Scheme).AddCookie(Scheme, options =>
        {
            options.Cookie.Name = "GE.LocalSession";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = false;
            options.Events.OnValidatePrincipal = context =>
            {
                if (context.Principal?.FindFirstValue("launch") != context.HttpContext.RequestServices.GetRequiredService<LocalLaunch>().Id)
                    context.RejectPrincipal();
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToLogin = context => Failure(context.HttpContext, 401, "session_required", "Pair this browser with the running local application.");
            options.Events.OnRedirectToAccessDenied = context => Failure(context.HttpContext, 403, "security_rejected", "The local request was rejected.");
        });
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-GE-CSRF";
            options.Cookie.Name = "GE.Antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });
        return services;
    }

    public static IApplicationBuilder UseLocalSecurity(this IApplicationBuilder app)
    {
        app.UseAuthentication();
        return app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api")) { await next(context); return; }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            var launch = context.RequestServices.GetRequiredService<LocalLaunch>();
            if (!launch.Hosts.Contains(context.Request.Host.Value ?? ""))
            { await Failure(context, 403, "host_rejected", "Use the configured local application address."); return; }
            var origin = context.Request.Headers.Origin;
            if (origin.Count > 1 || origin.Count == 1 && !launch.Origins.Contains(origin.ToString()))
            { await Failure(context, 403, "origin_rejected", "This browser origin is not allowed."); return; }
            var safe = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);
            var path = context.Request.Path.Value;
            var bootstrap = safe && path is "/api/health" or "/api/session" ||
                HttpMethods.IsPost(context.Request.Method) && path == "/api/session/pair";
            if (!safe)
            {
                if (origin.Count != 1)
                { await Failure(context, 403, "origin_required", "A configured browser origin is required."); return; }
                if (!context.Request.HasJsonContentType())
                { await Failure(context, 415, "json_required", "Use application/json for local state changes."); return; }
            }
            if (!bootstrap && context.User.Identity?.IsAuthenticated != true)
            { await Failure(context, 401, "session_required", "Pair this browser with the running local application. Your unsaved changes can be kept."); return; }
            if (!bootstrap && context.Request.Headers["Sec-Fetch-Site"] == "cross-site")
            { await Failure(context, 403, "origin_rejected", "Cross-site application requests are not allowed."); return; }
            if (!safe && !bootstrap)
            {
                try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
                catch (AntiforgeryValidationException)
                { await Failure(context, 403, "antiforgery_rejected", "The local session needs pairing again. The action was not repeated."); return; }
            }
            await next(context);
        });
    }

    public static void MapLocalSession(this WebApplication app)
    {
        app.MapGet("/api/session", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var authenticated = context.User.Identity?.IsAuthenticated == true;
            return Results.Ok(new { authenticated, csrfToken = authenticated ? antiforgery.GetAndStoreTokens(context).RequestToken : null });
        });
        app.MapPost("/api/session/pair", async (HttpContext context, LocalLaunch launch) =>
        {
            if (!launch.AllowPairingAttempt())
            {
                context.Response.Headers.RetryAfter = "60";
                await Failure(context, 429, "pairing_limited", "Too many pairing attempts. Wait one minute and try again.");
                return;
            }
            if (context.Request.ContentLength > 1024)
            { await Failure(context, 413, "pairing_too_large", "Pairing input is too large."); return; }
            try
            {
                // Also bounds chunked requests; token parsing never logs supplied input.
                var buffer = new byte[1025];
                var count = 0;
                while (count < buffer.Length)
                {
                    var read = await context.Request.Body.ReadAsync(buffer.AsMemory(count), context.RequestAborted);
                    if (read == 0) break;
                    count += read;
                }
                if (count > 1024)
                { await Failure(context, 413, "pairing_too_large", "Pairing input is too large."); return; }
                using var json = JsonDocument.Parse(buffer.AsMemory(0, count));
                var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 ||
                    !root.TryGetProperty("token", out var token) || token.ValueKind != JsonValueKind.String || !launch.Matches(token.GetString()!))
                { await Failure(context, 401, "pairing_rejected", "Pairing token was not accepted. Open the current launch's local pairing file."); return; }
            }
            catch (JsonException)
            { await Failure(context, 400, "pairing_invalid", "Enter the pairing token in the local pairing screen."); return; }
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, launch.Id), new Claim("launch", launch.Id)
            ], Scheme));
            await context.SignInAsync(Scheme, principal, new AuthenticationProperties { IsPersistent = false });
            await context.Response.WriteAsJsonAsync(new { paired = true });
        });
        app.MapPost("/api/session/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(Scheme);
            return Results.NoContent();
        });
    }

    private static Task Failure(HttpContext context, int status, string code, string detail) =>
        Results.Problem(statusCode: status, title: "Local request rejected", detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code }).ExecuteAsync(context);
}
