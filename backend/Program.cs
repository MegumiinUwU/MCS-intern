using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using MCS_app.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Kestrel sits behind Caddy, which terminates TLS and forwards the original
// scheme/client IP in X-Forwarded-* headers.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Caddy runs on the same machine, so trust the loopback proxy only.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// EF Core + SQL Server
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// CORS so a front end (e.g. running on a different port) can call the API.
const string CorsPolicy = "AllowFrontend";
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Create the database (and apply seed data) on startup if it doesn't exist.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// Configure the HTTP request pipeline.
app.UseForwardedHeaders();

// Swagger stays enabled in all environments so it is reachable through Caddy.
app.UseSwagger();
app.UseSwaggerUI();

// HTTPS redirection is handled by Caddy (the reverse proxy); Kestrel itself
// only listens on plain HTTP on localhost.

app.UseCors(CorsPolicy);

app.UseAuthorization();

app.MapControllers();

app.Run();
