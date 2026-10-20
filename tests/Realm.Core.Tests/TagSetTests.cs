using System;
using System.Linq;
using Realm.Core.Ids;
using Xunit;

namespace Realm.Core.Tests;

public class TagSetTests
{
    [Fact]
    public void Enumerates_InSortedOrder_AndDeduplicates()
    {
        TagSet tags = TagSet.From(new[] { "metal", "cold", "blessed", "cold" });

        Assert.Equal(new[] { "blessed", "cold", "metal" }, tags.ToArray());
        Assert.Equal(3, tags.Count);
    }

    [Fact]
    public void Contains_FindsMembersOnly()
    {
        TagSet tags = TagSet.From(new[] { "metal", "cold" });

        Assert.True(tags.Contains("metal"));
        Assert.False(tags.Contains("hot"));
        Assert.False(tags.Contains("Metal"));
    }

    [Fact]
    public void Empty_HasNoTags()
    {
        Assert.Empty(TagSet.From(Array.Empty<string>()));
        Assert.Same(TagSet.Empty, TagSet.From(Array.Empty<string>()));
        Assert.False(TagSet.Empty.Contains("metal"));
    }

    [Theory]
    [InlineData("Metal")]
    [InlineData("two words")]
    [InlineData("a.b")]
    [InlineData("")]
    [InlineData("1st")]
    public void From_RejectsInvalidTags(string bad)
    {
        Assert.Throws<FormatException>(() => TagSet.From(new[] { "ok", bad }));
    }

    [Fact]
    public void From_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => TagSet.From(null!));
    }
}
