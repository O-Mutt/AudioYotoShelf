using AudioYotoShelf.Api.Hubs;
using AudioYotoShelf.Api.Middleware;
using AudioYotoShelf.Core.Interfaces;
using AudioYotoShelf.Core.Services;
using AudioYotoShelf.Infrastructure.Caching;
using AudioYotoShelf.Infrastructure.Data;
using AudioYotoShelf.Infrastructure.Services;
using AudioYotoShelf.Infrastructure.Services.Audiobookshelf;
using AudioYotoShelf.Infrastructure.Services.BackgroundJobs;
using AudioYotoShelf.Infrastructure.Services.IconGeneration;
using AudioYotoShelf.Infrastructure.Services.Yoto;
using FluentValidation;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// --- Serilog ---
builder.Host.UseSerilog((context, loggerConfig) =>
{
    loggerConfig
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithEnvironmentName()
        .Enrich.WithThreadId()
        .WriteTo.Console(outputTemplate:
            "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");

    var seqUrl = context.Configuration["Serilog:SeqUrl"];
    if (!string.IsNullOrEmpty(seqUrl))
        loggerConfig.WriteTo.Seq(seqUrl);
});

// --- Database ---
builder.Services.AddDbContext<AudioYotoShelfDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"), npgsql =>
    {
        npgsql.MigrationsAssembly(typeof(AudioYotoShelfDbContext).Assembly.FullName);
        npgsql.EnableRetryOnFailure(3);
    }));

// --- Redis ---
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "AudioYotoShelf:";
});

// --- Hangfire ---
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options =>
        options.UseNpgsqlConnection(builder.Configuration.GetConnectionString("Postgres"))));
builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = 2;
    options.Queues = ["transfers", "icons", "default"];
});

// --- HttpClients ---
builder.Services.AddHttpClient("Audiobookshelf", client =>
{
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromMinutes(10);
});
builder.Services.AddHttpClient("Yoto", client =>
{
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromMinutes(5);
});
builder.Services.AddHttpClient("YotoAuth", client =>
{
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});
builder.Services.AddHttpClient("YotoUpload", client =>
{
    client.Timeout = TimeSpan.FromMinutes(30);
});
builder.Services.AddHttpClient("Gemini", client =>
{
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromMinutes(2);
});

// --- Services (DI) ---
builder.Services.AddScoped<IAudiobookshelfService, AudiobookshelfService>();
builder.Services.AddScoped<IYotoService, YotoService>();
builder.Services.AddScoped<IAgeSuggestionService, AgeSuggestionService>();
builder.Services.AddScoped<IChapterExtractor, FfmpegChapterExtractor>();
builder.Services.AddScoped<IIconGenerationService, GeminiIconGenerationService>();
builder.Services.AddScoped<ITransferOrchestrator, TransferOrchestrator>();
builder.Services.AddSingleton<ICacheService, RedisCacheService>();
builder.Services.AddTransferJobs();

// --- FluentValidation ---
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// --- SignalR with Redis backplane ---
var signalRBuilder = builder.Services.AddSignalR();
var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrEmpty(redisConnectionString))
{
    signalRBuilder.AddStackExchangeRedis(redisConnectionString, options =>
    {
        options.Configuration.ChannelPrefix = new StackExchange.Redis.RedisChannel(
            "AudioYotoShelf", StackExchange.Redis.RedisChannel.PatternMode.Literal);
    });
}

// --- API ---
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// --- CORS for Vue dev server ---
builder.Services.AddCors(options =>
{
    options.AddPolicy("Development", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

// --- Middleware pipeline ---
app.UseGlobalExceptionHandling();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors("Development");
}

app.UseSerilogRequestLogging();
app.UseStaticFiles();
app.UseRouting();

app.MapHangfireDashboard("/hangfire", new DashboardOptions
{
    DashboardTitle = "AudioYotoShelf Jobs"
});

app.MapControllers();
app.MapHub<TransferHub>("/hubs/transfer");
app.MapFallbackToFile("index.html");

// --- Database migration on startup ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AudioYotoShelfDbContext>();
    await db.Database.MigrateAsync();

    var ffmpeg = scope.ServiceProvider.GetRequiredService<IChapterExtractor>();
    var ffmpegAvailable = await ffmpeg.IsFfmpegAvailableAsync();
    Log.Information("FFmpeg available: {Available}", ffmpegAvailable);
}

Log.Information("AudioYotoShelf started. Environment: {Environment}", app.Environment.EnvironmentName);
await app.RunAsync();

// Required for integration test WebApplicationFactory
public partial class Program;
