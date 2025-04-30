var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.sample_API>("sample");

builder.Build().Run();
