using FplMiniLeague.Services;

namespace FplMiniLeague.Tests;

public class TeamListTests
{
    [Fact]
    public void Reads_one_id_per_line_and_ignores_notes_and_blank_lines()
    {
        var result = TeamList.Parse("""
            # The league
            2985395   # mikkjal

            1125906
            """);

        Assert.Equal([2985395, 1125906], result.Ids);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Keeps_the_order_of_the_file_and_drops_repeats()
    {
        var result = TeamList.Parse("3\n1\n2\n1\n");

        Assert.Equal([3, 1, 2], result.Ids);
    }

    [Theory]
    [InlineData("https://fantasy.premierleague.com/entry/2985395/history")]
    [InlineData("https://fantasy.premierleague.com/entry/2985395/event/10")]
    [InlineData("fantasy.premierleague.com/entry/2985395")]
    public void A_pasted_fpl_address_works_too(string line) =>
        Assert.Equal([2985395], TeamList.Parse(line).Ids);

    [Fact]
    public void Windows_line_endings_and_a_byte_order_mark_are_fine()
    {
        var result = TeamList.Parse("﻿12\r\n34\r\n");

        Assert.Equal([12, 34], result.Ids);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void A_line_that_is_not_an_id_is_reported_with_its_line_number()
    {
        var result = TeamList.Parse("12\nmikkjal\n34");

        Assert.Equal([12, 34], result.Ids);
        var problem = Assert.Single(result.Problems);
        Assert.Contains("line 2", problem);
        Assert.Contains("\"mikkjal\"", problem);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("99999999999")]
    [InlineData("-5")]
    [InlineData("12.5")]
    public void Numbers_that_cannot_be_team_ids_are_reported(string line) =>
        Assert.Single(TeamList.Parse(line).Problems);

    [Fact]
    public void A_missing_file_is_reported()
    {
        var result = TeamList.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt"));

        Assert.Empty(result.Ids);
        Assert.Contains("wasn't found", Assert.Single(result.Problems));
    }
}
