using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.FluentUI.AspNetCore.Components;
using MonAlbumPc.Nas;
using MonAlbumPc.Nas.Components;

var options = NasOptions.FromEnvironment();
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<NasHost>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<NasHost>());
builder.Services.AddSingleton(sp => sp.GetRequiredService<NasHost>().Core);
builder.Services.AddSingleton(new AdminPassword(options.ConfigDirectory));

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddFluentUIComponents();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(cookie =>
    {
        cookie.LoginPath = "/login";
        cookie.Cookie.Name = "monalbumpc";
        cookie.ExpireTimeSpan = TimeSpan.FromDays(30);
        cookie.SlidingExpiration = true;
        // A cookie issued before the last password change is rejected.
        cookie.Events.OnValidatePrincipal = context =>
        {
            var current = context.HttpContext.RequestServices.GetRequiredService<AdminPassword>().Stamp;
            if (context.Principal?.FindFirst(StampClaim)?.Value != current)
            {
                context.RejectPrincipal();
            }
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

// Administration over HTTPS with the server certificate (the same one phones pin).
builder.WebHost.ConfigureKestrel((context, kestrel) =>
{
    var host = kestrel.ApplicationServices.GetRequiredService<NasHost>();
    kestrel.Listen(IPAddress.Any, options.AdminPort, listen => listen.UseHttps(host.Core.Certificate));
});

var app = builder.Build();

var password = app.Services.GetRequiredService<AdminPassword>();
if (!password.IsSet && options.InitialAdminPassword is { } initial && password.Set(initial) is { } weak)
{
    app.Logger.LogWarning("MONALBUMPC_ADMIN_PASSWORD ignoré : {Reason}", weak);
}

app.UseExceptionHandler("/Error", createScopeForErrors: true);
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAuthentication();
app.UseAuthorization();

// Everything but the sign-in pages and their assets requires the administrator.
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    var open = path.StartsWithSegments("/login") || path.StartsWithSegments("/setup")
               || path.StartsWithSegments("/_framework") || path.StartsWithSegments("/_content")
               || path.Value is "/app.css" or "/favicon.png" || path.StartsWithSegments("/img");
    if (!open && context.User.Identity?.IsAuthenticated != true)
    {
        context.Response.Redirect(password.IsSet ? "/login" : "/setup");
        return;
    }
    await next();
});

app.UseAntiforgery();
app.MapStaticAssets();

app.MapPost("/login", async (HttpContext http, AdminPassword admin, IAntiforgery antiforgery) =>
{
    if (!await IsGenuineAsync(http, antiforgery))
    {
        return Results.Redirect("/login");
    }
    var form = await http.Request.ReadFormAsync();
    if (!admin.Verify(form["password"].ToString()))
    {
        return Results.Redirect("/login?error=1");
    }
    await SignInAsync(http, admin);
    return Results.Redirect("/");
});

app.MapPost("/setup", async (HttpContext http, AdminPassword admin, IAntiforgery antiforgery) =>
{
    if (admin.IsSet || !await IsGenuineAsync(http, antiforgery))
    {
        return Results.Redirect(admin.IsSet ? "/login" : "/setup");
    }
    var form = await http.Request.ReadFormAsync();
    var first = form["password"].ToString();
    if (first != form["confirm"].ToString())
    {
        return Results.Redirect("/setup?error=mismatch");
    }
    if (admin.Set(first) is not null)
    {
        return Results.Redirect("/setup?error=weak");
    }
    await SignInAsync(http, admin);
    return Results.Redirect("/");
});

app.MapPost("/logout", async (HttpContext http, IAntiforgery antiforgery) =>
{
    if (!await IsGenuineAsync(http, antiforgery))
    {
        return Results.Redirect("/");
    }
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

MediaEndpoints.Map(app, options);

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

static Task SignInAsync(HttpContext http, AdminPassword admin) => http.SignInAsync(
    CookieAuthenticationDefaults.AuthenticationScheme,
    new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, "admin"), new Claim(StampClaim, admin.Stamp ?? "")],
        CookieAuthenticationDefaults.AuthenticationScheme)),
    new AuthenticationProperties { IsPersistent = true });

// The sign-in forms carry an antiforgery token; these endpoints read the form by hand, so check it here.
static async Task<bool> IsGenuineAsync(HttpContext http, IAntiforgery antiforgery)
{
    try
    {
        await antiforgery.ValidateRequestAsync(http);
        return true;
    }
    catch (AntiforgeryValidationException)
    {
        return false;
    }
}

internal partial class Program
{
    private const string StampClaim = "monalbumpc:stamp";
}
