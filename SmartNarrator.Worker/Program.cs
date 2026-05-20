using SmartNarrator.Infrastructure;
using SmartNarrator.Infrastructure.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSmartNarratorInfrastructure(builder.Configuration, InfrastructureHostRole.Worker);
builder.Services.AddSmartNarratorWorkerSupport(builder.Configuration);

var host = builder.Build();
await host.RunAsync().ConfigureAwait(false);
