var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("cache")
            .WithLifetime(ContainerLifetime.Persistent);

var api = builder.AddProject<Projects.secure_resource>("api")
            .WithReference(cache)
            .WaitFor(cache);

var web = builder.AddProject<Projects.acme_org>("web")
            .WithReference(api)
            .WaitFor(api);

builder.Build().Run();
