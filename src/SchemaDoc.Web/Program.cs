using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using SchemaDoc.Core.Interfaces;
using SchemaDoc.Core.Services;
using SchemaDoc.Extraction;
using SchemaDoc.Infrastructure.Data;
using SchemaDoc.Infrastructure.Repositories;
using SchemaDoc.Web.Components;
using SchemaDoc.Export;
using SchemaDoc.Web.Services;

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
// Serve wwwroot files from embedded assembly resources (enables single-file exe distribution)
var embeddedProvider = new EmbeddedFileProvider(
    typeof(Program).Assembly,
    "SchemaDoc.Web.wwwroot");
app.UseStaticFiles(new StaticFileOptions { FileProvider = embeddedProvider });

app.UseAntiforgery();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
