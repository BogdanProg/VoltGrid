using Microsoft.EntityFrameworkCore;
using VoltGrid.Gateway;
using VoltGrid.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

var connectionString = builder.Configuration.GetConnectionString("Postgres") 
                       ?? throw new InvalidOperationException("Connection string 'Postgres' not found");
builder.Services.AddDbContextFactory<VoltGridDbContext>(options =>
    options.UseNpgsql(connectionString));


var app = builder.Build();

app.MapGrpcService<StationLinkService>();

app.Run();
