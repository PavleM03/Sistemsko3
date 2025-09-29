using System.Reactive.Linq;
using System.Reactive.Disposables;
using System.Reactive.Subjects;

public class GitHubAnalysisService : IDisposable
{
    private readonly GitHubService _gitHubService;
    private readonly SentimentAnalysisService _sentimentService;
    private readonly CompositeDisposable _disposables;

    public GitHubAnalysisService(string? githubToken = null)
    {
        _gitHubService = new GitHubService(githubToken);
        _sentimentService = new SentimentAnalysisService();
        _disposables = new CompositeDisposable();
    }

    public IObservable<AnalysisResult> AnalyzeRepository(string owner, string repo, int maxIssues = 10)
    {
        return _gitHubService.GetIssues(owner, repo, maxIssues)
            .SelectMany(issues => issues)
            .SelectMany(issue => AnalyzeIssueWithComments(issue))
            .Where(result => result.CommentAnalyses.Any())
            .Do(result =>
            {
                if (_gitHubService is GitHubService gitHubService)
                {
                    Console.WriteLine($"Completed analysis for issue #{result.Issue.Number}");
                }
            });
    }

    private IObservable<AnalysisResult> AnalyzeIssueWithComments(GitHubIssue issue)
    {
        return _gitHubService.GetCommentsForIssue(issue)
            .SelectMany(comments => comments)
            .Select(comment => _sentimentService.AnalyzeComment(comment))
            .Merge()
            .ToList()
            .Select(allAnalyses => new AnalysisResult
            {
                Issue = issue,
                CommentAnalyses = allAnalyses.ToList()
            })
            .Where(result => result.CommentAnalyses.Any())
            .Timeout(TimeSpan.FromSeconds(90))
            .Catch<AnalysisResult, Exception>(ex =>
            {
                if (_gitHubService is GitHubService gitHubService)
                {
                    Console.WriteLine($"Timeout or error analyzing issue #{issue.Number}: {ex.Message}");
                }
                return Observable.Return(new AnalysisResult { Issue = issue, CommentAnalyses = new List<SentimentAnalysis>() });
            });
    }

    public IObservable<string> GetLogs() => _gitHubService.Logs;
    public IObservable<Exception> GetErrors() => _gitHubService.Errors;

    public void Dispose()
    {
        _disposables?.Dispose();
        _gitHubService?.Dispose();
    }
}