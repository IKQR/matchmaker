using MassTransit;
using MatchMaking.Contracts;
using MatchMaking.Service.Cache;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Caching.Distributed;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["Redis:ConnectionString"];
});

builder.Services
    .AddSingleton<IOutputCacheStore, RedisOutputCacheStore>()
    .AddOutputCache(opt =>
    {
        opt.AddPolicy(MatchmakingOutputCachePolicy.PolicyName, new MatchmakingOutputCachePolicy());
    });

builder.Services.AddMassTransit(x =>
{
    x.UsingInMemory((ctx, cfg) => cfg.ConfigureEndpoints(ctx));

    x.AddRider(r =>
    {
        r.AddConsumer<MatchMaking.Service.Consumer>();
        r.AddProducer<MatchmakingRequestMessage>(builder.Configuration["Kafka:MatchmakingRequestTopic"]!);

        r.UsingKafka((ctx, k) =>
        {
            k.Host(builder.Configuration["Kafka:Brokers"]);

            k.TopicEndpoint<MatchmakingCompleteMessage>(
                builder.Configuration["Kafka:MatchmakingCompleteTopic"]!,
                builder.Configuration["Kafka:MatchmakingCompleteGroup"]!,
                e =>
                {
                    e.ConfigureConsumer<MatchMaking.Service.Consumer>(ctx);
                    e.CreateIfMissing();
                });
        });
    });
});

var app = builder.Build();

app.MapOpenApi();
app.UseRouting();
app.UseOutputCache();
app.MapControllers();
app.MapScalarApiReference("/docs", options =>
{
    options
        .WithTitle("API")
        .WithTheme(ScalarTheme.BluePlanet) 
        .WithSidebar(false)
        .WithDarkMode()
        .WithDefaultOpenAllTags(false); 
    
    options.DocumentDownloadType = DocumentDownloadType.None;
    options.HideTestRequestButton = false; 
    options.HideModels = true;
    options.HideDarkModeToggle = true;
    options.HiddenClients = false;
    options.HideClientButton = true;
});

app.Run();