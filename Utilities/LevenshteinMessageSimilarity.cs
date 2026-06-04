namespace AntiSpamBot.Utilities;

public interface IMessageSimilarity
{
    double Compare(string left, string right);
}

public sealed class LevenshteinMessageSimilarity : IMessageSimilarity
{
    public double Compare(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return 0.0;
        }

        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return 1.0;
        }

        var distance = Distance(left, right);
        var maxLength = Math.Max(left.Length, right.Length);
        var levenshteinRatio = maxLength == 0 ? 1.0 : 1.0 - (double)distance / maxLength;
        var tokenRatio = TokenDiceRatio(left, right);

        return Math.Max(levenshteinRatio, tokenRatio);
    }

    private static double TokenDiceRatio(string left, string right)
    {
        var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

        if (leftTokens.Count == 0 || rightTokens.Count == 0)
        {
            return 0.0;
        }

        var intersection = leftTokens.Count(rightTokens.Contains);
        return (2.0 * intersection) / (leftTokens.Count + rightTokens.Count);
    }

    private static int Distance(string left, string right)
    {
        if (left.Length > right.Length)
        {
            (left, right) = (right, left);
        }

        var previous = new int[left.Length + 1];
        var current = new int[left.Length + 1];

        for (var i = 0; i <= left.Length; i++)
        {
            previous[i] = i;
        }

        for (var j = 1; j <= right.Length; j++)
        {
            current[0] = j;

            for (var i = 1; i <= left.Length; i++)
            {
                var substitutionCost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[i] = Math.Min(
                    Math.Min(current[i - 1] + 1, previous[i] + 1),
                    previous[i - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        return previous[left.Length];
    }
}
