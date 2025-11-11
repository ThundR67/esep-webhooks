using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Lambda.TestUtilities;
using Xunit;

namespace EsepWebhook.Tests;

public class FunctionTest
{
    [Fact]
    public async Task SendsSlackNotificationWithIssueDetails()
    {
        const string slackUrl = "https://hooks.slack.com/services/test/test/test";
        var handler = new StubHttpMessageHandler();
        using var httpClient = new HttpClient(handler);

        var priorSlackUrl = Environment.GetEnvironmentVariable("SLACK_URL");
        Environment.SetEnvironmentVariable("SLACK_URL", slackUrl);

        try
        {
            var function = new Function(httpClient);
            var context = new TestLambdaContext();
            var payload = """
                          {
                            "action": "opened",
                            "repository": {
                              "full_name": "octo-org/octo-repo"
                            },
                            "issue": {
                              "html_url": "https://github.com/octo-org/octo-repo/issues/42",
                              "number": 42,
                              "title": "Bug report"
                            },
                            "sender": {
                              "login": "octocat"
                            }
                          }
                          """;

            var result = await function.FunctionHandler(payload, context);

            Assert.Contains("Slack notification sent", result);
            Assert.NotNull(handler.LastRequestBody);
            Assert.Contains("octo-org/octo-repo", handler.LastRequestBody);
            Assert.Contains("https://github.com/octo-org/octo-repo/issues/42", handler.LastRequestBody);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SLACK_URL", priorSlackUrl);
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync();

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
