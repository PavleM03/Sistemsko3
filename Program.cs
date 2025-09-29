using System.Reactive.Linq;
using System.Text;
using System.Net;
using System.Threading.Tasks;

public class Program
{
    private static GitHubAnalysisService? _analysisService;
    private static readonly StringBuilder _logBuffer = new StringBuilder();
    private static List<AnalysisResult> _analysisResults = new List<AnalysisResult>();
    private static readonly object _logLock = new object();
    private static readonly object _resultsLock = new object();

    public static async Task Main(string[] args)
    {
        Console.WriteLine("Starting GitHub Sentiment Analysis with VaderSharp2...");

        var githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        _analysisService = new GitHubAnalysisService(githubToken);

        _analysisService.GetLogs()
            .Subscribe(log =>
            {
                var logEntry = $"[{DateTime.Now:HH:mm:ss}] {log}";
                Console.WriteLine(logEntry);
                lock (_logLock)
                {
                    _logBuffer.AppendLine(logEntry);
                }
            });

        _analysisService.GetErrors()
            .Subscribe(error =>
            {
                var errorEntry = $"[{DateTime.Now:HH:mm:ss}] ERROR: {error.Message}";
                Console.WriteLine(errorEntry);
                lock (_logLock)
                {
                    _logBuffer.AppendLine(errorEntry);
                }
            });
        var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:5000/");
        listener.Start();

        Console.WriteLine("Server running on: http://localhost:5000");
        Console.WriteLine("Press Ctrl+C to stop");

        _ = Task.Run(() => HandleRequests(listener));

        Console.CancelKeyPress += (sender, e) =>
        {
            Console.WriteLine("Shutting down...");
            listener.Stop();
            listener.Close();
            Environment.Exit(0);
        };

        await Task.Delay(-1);
    }

    private static async Task HandleRequests(HttpListener listener)
    {
        while (listener.IsListening)
        {
            try
            {
                var context = await listener.GetContextAsync();
                _ = Task.Run(() => ProcessRequest(context));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Server error: {ex.Message}");
            }
        }
    }

    private static async Task ProcessRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            var path = request.Url?.AbsolutePath ?? "/";

            switch (path)
            {
                case "/":
                    await SendHtmlResponse(response, GetHomePage());
                    break;
                case "/analyze":
                    await SendHtmlResponse(response, GetAnalyzePage());
                    _ = Task.Run(async () => await StartAnalysis("dotnet", "runtime", 10));
                    break;
                case "/results":
                    await SendHtmlResponse(response, GetResultsPage());
                    break;
                case "/logs":
                    await SendHtmlResponse(response, GetLogsPage());
                    break;
                default:
                    await SendHtmlResponse(response, GetNotFoundPage());
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Request error: {ex.Message}");
            await SendErrorResponse(response, ex.Message);
        }
    }

    private static async Task SendHtmlResponse(HttpListenerResponse response, string html)
    {
        try
        {
            var buffer = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            response.StatusCode = 200;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Response error: {ex.Message}");
        }
    }

    private static async Task SendErrorResponse(HttpListenerResponse response, string message)
    {
        var html = $@"
            <html>
                <body>
                    <h1>Error</h1>
                    <p>{message}</p>
                    <a href='/'>Back to Home</a>
                </body>
            </html>";
        await SendHtmlResponse(response, html);
    }

    private static string GetHomePage()
    {
        return @"
            <html>
                <head>
                    <title>GitHub Issue Sentiment Analysis</title>
                    <style>
                        body { font-family: Arial, sans-serif; margin: 40px; }
                        .log { background: #f5f5f5; padding: 20px; border-radius: 5px; margin: 20px 0; }
                        .positive { color: green; font-weight: bold; }
                        .negative { color: red; font-weight: bold; }
                        .neutral { color: blue; font-weight: bold; }
                        .issue { border: 1px solid #ddd; padding: 15px; margin: 10px 0; border-radius: 5px; }
                        .comment { margin: 10px 0; padding: 10px; background: #f9f9f9; }
                        .sentiment-bar { background: #eee; height: 20px; margin: 5px 0; }
                        .positive-bar { background: green; height: 100%; }
                        .negative-bar { background: red; height: 100%; }
                        .neutral-bar { background: blue; height: 100%; }
                    </style>
                </head>
                <body>
                    <h1>GitHub Issue Sentiment Analysis (VaderSharp2)</h1>
                    <p><a href='/analyze'>Start Analysis</a></p>
                    <p><a href='/results'>View Results</a></p>
                    <p><a href='/logs'>View Logs</a></p>
                </body>
            </html>";
    }

    private static string GetAnalyzePage()
    {
        return @"
            <html>
                <body>
                    <h1>Analysis Started</h1>
                    <p>Analyzing GitHub issues from dotnet/runtime using VaderSharp2...</p>
                    <p>Check <a href='/logs'>logs</a> for progress.</p>
                    <a href='/'>Back</a>
                </body>
            </html>";
    }

    private static string GetResultsPage()
    {
        var html = new StringBuilder();
        html.AppendLine("<html><body>");
        html.AppendLine("<h1>Analysis Results (VaderSharp2)</h1>");

        List<AnalysisResult> resultsCopy;
        lock (_resultsLock)
        {
            resultsCopy = new List<AnalysisResult>(_analysisResults);
        }

        if (resultsCopy.Any())
        {
            foreach (var result in resultsCopy)
            {
                html.AppendLine($@"<div class='issue'>
                <h3>Issue #{result.Issue.Number}: {result.Issue.Title}</h3>
                <p>Comments analyzed: {result.CommentAnalyses.Count}</p>
                <p>Average Compound Score: 
                    <span class='{(result.AverageCompound >= 0.05 ? "positive" : result.AverageCompound <= -0.05 ? "negative" : "neutral")}'>
                        {result.AverageCompound:F3}
                    </span>
                </p>
                <div class='sentiment-bar'>
                    <div class='positive-bar' style='width: {result.AveragePositive * 100}%'></div>
                    <div class='negative-bar' style='width: {result.AverageNegative * 100}%'></div>
                    <div class='neutral-bar' style='width: {result.AverageNeutral * 100}%'></div>
                </div>");

                foreach (var analysis in result.CommentAnalyses.Take(5))
                {
                    var snippet = analysis.Comment.Body.Length > 100
                        ? analysis.Comment.Body.Substring(0, 100) + "..."
                        : analysis.Comment.Body;

                    html.AppendLine($@"<div class='comment'>
                    <strong>{analysis.Comment.User.Login}</strong>: 
                    <span class='{analysis.SentimentLabel.ToLower()}'>{analysis.SentimentLabel}</span>
                    (Compound: {analysis.Result.Compound:F3})
                    <br/>{(snippet)}
                </div>");
                }
                html.AppendLine("</div>");
            }
        }
        else
        {
            html.AppendLine("<p>No results yet. <a href='/analyze'>Start analysis</a></p>");
        }

        html.AppendLine("<a href='/'>Back</a>");
        html.AppendLine("</body></html>");

        return html.ToString();
    }

    private static string GetLogsPage()
    {
        string logs;
        lock (_logLock)
        {
            logs = _logBuffer.ToString();
        }
        return $@"
        <html>
            <body>
                <h1>Server Logs</h1>
                <div class='log'>
                    <pre>{logs}</pre>
                </div>
                <a href='/'>Back</a>
            </body>
        </html>";
    }

    private static string GetNotFoundPage()
    {
        return @"
            <html>
                <body>
                    <h1>404 - Not Found</h1>
                    <p>The requested page was not found.</p>
                    <a href='/'>Back to Home</a>
                </body>
            </html>";
    }

    private static async Task StartAnalysis(string owner, string repo, int maxIssues)
    {
        try
        {
            if (_analysisService == null) return;

            lock (_resultsLock)
            {
                _analysisResults.Clear();
            }

            var results = await _analysisService.AnalyzeRepository(owner, repo, maxIssues)
                .Do(result =>
                {
                    Console.WriteLine($"\n=== Issue #{result.Issue.Number}: {result.Issue.Title} ===");
                    Console.WriteLine($"Comments analyzed: {result.CommentAnalyses.Count}");
                    Console.WriteLine($"Average Compound Score: {result.AverageCompound:F3}");
                    Console.WriteLine($"Sentiment Breakdown - Positive: {result.AveragePositive:P1}, Negative: {result.AverageNegative:P1}, Neutral: {result.AverageNeutral:P1}");

                    foreach (var analysis in result.CommentAnalyses.Take(2))
                    {
                        var snippet = analysis.Comment.Body.Length > 50
                            ? analysis.Comment.Body.Substring(0, 50) + "..."
                            : analysis.Comment.Body;

                        Console.WriteLine($"  - {analysis.SentimentLabel} (Compound: {analysis.Result.Compound:F3}): {snippet}");
                    }
                })
                .ToArray();

            lock (_resultsLock)
            {
                _analysisResults = results.ToList();
            }

            Console.WriteLine($"\n=== ANALYSIS COMPLETED ===");
            Console.WriteLine($"Total issues analyzed: {results.Length}");

            if (results.Any())
            {
                var overallCompound = results.Average(r => r.AverageCompound);
                var sentiment = overallCompound >= 0.05 ? "POSITIVE" : overallCompound <= -0.05 ? "NEGATIVE" : "NEUTRAL";
                Console.WriteLine($"Overall sentiment: {overallCompound:F3} ({sentiment})");

                var totalComments = results.Sum(r => r.CommentAnalyses.Count);
                var positiveComments = results.Sum(r => r.CommentAnalyses.Count(c => c.Result.Compound >= 0.05));
                var negativeComments = results.Sum(r => r.CommentAnalyses.Count(c => c.Result.Compound <= -0.05));
                var neutralComments = results.Sum(r => r.CommentAnalyses.Count(c => c.Result.Compound > -0.05 && c.Result.Compound < 0.05));

                Console.WriteLine($"Comments breakdown: {positiveComments} positive, {negativeComments} negative, {neutralComments} neutral");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Analysis failed: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
}