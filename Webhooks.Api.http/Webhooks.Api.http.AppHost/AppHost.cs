var builder = DistributedApplication.CreateBuilder(args);


var database = builder.AddPostgres("postgres")
                      .WithDataVolume("webhooks-data")
                      .WithPgAdmin(pg => pg.WithHostPort(5050))
                      .AddDatabase("webhooks");

var queue = builder.AddRabbitMQ("rabbitmq")
                   .WithDataVolume("redis-data")
                   .WithManagementPlugin();

builder.AddProject<Projects.Webhooks_Api_http>("webhooks-api-http")
       .WithReference(database)
       .WithReference(queue)
       .WaitFor(database)
       .WaitFor(queue);

builder.Build().Run();
