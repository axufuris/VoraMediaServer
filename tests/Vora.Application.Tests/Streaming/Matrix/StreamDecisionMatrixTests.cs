namespace Vora.Application.Tests.Streaming.Matrix;

public sealed class StreamDecisionMatrix : IAsyncLifetime
{
    public const string ScopeVariable = "VORA_STREAM_MATRIX";
    private const int ExamplesKept = 5;

    private readonly Dictionary<string, List<string>> _examples = MatrixRules.All.ToDictionary(rule => rule.Name, _ => new List<string>());
    private readonly Dictionary<string, int> _broken = MatrixRules.All.ToDictionary(rule => rule.Name, _ => 0);

    public int Combinations { get; private set; }

    public async ValueTask InitializeAsync()
    {
        bool exhaustive = Environment.GetEnvironmentVariable(ScopeVariable) == "full";
        var known = new Dictionary<string, MatrixOutcome>();

        async Task<MatrixOutcome> RunAsync(MatrixScenario scenario)
        {
            if (exhaustive) return await MatrixRunner.RunAsync(scenario);
            if (!known.TryGetValue(scenario.Key, out var outcome)) known[scenario.Key] = outcome = await MatrixRunner.RunAsync(scenario);
            return outcome;
        }

        foreach (var scenario in MatrixCatalog.Scenarios(exhaustive))
        {
            Combinations++;
            var outcome = await RunAsync(scenario);
            foreach (var rule in MatrixRules.All)
            {
                if (!await rule.IsBrokenAsync(scenario, outcome, RunAsync)) continue;
                _broken[rule.Name]++;
                if (_examples[rule.Name].Count < ExamplesKept) _examples[rule.Name].Add($"{scenario.Key} => {outcome.Summary}. {outcome.Reason}");
            }
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public IReadOnlyList<string> BrokenBy(string rule) =>
        _broken[rule] == 0 ? [] : [$"{_broken[rule]} of {Combinations} combinations break this rule", .. _examples[rule]];
}

public class StreamDecisionMatrixTests(StreamDecisionMatrix matrix) : IClassFixture<StreamDecisionMatrix>
{
    public static TheoryData<string> Rules => new(MatrixRules.All.Select(rule => rule.Name));

    [Theory]
    [MemberData(nameof(Rules))]
    public void Every_combination_of_file_client_setting_and_profile_follows_the_rule(string rule)
    {
        string.Join(Environment.NewLine, matrix.BrokenBy(rule)).Should().BeEmpty();
    }

    [Fact]
    public void The_matrix_covers_every_client_profile_and_situation()
    {
        matrix.Combinations.Should().BeGreaterThan(200_000);
    }
}
