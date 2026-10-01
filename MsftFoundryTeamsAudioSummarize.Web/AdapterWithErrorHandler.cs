using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;

namespace MsftFoundryTeamsAudioSummarize.Web;

public sealed class AdapterWithErrorHandler : CloudAdapter
{
    public AdapterWithErrorHandler(
        BotFrameworkAuthentication botFrameworkAuthentication,
        ILogger<CloudAdapter> logger)
        : base(botFrameworkAuthentication, logger)
    {
        OnTurnError = async (turnContext, exception) =>
        {
            logger.LogError(exception, "Unhandled bot error.");

            await turnContext.SendActivityAsync(
                MessageFactory.Text("An error occurred while processing your message."));
        };
    }
}
