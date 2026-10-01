using System.Net;
using System.Text;
using Azure.Core;
using Microsoft.Bot.Schema;
using MsftFoundryTeamsAudioSummarize.Web.Services;
using Newtonsoft.Json.Linq;

static void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    Console.WriteLine("PASS " + description);
}

var activity = new Activity
{
    ChannelId = "msteams", Id = "123", Conversation = new ConversationAccount { Id = "19:chat@thread.v2", ConversationType = "groupChat" }
};
Check(GraphAudioService.IsSharedConversation(activity), "group chat detected even without bot attachments");
Check(GraphAudioService.GetMessagePath(activity) == "chats/19%3Achat%40thread.v2/messages/123", "chat routing encodes identifiers");
activity.Conversation.ConversationType = "channel";
activity.ChannelData = JObject.Parse("""{"team":{"aadGroupId":"team-guid"},"channel":{"id":"19:channel"}}""");
activity.ReplyToId = "100";
Check(GraphAudioService.GetMessagePath(activity) == "teams/team-guid/channels/19%3Achannel/messages/100/replies/123", "channel reply routing");
activity.ReplyToId = null;
Check(GraphAudioService.GetMessagePath(activity).EndsWith("/messages/123"), "channel root routing");
activity.Conversation.ConversationType = "personal";
Check(!GraphAudioService.IsSharedConversation(activity), "personal messages bypass Graph");
var direct = AudioAttachment.FindFirst([new Attachment
{
    ContentType = "application/vnd.microsoft.teams.file.download.info", Name = "Audio.mp3",
    Content = JObject.Parse("""{"fileType":"mp3","downloadUrl":"https://example.com/audio"}""")
}]);
Check(direct is { RequiresBotToken: false }, "personal audio detection preserved");

const string message = """{"attachments":[{"contentType":"reference","name":"Audio 24.MP3","contentUrl":"https://tenant.sharepoint.com/Audio%2024.mp3"}]}""";
const string item = """{"name":"Audio 24.mp3","size":123,"file":{"mimeType":"audio/mpeg"},"@microsoft.graph.downloadUrl":"https://tenant.sharepoint.com/download?token=secret"}""";
var handler = new ResponsesHandler(message, item);
var service = new GraphAudioService(new HttpClient(handler), new FakeCredential(), true);
var audio = await service.ResolveAsync("chats/chat/messages/123", default);
Check(audio is { FileName: "Audio 24.mp3", RequiresBotToken: false }, "Graph reference resolves to preauthenticated audio");
var expectedShare = "u!" + Convert.ToBase64String(Encoding.UTF8.GetBytes("https://tenant.sharepoint.com/Audio%2024.mp3")).TrimEnd('=').Replace('/', '_').Replace('+', '-');
Check(handler.Paths[1] == "/v1.0/shares/" + expectedShare + "/driveItem", "sharing URL encoded correctly");
Check(handler.Paths.Count == 2, "only triggering message and its file are fetched");
var noFile = new GraphAudioService(new HttpClient(new ResponsesHandler("""{"attachments":[]}""")), new FakeCredential(), true);
Check(await noFile.ResolveAsync("chats/chat/messages/123", default) is null, "text message returns no audio");
var nonAudio = new GraphAudioService(new HttpClient(new ResponsesHandler(message, item.Replace("Audio 24.mp3", "document.pdf"))), new FakeCredential(), true);
Check(await nonAudio.ResolveAsync("chats/chat/messages/123", default) is null, "actual file extension validated");
try
{
    var oversized = new GraphAudioService(new HttpClient(new ResponsesHandler(message, item.Replace("123", "209715201"))), new FakeCredential(), true);
    await oversized.ResolveAsync("chats/chat/messages/123", default);
    throw new Exception("Size limit not enforced");
}
catch (AudioTooLargeException) { Console.WriteLine("PASS oversized file rejected before download"); }
try
{
    var denied = new GraphAudioService(new HttpClient(new ResponsesHandler { Status = HttpStatusCode.Forbidden }), new FakeCredential(), true);
    await denied.ResolveAsync("chats/chat/messages/123", default);
    throw new Exception("Forbidden response not handled");
}
catch (GraphAudioException ex) { Check(ex.StatusCode == HttpStatusCode.Forbidden, "Graph permissions error preserved"); }
Console.WriteLine("All checks passed.");

sealed class FakeCredential : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext context, CancellationToken cancellationToken) => new("test-token", DateTimeOffset.UtcNow.AddHours(1));
    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext context, CancellationToken cancellationToken) => ValueTask.FromResult(GetToken(context, cancellationToken));
}
sealed class ResponsesHandler(params string[] responses) : HttpMessageHandler
{
    public List<string> Paths { get; } = [];
    public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.Host != "graph.microsoft.com" || request.Headers.Authorization?.Parameter != "test-token")
            throw new Exception("Unexpected token destination or missing authorization");
        Paths.Add(request.RequestUri.AbsolutePath);
        return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(responses.ElementAtOrDefault(Paths.Count - 1) ?? "{}") });
    }
}
