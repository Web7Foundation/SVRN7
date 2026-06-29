using Pando.Board.Models;

namespace Pando.Board.Services;

/// <summary>
/// Sort order engine — data concern only.
/// Computes velocity scores and sorts columns.
/// My Agent always index 0.
/// </summary>
public class VelocityService
{
    private readonly TimeSpan _window = TimeSpan.FromMinutes(10);
    private const double Decay = 0.85;

    public double ComputeScore(Contact contact)
    {
        if (contact.IsMyAgent) return double.MaxValue;
        var now = DateTime.UtcNow;
        var cutoff = now - _window;
        double score = 0;
        foreach (var t in contact.Threads)
            foreach (var m in t.Messages)
            {
                if (m.Timestamp < cutoff) continue;
                score += Math.Pow(Decay, (now - m.Timestamp).TotalMinutes);
            }
        return score;
    }

    public List<BoardColumn> RefreshAndSort(List<BoardColumn> columns)
    {
        foreach (var col in columns)
            col.Contact.VelocityScore = ComputeScore(col.Contact);

        var agent = columns.Where(c => c.Contact.IsMyAgent).ToList();
        var others = columns.Where(c => !c.Contact.IsMyAgent)
                            .OrderByDescending(c => c.Contact.VelocityScore)
                            .ToList();

        var result = agent.Concat(others).ToList();
        for (int i = 0; i < result.Count; i++) result[i].SortIndex = i;
        return result;
    }
}
