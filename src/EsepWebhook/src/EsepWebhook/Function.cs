using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace EsepWebhook;

public class Function
{
    private const string SlackUrlEnvVar = "SLACK_URL";
    private readonly HttpClient _httpClient;

    public Function() : this(new HttpClient())
    {
    }

    public Function(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <summary>
    /// Parse a GitHub issues webhook payload and forward the details to Slack.
    /// </summary>
    public async Task<string> FunctionHandler(string input, ILambdaContext context)
    {
        var slackWebhookUrl = Environment.GetEnvironmentVariable(SlackUrlEnvVar);
        if (string.IsNullOrWhiteSpace(slackWebhookUrl))
        {
            const string message = "Missing SLACK_URL environment variable.";
            context.Logger.LogLine(message);
            return message;
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            const string message = "No payload supplied.";
            context.Logger.LogLine(message);
            return message;
        }

        IssueNotification notification;
        try
        {
            notification = ParsePayload(input);
        }
        catch (Exception ex)
        {
            var message = $"Invalid GitHub webhook payload: {ex.Message}";
            context.Logger.LogLine(message);
            return message;
        }

        var slackPayload = new
        {
            text = BuildSlackMessage(notification)
        };

        var requestBody = JsonConvert.SerializeObject(slackPayload);
        using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(slackWebhookUrl, content);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            var message = $"Slack webhook returned {(int)response.StatusCode}: {body}";
            context.Logger.LogLine(message);
            return message;
        }

        var issueIdentifier = notification.IssueNumber?.ToString() ?? "unknown";
        return $"Slack notification sent for issue #{issueIdentifier}.";
    }

    private static IssueNotification ParsePayload(string raw)
    {
        var payload = JObject.Parse(raw);
        var issue = payload["issue"] ?? throw new InvalidOperationException("Payload missing issue object.");

        var issueUrl = issue["html_url"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(issueUrl))
        {
            throw new InvalidOperationException("issue.html_url missing from payload.");
        }

        var repo = payload["repository"];
        var sender = payload["sender"];

        return new IssueNotification(
            IssueUrl: issueUrl,
            IssueTitle: issue["title"]?.Value<string>() ?? "GitHub Issue",
            IssueNumber: issue["number"]?.Value<int?>(),
            RepositoryFullName: repo?["full_name"]?.Value<string>() ?? "unknown repository",
            Action: payload["action"]?.Value<string>() ?? "acted on",
            Sender: sender?["login"]?.Value<string>() ?? "unknown user");
    }

    private static string BuildSlackMessage(IssueNotification notification)
    {
        var issueLabel = notification.IssueNumber.HasValue ? $"#{notification.IssueNumber}" : "Issue";
        return $"[{notification.RepositoryFullName}] {notification.Sender} {notification.Action} {issueLabel}: <{notification.IssueUrl}|{notification.IssueTitle}>";
    }

    private sealed record IssueNotification(
        string IssueUrl,
        string IssueTitle,
        int? IssueNumber,
        string RepositoryFullName,
        string Action,
        string Sender);
}
