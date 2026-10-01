using Azure.Identity;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;
using MsftFoundryTeamsAudioSummarize.Web;
using MsftFoundryTeamsAudioSummarize.Web.Bots;
using MsftFoundryTeamsAudioSummarize.Web.Configuration;
using MsftFoundryTeamsAudioSummarize.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// App registration #1 (Bot Service) and #2 (Foundry), validated at startup.
var botSettings = BotSettings.Create(builder.Configuration);
var foundrySettings = FoundrySettings.Create(builder.Configuration);

builder.Services.AddSingleton(botSettings);
builder.Services.AddSingleton(foundrySettings);

// Single Entra ID credential shared by Speech to Text and the Foundry Agent Service.
builder.Services.AddSingleton(new ClientSecretCredential(
    foundrySettings.TenantId,
    foundrySettings.ClientId,
    foundrySettings.ClientSecret));

builder.Services.AddControllers().AddNewtonsoftJson();

// Bot Framework auth reads MicrosoftAppType / MicrosoftAppId / MicrosoftAppPassword / MicrosoftAppTenantId.
builder.Services.AddSingleton<BotFrameworkAuthentication, ConfigurationBotFrameworkAuthentication>();
builder.Services.AddSingleton<AdapterWithErrorHandler>();
builder.Services.AddSingleton<IBotFrameworkHttpAdapter>(sp => sp.GetRequiredService<AdapterWithErrorHandler>());
builder.Services.AddSingleton<CloudAdapter>(sp => sp.GetRequiredService<AdapterWithErrorHandler>());

builder.Services.AddHttpClient(nameof(AudioDownloader), client => client.Timeout = TimeSpan.FromMinutes(5))
    .RemoveAllLoggers();
builder.Services.AddSingleton<AudioDownloader>();
builder.Services.AddHttpClient(nameof(GraphAudioService), client => client.Timeout = TimeSpan.FromSeconds(30))
    .RemoveAllLoggers()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton(sp => new GraphAudioService(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GraphAudioService)),
    new ClientSecretCredential(botSettings.TenantId, botSettings.AppId, botSettings.AppPassword),
    builder.Configuration.GetValue<bool>("Graph:Enabled")));
builder.Services.AddSingleton<SpeechTranscriptionService>();
builder.Services.AddSingleton<SummarizerAgentService>();
builder.Services.AddTransient<IBot, AudioSummarizerBot>();

var app = builder.Build();

app.MapControllers();
app.MapGet("/health", () => Results.Ok("healthy"));

app.Run();
