using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

public class SentimentAnalysisService
{
    private readonly VaderSharp2.SentimentIntensityAnalyzer _analyzer;
    
    public SentimentAnalysisService()
    {
        _analyzer = new VaderSharp2.SentimentIntensityAnalyzer();
    }

    public IObservable<SentimentAnalysis> AnalyzeComment(GitHubComment comment)
    {
        return Observable.Start(() =>
        {
            try
            {
                var result = _analyzer.PolarityScores(comment.Body);
                var label = GetSentimentLabel(result.Compound);
                
                return new SentimentAnalysis
                {
                    Comment = comment,
                    Result = result,
                    SentimentLabel = label
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error analyzing comment {comment.Id}: {ex.Message}");
                return new SentimentAnalysis
                {
                    Comment = comment,
                    Result = new VaderSharp2.SentimentAnalysisResults { Neutral = 1.0, Compound = 0.0 },
                    SentimentLabel = "NEUTRAL"
                };
            }
        }, System.Reactive.Concurrency.TaskPoolScheduler.Default);
    }

    private static string GetSentimentLabel(double compound)
    {
        return compound switch
        {
            >= 0.05 => "POSITIVE",
            <= -0.05 => "NEGATIVE",
            _ => "NEUTRAL"
        };
    }
}