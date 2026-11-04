using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Api.Data;
using OrderManagement.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();

// The connection remains open for the lifetime of the application,
// so the SQLite in-memory database is not destroyed between requests.
builder.Services.AddSingleton<SqliteConnection>(_ =>
{
    var connection = new SqliteConnection("Data Source=:memory:");
    connection.Open();
    return connection;
});

builder.Services.AddDbContext<OrderDbContext>((services, options) =>
    options.UseSqlite(
        services.GetRequiredService<SqliteConnection>()));

builder.Services.AddScoped<OrderService>();

var app = builder.Build();

if (!app.Environment.IsProduction())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<OrderDbContext>();

    db.Database.EnsureCreated();
}

app.MapControllers();
app.Run();
