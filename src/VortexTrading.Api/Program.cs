using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.Options;
using VortexTrading.Api.Configuration;
using VortexTrading.Api.Charts;
using VortexTrading.Api.Trading;
using VortexTrading.Application;
using VortexTrading.Infrastructure;
using VortexTrading.Infrastructure.MarketData.Development;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// The simulated trading session is the product itself, so it is registered in every environment.
// Only the demonstration chart dataset stays Development-only, because it is a fixed fixture rather
// than a market.
builder.Services.AddPaperTradingSession(builder.Configuration);
if (builder.Environment.IsDevelopment())
    builder.Services.AddDevelopmentHistoricalCharts();

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

app.UseHttpLogging();
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    SuppressDiagnosticsCallback = _ => false
});
app.UseStatusCodePages();
if (app.Environment.IsDevelopment())
    app.MapHistoricalChartSnapshots();

app.MapTradingEndpoints();

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
