using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Ai;
using Vora.Application.Collections;
using Vora.Domain.Entities.Collections;

namespace Vora.Application.Tests.Ai;

public class OpenAiThemeProviderTests
{
    private const string HalloweenProfile = "{\"profile\":\"Films set on Halloween night: trick-or-treating, costumes and haunted houses.\",\"keywords\":[\"halloween\"]}";

    private readonly IOpenAiClient _ai = Substitute.For<IOpenAiClient>();
    private readonly FakeThemes _themes = new();
    private readonly Guid _collection = Guid.NewGuid();
    private readonly Guid _library = Guid.NewGuid();
    private readonly List<string> _profilePrompts = new();
    private readonly List<string> _judgePrompts = new();
    private string? _profileAnswer = HalloweenProfile;
    private string[] _belonging = Array.Empty<string>();

    private readonly ThemeCandidate _hocusPocus = Title("Hocus Pocus", 1993, "After moving to Salem, a teenager lights a candle that brings back three witches on Halloween night.");
    private readonly ThemeCandidate _trickRTreat = Title("Trick 'r Treat", 2007, "Four interwoven stories of Halloween night in a small Ohio town.");
    private readonly ThemeCandidate _saw = Title("Saw", 2004, "Two men wake up chained in a filthy bathroom.");
    private readonly ThemeCandidate _beetlejuice = Title("Beetlejuice", 1988, "A recently deceased couple hire a bio-exorcist to scare away the new owners of their house.");

    public OpenAiThemeProviderTests()
    {
        _ai.GetEmbeddingModelAsync().Returns("text-embedding-3-small");
        _ai.EmbedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ => (IReadOnlyList<float[]?>?)new List<float[]?> { new[] { 0.1f, 0.2f } });
        _ai.CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<double?>(), Arg.Any<string?>(), Arg.Any<Guid?>())
            .Returns(call =>
            {
                var prompt = call.ArgAt<string>(1);
                if (prompt.Contains("Decide which belong"))
                {
                    _judgePrompts.Add(prompt);
                    return Belonging(prompt, _belonging);
                }
                _profilePrompts.Add(prompt);
                return _profileAnswer;
            });

        foreach (var title in new[] { _hocusPocus, _trickRTreat, _saw, _beetlejuice }) _themes.Titles[title.Id] = title;
    }

    private static ThemeCandidate Title(string title, int year, string overview) =>
        new(Guid.NewGuid(), title, year, false, overview, new List<string> { "Horror" });

    private OpenAiThemeProvider Provider() => new(_ai, _themes, NullLogger<OpenAiThemeProvider>.Instance);

    private Task<IReadOnlyCollection<Guid>> SyncAsync(string theme = "Halloween") =>
        Provider().FindInLibraryAsync(new LibraryCollectionSyncRequest(_collection, theme, _library), TestContext.Current.CancellationToken);

    private static string Belonging(string prompt, string[] titles)
    {
        var indexes = prompt.Split('\n')
            .Select(line => Regex.Match(line, @"^(\d+)\. (.+?) \(\d{4}\) ·"))
            .Where(match => match.Success && titles.Contains(match.Groups[2].Value))
            .Select(match => match.Groups[1].Value);
        return "{\"belongs\":[" + string.Join(",", indexes) + "]}";
    }

    [Fact]
    public async Task Titles_tmdb_tags_with_the_theme_go_in_and_the_closest_others_are_checked_by_the_ai()
    {
        _themes.KeywordMatches.Add(_hocusPocus.Id);
        _themes.Nearest.AddRange(new[] { _hocusPocus.Id, _trickRTreat.Id, _saw.Id });
        _belonging = new[] { "Trick 'r Treat" };

        var members = await SyncAsync();

        members.Should().BeEquivalentTo(new[] { _hocusPocus.Id, _trickRTreat.Id });
        _themes.AskedKeywords.Should().Equal("halloween");
        _judgePrompts.Should().ContainSingle();
        _judgePrompts[0].Should().Contain("Trick 'r Treat").And.Contain("Saw").And.NotContain("Hocus Pocus");
        _themes.Verdicts.Should().BeEquivalentTo(new Dictionary<Guid, bool> { [_trickRTreat.Id] = true, [_saw.Id] = false });
    }

    [Fact]
    public async Task The_answers_are_kept_so_a_second_sync_asks_the_ai_nothing()
    {
        _themes.Nearest.AddRange(new[] { _trickRTreat.Id, _saw.Id });
        _belonging = new[] { "Trick 'r Treat" };
        await SyncAsync();
        _profilePrompts.Clear();
        _judgePrompts.Clear();

        var members = await SyncAsync();

        members.Should().BeEquivalentTo(new[] { _trickRTreat.Id });
        _profilePrompts.Should().BeEmpty();
        _judgePrompts.Should().BeEmpty();
    }

    [Fact]
    public async Task Only_a_title_new_to_the_closest_ones_is_asked_about_next_time()
    {
        _themes.Nearest.AddRange(new[] { _trickRTreat.Id, _saw.Id });
        _belonging = new[] { "Trick 'r Treat", "Beetlejuice" };
        await SyncAsync();
        _judgePrompts.Clear();
        _themes.Nearest.Add(_beetlejuice.Id);

        var members = await SyncAsync();

        members.Should().BeEquivalentTo(new[] { _trickRTreat.Id, _beetlejuice.Id });
        _judgePrompts.Should().ContainSingle();
        _judgePrompts[0].Should().Contain("Beetlejuice").And.NotContain("Trick 'r Treat").And.NotContain("Saw");
    }

    [Fact]
    public async Task Changing_the_theme_starts_over()
    {
        _themes.Stored = new CollectionTheme { CollectionId = _collection, Theme = "Christmas", Profile = "Films set at Christmas.", Keywords = new() { "christmas" }, Embedding = new[] { 0.3f }, EmbeddingModel = "text-embedding-3-small" };
        _themes.Verdicts[_saw.Id] = true;
        _themes.Nearest.AddRange(new[] { _trickRTreat.Id, _saw.Id });
        _belonging = new[] { "Trick 'r Treat" };

        var members = await SyncAsync("Halloween");

        _profilePrompts.Should().ContainSingle().Which.Should().Contain("\"Halloween\"");
        _themes.Forgotten.Should().Be(1);
        (_themes.Stored?.Theme).Should().Be("Halloween");
        members.Should().BeEquivalentTo(new[] { _trickRTreat.Id });
    }

    [Fact]
    public async Task Without_an_embedding_model_only_the_tagged_titles_go_in()
    {
        _ai.GetEmbeddingModelAsync().Returns((string?)null);
        _themes.KeywordMatches.Add(_hocusPocus.Id);
        _themes.Nearest.AddRange(new[] { _trickRTreat.Id });

        var members = await SyncAsync();

        members.Should().BeEquivalentTo(new[] { _hocusPocus.Id });
        _judgePrompts.Should().BeEmpty();
        await _ai.DidNotReceive().EmbedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_new_embedding_model_embeds_the_kept_profile_again_and_keeps_the_answers()
    {
        _themes.Stored = new CollectionTheme { CollectionId = _collection, Theme = "Halloween", Profile = "Films set on Halloween night.", Keywords = new() { "halloween" }, Embedding = new[] { 0.9f }, EmbeddingModel = "old-model" };
        _themes.Verdicts[_trickRTreat.Id] = true;

        var members = await SyncAsync();

        _profilePrompts.Should().BeEmpty();
        await _ai.Received(1).EmbedAsync(OpenAiThemeProvider.ProviderId, Arg.Is<IReadOnlyList<string>>(inputs => inputs.Single() == "Films set on Halloween night."), Arg.Any<CancellationToken>());
        (_themes.Stored?.EmbeddingModel).Should().Be("text-embedding-3-small");
        _themes.Forgotten.Should().Be(0);
        members.Should().Contain(_trickRTreat.Id);
    }

    [Fact]
    public async Task No_profile_from_the_ai_leaves_the_collection_alone()
    {
        _profileAnswer = null;
        _themes.KeywordMatches.Add(_hocusPocus.Id);

        var members = await SyncAsync();

        members.Should().BeEmpty();
        _themes.Stored.Should().BeNull();
    }

    [Fact]
    public async Task The_theme_s_keywords_are_kept_once_each_in_lowercase()
    {
        _profileAnswer = "{\"profile\":\"Films set on Halloween night.\",\"keywords\":[\"Halloween\",\" Trick or Treating \",\"halloween\",\"\"]}";

        await SyncAsync();

        (_themes.Stored?.Keywords).Should().Equal("halloween", "trick or treating");
    }

    private sealed class FakeThemes : ICollectionThemeRepository
    {
        public CollectionTheme? Stored;
        public readonly Dictionary<Guid, bool> Verdicts = new();
        public readonly List<Guid> KeywordMatches = new();
        public readonly List<Guid> Nearest = new();
        public readonly Dictionary<Guid, ThemeCandidate> Titles = new();
        public int Forgotten;
        public List<string>? AskedKeywords;

        public Task<CollectionTheme?> GetAsync(Guid collectionId) => Task.FromResult(Stored);

        public Task SaveAsync(CollectionTheme theme, bool forgetVerdicts)
        {
            Stored = theme;
            if (forgetVerdicts)
            {
                Verdicts.Clear();
                Forgotten++;
            }
            return Task.CompletedTask;
        }

        public Task<Dictionary<Guid, bool>> GetVerdictsAsync(Guid collectionId) => Task.FromResult(new Dictionary<Guid, bool>(Verdicts));

        public Task SaveVerdictsAsync(Guid collectionId, IReadOnlyDictionary<Guid, bool> verdicts)
        {
            foreach (var (id, belongs) in verdicts) Verdicts[id] = belongs;
            return Task.CompletedTask;
        }

        public Task<List<Guid>> FindByKeywordsAsync(IReadOnlyCollection<string> keywords, Guid? libraryId)
        {
            AskedKeywords = keywords.ToList();
            return Task.FromResult(KeywordMatches.ToList());
        }

        public Task<List<Guid>> FindNearestAsync(float[] vector, string embeddingModel, Guid? libraryId, int limit) =>
            Task.FromResult(Nearest.Take(limit).ToList());

        public Task<List<ThemeCandidate>> GetCandidatesAsync(IReadOnlyCollection<Guid> mediaItemIds) =>
            Task.FromResult(mediaItemIds.Select(id => Titles[id]).ToList());
    }
}
