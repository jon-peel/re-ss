var builder = DistributedApplication.CreateBuilder(args);

// Add the ReSS web application as an Aspire resource.
// Aspire automatically injects OTEL_EXPORTER_OTLP_ENDPOINT and related env vars
// so that the ReSS app ships traces and metrics to the Aspire dashboard.
builder.AddProject<Projects.ReSS>("ress");

builder.Build().Run();
