using Microsoft.EntityFrameworkCore;
using VoltGrid.Gateway;
using VoltGrid.Gateway.Handlers;
using VoltGrid.Infrastructure.Persistence;
using VoltGrid.Application.Authorization;
using VoltGrid.Infrastructure.Persistence.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

var connectionString = builder.Configuration.GetConnectionString("Postgres") 
                       ?? throw new InvalidOperationException("Connection string 'Postgres' not found");
builder.Services.AddDbContextFactory<VoltGridDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddSingleton<BootNotificationHandler>();
builder.Services.AddSingleton<HeartbeatHandler>();
builder.Services.AddSingleton<StatusNotificationHandler>();
builder.Services.AddSingleton<IRfidTagRepository, RfidTagRepository>();

var app = builder.Build();

app.MapGrpcService<StationLinkService>();

app.Run();