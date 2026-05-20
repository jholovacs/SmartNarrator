using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using SmartNarrator.Application.Ports;
using SmartNarrator.Api.Exceptions;
using SmartNarrator.Api.Hubs;
using SmartNarrator.Api.Options;
using SmartNarrator.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection(ApiOptions.SectionName));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSmartNarratorInfrastructure(builder.Configuration);

builder.Services.AddSingleton<IJobRealtimeNotifier, SmartNarrator.Api.Services.JobRealtimeNotifier>();
builder.Services.AddSignalR().AddJsonProtocol(o =>
{
    o.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false));
});

builder.Services.AddCors(opts =>
{
    opts.AddPolicy("DevSpa",
        policy => policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials()
            .SetIsOriginAllowed(_ => true));
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

app.UseExceptionHandler();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<SmartNarrator.Infrastructure.Persistence.SmartNarratorDbContext>();
    await dbContext.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseRouting();
app.UseCors("DevSpa");

app.UseAuthorization();

app.MapControllers();

app.MapHub<JobsHub>("/hubs/jobs").RequireCors("DevSpa");

app.MapGet("/health", () => Results.Ok(new { ok = true })).AllowAnonymous();

app.Run();
