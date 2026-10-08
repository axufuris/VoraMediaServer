using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public class StorageReferenceRepositoryTests
{
    [Theory]
    [InlineData("/api/artwork/custom/", "/api/artwork/custom/")]
    [InlineData("/app/data/custom_artwork/", "/app/data/custom_artwork/")]
    [InlineData("C:/data (old)/art.v2/", @"C:/data \(old\)/art\.v2/")]
    public void Prefixes_are_matched_literally_by_the_database_regex(string prefix, string expected)
    {
        StorageReferenceRepository.RegexLiteral(prefix).Should().Be(expected);
    }

    [Theory]
    [InlineData("/app/data/custom_artwork/", @"/app/data/custom\_artwork/")]
    [InlineData("/data/100%/", @"/data/100\%/")]
    public void Prefixes_are_matched_literally_by_like(string prefix, string expected)
    {
        StorageReferenceRepository.LikeLiteral(prefix).Should().Be(expected);
    }
}
