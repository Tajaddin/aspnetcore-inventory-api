using InventoryApi.Data;
using InventoryApi.Endpoints;
using InventoryApi.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// RFC 7807 problem details for all error responses.
builder.Services.AddProblemDetails();

// EF Core. Provider is chosen by DB_PROVIDER (postgres|sqlite); default
// postgres for production. The sqlite provider powers a zero-setup demo and
// the load test with no Docker. The test host overrides this with its own
// SQLite registration.
var provider = (Environment.GetEnvironmentVariable("DB_PROVIDER") ?? "postgres").ToLowerInvariant();
if (provider == "sqlite")
{
    var sqlitePath = Environment.GetEnvironmentVariable("SQLITE_PATH") ?? "inventory.db";
    builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={sqlitePath}"));
}
else
{
    var connectionString = builder.Configuration.GetConnectionString("Default")
        ?? Environment.GetEnvironmentVariable("DATABASE_URL")
        ?? "Host=localhost;Port=5432;Database=inventory;Username=inventory;Password=inventory";
    builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
}
builder.Services.AddScoped<ProductService>();

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Initialize the database on startup. Postgres runs EF migrations; SQLite
// (demo/load) creates the schema from the model. The Testing host skips this
// and sets up its own SQLite connection in the fixture.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (provider == "sqlite")
    {
        db.Database.EnsureCreated();
    }
    else
    {
        db.Database.Migrate();
    }
}

app.MapHealthChecks("/healthz");
app.MapProductEndpoints();

app.Run();

// Exposed so the test project's WebApplicationFactory<Program> can boot the host.
public partial class Program;
