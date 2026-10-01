using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.Options;
using VortexTrading.Api.Configuration;
using VortexTrading.Application;
using VortexTrading.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

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

builder.Services.AddHealthChecks();
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

app.MapHealthChecks("/health")
    .WithMetadata(new HttpMethodMetadata([HttpMethods.Get]));

var applicationName = app.Services
    .GetRequiredService<IOptions<VortexTradingOptions>>().Value.ApplicationName;
app.Logger.LogInformation("Starting {ApplicationName}.", applicationName);

app.Run();
