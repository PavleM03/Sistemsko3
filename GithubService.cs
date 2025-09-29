using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Text;

public class GitHubService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string? _token;
    private readonly Subject<string> _logSubject;
    private readonly Subject<Exception> _errorSubject;
    
    public IObservable<string> Logs => _logSubject.AsObservable();
    public IObservable<Exception> Errors => _errorSubject.AsObservable();

    public GitHubService(string? token = null)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Rx-GitHub-Analyzer-VaderSharp2");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _token = token;
        
        if (!string.IsNullOrEmpty(_token))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_token}");
        }
        
        _logSubject = new Subject<string>();
        _errorSubject = new Subject<Exception>();
    }

    public IObservable<List<GitHubIssue>> GetIssues(string owner, string repo, int perPage = 30)
    {
        var url = $"https://api.github.com/repos/{owner}/{repo}/issues?per_page={perPage}&state=all";
        
        return Observable.FromAsync(() => MakeGitHubRequest<List<GitHubIssue>>(url))
            .Where(issues => issues != null)
            .Select(issues => issues ?? new List<GitHubIssue>())
            .Do(issues => _logSubject.OnNext($"Fetched {issues.Count} issues from {owner}/{repo}"))
            .Catch<List<GitHubIssue>, Exception>(ex =>
            {
                _errorSubject.OnNext(new Exception($"Failed to fetch issues from {owner}/{repo}", ex));
                return Observable.Return(new List<GitHubIssue>());
            })
            .SubscribeOn(System.Reactive.Concurrency.NewThreadScheduler.Default);
    }

    public IObservable<List<GitHubComment>> GetCommentsForIssue(GitHubIssue issue)
    {
        if (issue.CommentsCount == 0)
            return Observable.Return(new List<GitHubComment>());

        return Observable.FromAsync(() => GetAllComments(issue.CommentsUrl))
            .Do(comments => _logSubject.OnNext($"Fetched {comments.Count} comments for issue #{issue.Number} (expected: {issue.CommentsCount})"))
            .Catch<List<GitHubComment>, Exception>(ex =>
            {
                _errorSubject.OnNext(new Exception($"Failed to fetch comments for issue #{issue.Number}", ex));
                return Observable.Return(new List<GitHubComment>());
            })
            .SubscribeOn(System.Reactive.Concurrency.NewThreadScheduler.Default);
    }

    private async Task<List<GitHubComment>> GetAllComments(string commentsUrl)
    {
        var allComments = new List<GitHubComment>();
        var page = 1;
        var hasMorePages = true;

        while (hasMorePages)
        {
            var url = $"{commentsUrl}?per_page=100&page={page}";
            var comments = await MakeGitHubRequest<List<GitHubComment>>(url);
            
            if (comments == null || !comments.Any())
            {
                hasMorePages = false;
            }
            else
            {
                allComments.AddRange(comments);
                
                if (comments.Count < 100)
                {
                    hasMorePages = false;
                }
                else
                {
                    page++;
                    await Task.Delay(500);
                }
            }
        }

        return allComments;
    }

    private async Task<T?> MakeGitHubRequest<T>(string url)
    {
        try
        {
            var response = await _httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return default(T);
                }
                
                throw new HttpRequestException($"GitHub API returned {response.StatusCode}: {errorContent}");
            }

            if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining))
            {
                var remainingCount = remaining.FirstOrDefault();
                if (int.TryParse(remainingCount, out int remainingInt) && remainingInt < 10)
                {
                    _logSubject.OnNext($"WARNING: Rate limit low - {remainingInt} requests remaining");
                }
            }

            var json = await response.Content.ReadAsStringAsync();
            return System.Text.Json.JsonSerializer.Deserialize<T>(json, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            _errorSubject.OnNext(ex);
            throw;
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        _logSubject?.OnCompleted();
        _errorSubject?.OnCompleted();
    }
}