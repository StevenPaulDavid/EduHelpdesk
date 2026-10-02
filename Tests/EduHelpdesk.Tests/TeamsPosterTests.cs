using System.Net;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduHelpdesk.Tests;

// Sending to Teams (TeamsPoster.SendAsync) against a pretend Teams: a good answer, a refusal that isn't retried, a busy
// Teams that is, an unreachable one, and that the secret address never reaches the words shown on the settings page.
public class TeamsPosterTests
{
    private const string Address = "https://prod-12.westeurope.logic.azure.com:443/workflows/abc123/invoke?sig=SECRETSIGNATURE";
    private static readonly HelpdeskStore.TeamsPost Post = new("New ticket #5", "Submitted.", "Open ticket", "https://helpdesk.test/Job?number=5");

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<(Uri? Uri, string Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Requests.Add((request.RequestUri, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancel)));
            return answer(request);
        }
    }

    private sealed class Factory(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static (TeamsPoster Poster, Handler Handler, TestStore Test) Build(Func<HttpRequestMessage, HttpResponseMessage> answer, bool withAddress = true)
    {
        var test = new TestStore();
        if (withAddress) test.Store.SetTeamsSettings(Address, false, true, true, true);
        var handler = new Handler(answer);
        return (new TeamsPoster(test.Store, new Factory(handler), NullLogger<TeamsPoster>.Instance, [TimeSpan.Zero, TimeSpan.Zero]), handler, test);
    }

    [Fact]
    public async Task A_good_answer_is_a_success_and_the_card_goes_to_the_saved_address()
    {
        var (poster, handler, test) = Build(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        using var _ = test;
        var (ok, message) = await poster.SendAsync(Post, retry: true, CancellationToken.None);

        Assert.True(ok, message);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal(new Uri(Address), sent.Uri);
        Assert.Contains("\"AdaptiveCard\"", sent.Body);
        Assert.Contains("New ticket #5", sent.Body);
    }

    [Fact]
    public async Task A_refusal_that_asking_again_would_not_change_is_tried_once_and_the_address_stays_out_of_the_words()
    {
        var (poster, handler, test) = Build(_ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent($"Bad request to {Address} - the workflow was removed") });
        using var _ = test;
        var (ok, message) = await poster.SendAsync(Post, retry: true, CancellationToken.None);

        Assert.False(ok);
        Assert.Single(handler.Requests);
        Assert.Contains("400", message);
        Assert.Contains("the workflow was removed", message);
        Assert.DoesNotContain("SECRETSIGNATURE", message);
    }

    [Fact]
    public async Task A_busy_Teams_is_retried_and_a_later_success_counts()
    {
        var calls = 0;
        var (poster, handler, test) = Build(_ => ++calls < 3 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : new HttpResponseMessage(HttpStatusCode.OK));
        using var _ = test;
        var (ok, message) = await poster.SendAsync(Post, retry: true, CancellationToken.None);

        Assert.True(ok, message);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task A_test_message_is_never_retried_and_an_unreachable_Teams_is_reported_without_the_address()
    {
        var (poster, handler, test) = Build(_ => throw new HttpRequestException($"The connection to {Address} was refused"));
        using var _ = test;
        var (ok, message) = await poster.SendAsync(Post, retry: false, CancellationToken.None);

        Assert.False(ok);
        Assert.Single(handler.Requests);
        Assert.StartsWith("Teams couldn't be reached", message);
        Assert.DoesNotContain("SECRETSIGNATURE", message);

        var (retriedOk, _) = await Build(_ => throw new HttpRequestException("down")).Poster.SendAsync(Post, retry: true, CancellationToken.None);
        Assert.False(retriedOk);
    }

    [Fact]
    public async Task With_no_address_nothing_is_sent()
    {
        var (poster, handler, test) = Build(_ => new HttpResponseMessage(HttpStatusCode.OK), withAddress: false);
        using var _ = test;
        var (ok, message) = await poster.SendAsync(Post, retry: true, CancellationToken.None);

        Assert.False(ok);
        Assert.Empty(handler.Requests);
        Assert.Equal("No webhook address is set.", message);
    }
}
