using Microsoft.AspNetCore.Mvc;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;

namespace MsftFoundryTeamsAudioSummarize.Web.Controllers;

/// <summary>
/// Messaging endpoint configured in Azure Bot Service: https://&lt;app&gt;.azurewebsites.net/api/messages
/// </summary>
[ApiController]
[Route("api/messages")]
public sealed class BotController(IBotFrameworkHttpAdapter adapter, IBot bot) : ControllerBase
{
    // ProcessAsync validates the Azure Bot Service JWT against MicrosoftAppId / MicrosoftAppTenantId.
    [HttpPost]
    public Task PostAsync(CancellationToken cancellationToken) =>
        adapter.ProcessAsync(Request, Response, bot, cancellationToken);
}
