using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using VortexTrading.Api.Configuration;
using VortexTrading.Api.Charts;
using VortexTrading.Api.Market;
using VortexTrading.Api.Reports;
using VortexTrading.Api.Trading;
using VortexTrading.Application;
using VortexTrading.Infrastructure;
using VortexTrading.Infrastructure.MarketData.Development;
using VortexTrading.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// The paper session is the product itself, so it is registered in every environment.
// Only the demonstration chart dataset stays Development-only, because it is a fixed fixture rather
// than a market.
builder.Services.AddPaperTradingSession(builder.Configuration);
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddDevelopmentHistoricalCharts();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Vortex Trading Terminal",
            Version = "v1",
            Description = "Pregão da B3 pelas cotações gratuitas do Yahoo Finance. A conta de demonstração é compartilhada."
        });
    });
}

builder.Services
    .AddOptions<VortexTradingOptions>()
    .BindConfiguration(VortexTradingOptions.SectionName)
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.ApplicationName),
        "VortexTrading:ApplicationName must not be empty or whitespace.")
    .ValidateOnStart();

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Detail = null;
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    };
});

builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.RequestMethod
        | HttpLoggingFields.RequestPath
        | HttpLoggingFields.ResponseStatusCode
        | HttpLoggingFields.Duration;
    options.CombineLogs = true;
});

var app = builder.Build();

if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<VortexTradingDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
    return;
}

app.UseHttpLogging();
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    SuppressDiagnosticsCallback = _ => false
});
app.UseStatusCodePages();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Vortex Trading Terminal");
        options.DocumentTitle = "Vortex Trading Terminal";
    });
    app.MapHistoricalChartSnapshots();
}

app.MapTradingEndpoints();
app.MapMarketQuotesEndpoints();
app.MapYahooHistoryEndpoints();
app.MapReportEndpoints();
app.MapTerminalEndpoints();

app.MapHealthChecks("/health")
    .WithMetadata(new HttpMethodMetadata([HttpMethods.Get]));

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
})
    .WithMetadata(new HttpMethodMetadata([HttpMethods.Get]));

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
})
    .WithMetadata(new HttpMethodMetadata([HttpMethods.Get]));

var applicationName = app.Services
    .GetRequiredService<IOptions<VortexTradingOptions>>().Value.ApplicationName;
app.Logger.LogInformation("Starting {ApplicationName}.", applicationName);

app.Run();
