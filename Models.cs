using System.Text.Json.Serialization;

public class GitHubIssue
{
    [JsonPropertyName("number")] 
    public int Number { get; set; }
    
    [JsonPropertyName("title")] 
    public string Title { get; set; } = string.Empty;
    
    [JsonPropertyName("body")] 
    public string Body { get; set; } = string.Empty;
    
    [JsonPropertyName("comments_url")] 
    public string CommentsUrl { get; set; } = string.Empty;
    
    [JsonPropertyName("comments")] 
    public int CommentsCount { get; set; }
}

public class GitHubComment
{
    [JsonPropertyName("id")] 
    public long Id { get; set; }
    
    [JsonPropertyName("body")] 
    public string Body { get; set; } = string.Empty;
    
    [JsonPropertyName("user")] 
    public GitHubUser User { get; set; } = new GitHubUser();
    
    [JsonPropertyName("created_at")] 
    public DateTime CreatedAt { get; set; }
}

public class GitHubUser
{
    [JsonPropertyName("login")] 
    public string Login { get; set; } = string.Empty;
}

public class SentimentAnalysis
{
    public GitHubComment Comment { get; set; } = new GitHubComment();
    public VaderSharp2.SentimentAnalysisResults Result { get; set; } = new VaderSharp2.SentimentAnalysisResults();
    public string SentimentLabel { get; set; } = string.Empty;
}

public class AnalysisResult
{
    public GitHubIssue Issue { get; set; } = new GitHubIssue();
    public List<SentimentAnalysis> CommentAnalyses { get; set; } = new();
    
    public double AveragePositive => CommentAnalyses.Count > 0 ? CommentAnalyses.Average(c => c.Result.Positive) : 0;
    public double AverageNegative => CommentAnalyses.Count > 0 ? CommentAnalyses.Average(c => c.Result.Negative) : 0;
    public double AverageNeutral => CommentAnalyses.Count > 0 ? CommentAnalyses.Average(c => c.Result.Neutral) : 0;
    public double AverageCompound => CommentAnalyses.Count > 0 ? CommentAnalyses.Average(c => c.Result.Compound) : 0;
}