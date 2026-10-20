using System;
using System.Collections.Generic;
using Realm.Core.Ids;
using Xunit;

namespace Realm.Core.Tests;

public class ContentIdTests
{
    [Theory]
    [InlineData("material.iron_ore")]
    [InlineData("creature.goblin")]
    [InlineData("dungeon.crypt.sector_3")]
    [InlineData("a.b")]
    [InlineData("item.sword2")]
    public void Parse_AcceptsValidIds(string text)
    {
        ContentId id = ContentId.Parse(text);
        Assert.Equal(text, id.Value);
        Assert.Equal(text, id.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("material")]
    [InlineData(".iron")]
    [InlineData("material.")]
    [InlineData("material..iron")]
    [InlineData("Material.iron")]
    [InlineData("material.Iron")]
    [InlineData("material.iron-ore")]
    [InlineData("material.iron ore")]
    [InlineData("material.1iron")]
    [InlineData("material._iron")]
    [InlineData("material.eisenerzä")]
    public void TryParse_RejectsInvalidIds(string text)
    {
        Assert.False(ContentId.TryParse(text, out ContentId id, out string? error));
        Assert.Equal(default, id);
        Assert.False(string.IsNullOrEmpty(error));
        Assert.Throws<FormatException>(() => ContentId.Parse(text));
    }

    [Fact]
    public void TryParse_RejectsNull()
    {
        Assert.False(ContentId.TryParse(null, out _));
    }

    [Fact]
    public void Type_IsFirstSegment()
    {
        Assert.Equal("dungeon", ContentId.Parse("dungeon.crypt.sector_3").Type);
    }

    [Fact]
    public void Default_IsEmptyAndSafe()
    {
        ContentId id = default;
        Assert.Equal(string.Empty, id.Value);
        Assert.Equal(string.Empty, id.Type);
        Assert.Equal(string.Empty, id.ToString());
    }

    [Fact]
    public void Equality_IsByValue()
    {
        Assert.Equal(ContentId.Parse("material.iron"), ContentId.Parse("material.iron"));
        Assert.True(ContentId.Parse("material.iron") == ContentId.Parse("material.iron"));
        Assert.True(ContentId.Parse("material.iron") != ContentId.Parse("material.tin"));
        Assert.Equal(
            ContentId.Parse("material.iron").GetHashCode(),
            ContentId.Parse("material.iron").GetHashCode());
    }

    [Fact]
    public void Ordering_IsOrdinal()
    {
        var ids = new List<ContentId>
        {
            ContentId.Parse("material.b"),
            ContentId.Parse("item.z"),
            ContentId.Parse("material.a"),
            ContentId.Parse("material.a_b"),
        };

        ids.Sort();

        Assert.Equal(
            new[] { "item.z", "material.a", "material.a_b", "material.b" },
            ids.ConvertAll(i => i.Value));
    }
}
