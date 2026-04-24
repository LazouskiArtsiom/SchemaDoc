using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SchemaDoc.Core.Interfaces;
using SchemaDoc.Core.Services;
using SchemaDoc.Extraction;
using SchemaDoc.Infrastructure.Data;
using SchemaDoc.Infrastructure.Repositories;
using SchemaDoc.Web.Components;
using SchemaDoc.Export;
using SchemaDoc.Web.Services;

// Register Azure AD (Microsoft Entra) authentication providers explicitly.
// The Microsoft.Data.SqlClient.Extensions.Azure package registers these via a
// module initializer, but that can be trimmed out in single-file self-contained
// publishes. Registering here guarantees MFA, Managed Identity, Default, etc.
// all work after publish.
var entraProvider = new ActiveDirectoryAuthenticationProvider();
SqlAuthenticationProvider.SetProvider(SqlAuthenticationMethod.ActiveDirectoryInteractive,       entraProvider);
#pragma warning disable CS0618 // ActiveDirectoryPassword is deprecated but still widely used in legacy tools.
SqlAuthenticationProvider.SetProvider(SqlAuthenticationMethod.ActiveDirectoryPassword,          entraProvider);
#pragma warning restore CS0618
SqlAuthenticationProvider.SetProvider(SqlAuthenticationMethod.ActiveDirectoryIntegrated,        entraProvider);
SqlAuthenticationProvider.SetProvider(SqlAuthenticationMethod.ActiveDirectoryServicePrincipal,  entraProvider);
SqlAuthenticationProvider.SetProvider(SqlAuthenticationMethod.ActiveDirectoryManagedIdentity,   entraProvider);
SqlAuthenticationProvider.SetProvider(SqlAuthenticationMethod.ActiveDirectoryDefault,           entraProvider);
SqlAuthenticationProvider.SetProvider(SqlAuthenticationMethod.ActiveDirectoryDeviceCodeFlow,    entraProvider);

var builder = WebApplication.CreateBuilder(args);

// In self-contained desktop mode bind to HTTP only (no dev cert needed)
if (!builder.Environment.IsDevelopment())
{
    builder.WebHost.UseUrls("http://localhost:5000");
}

// Blazor
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// SQLite database
var dbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "SchemaDoc", "schemadoc.db");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Repositories
builder.Services.AddScoped<IConnectionRepository, ConnectionRepository>();
builder.Services.AddScoped<IAnnotationRepository, AnnotationRepository>();
builder.Services.AddScoped<ISchemaSnapshotRepository, SchemaSnapshotRepository>();

// Extraction
builder.Services.AddSingleton<ExtractorFactory>();

// Export
builder.Services.AddSingleton<IPdfExporter, PdfExporter>();

// App services
builder.Services.AddScoped<SchemaLoaderService>();
builder.Services.AddScoped<SchemaSessionState>();
builder.Services.AddSingleton<SchemaDiffService>();

var app = builder.Build();

// Auto-migrate on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);

    // Auto-open browser when launched as a desktop app
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        try
        {
            Process.Start(new ProcessStartInfo("http://localhost:5000")
            {
                UseShellExecute = true
            });
        }
        catch { /* silently ignore if browser fails to open */ }
    });
}
else
{
    app.UseHttpsRedirection();
}
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
