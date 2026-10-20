using System.Net;
using System.Security.Claims;
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

app.MapPost("/login", async (HttpContext http, AdminPassword admin) =>
{
    var form = await http.Request.ReadFormAsync();
    if (!admin.Verify(form["password"].ToString()))
    {
        return Results.Redirect("/login?error=1");
    }
    await SignInAsync(http);
    return Results.Redirect("/");
});

app.MapPost("/setup", async (HttpContext http, AdminPassword admin) =>
{
    if (admin.IsSet)
    {
        return Results.Redirect("/login");
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
    await SignInAsync(http);
    return Results.Redirect("/");
});

app.MapPost("/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

MediaEndpoints.Map(app, options);

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

static Task SignInAsync(HttpContext http) => http.SignInAsync(
    CookieAuthenticationDefaults.AuthenticationScheme,
    new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], CookieAuthenticationDefaults.AuthenticationScheme)),
    new AuthenticationProperties { IsPersistent = true });
