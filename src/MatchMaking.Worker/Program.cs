using MassTransit;
using MatchMaking.Contracts;
using MatchMaking.Worker;
using MatchMaking.Worker.Options;
using MatchMaking.Worker.Workers;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.Configure<AssemblerOptions>(builder.Configuration.GetSection("Assembler"));
builder.Services.Configure<OutboxerOptions>(builder.Configuration.GetSection("Outboxer"));

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration["Redis:ConnectionString"]!));

builder.Services.AddMassTransit(x =>
{
    x.UsingInMemory((ctx, cfg) => cfg.ConfigureEndpoints(ctx));

    x.AddRider(r =>
    {
        r.AddConsumer<Consumer>();
        r.AddProducer<MatchmakingCompleteMessage>(
            builder.Configuration["Kafka:MatchmakingCompleteTopic"]!);

        r.UsingKafka((ctx, k) =>
        {
            k.Host(builder.Configuration["Kafka:Brokers"]);

            k.TopicEndpoint<MatchmakingRequestMessage>(
                builder.Configuration["Kafka:MatchmakingRequestTopic"]!,
                builder.Configuration["Kafka:MatchmakingRequestGroup"]!,
                e =>
                {
                    e.ConfigureConsumer<Consumer>(ctx);
                    e.CreateIfMissing();
                });
        });
    });
});

builder.Services.AddHostedService<Assembler>();
builder.Services.AddHostedService<Outboxer>();

var host = builder.Build();
await host.RunAsync();