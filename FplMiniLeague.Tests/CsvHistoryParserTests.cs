using FplMiniLeague.Services;

namespace FplMiniLeague.Tests;

public class CsvHistoryParserTests
{
    [Fact]
    public void Reads_the_columns_it_needs_and_ignores_the_rest()
    {
        const string csv = """
            GW,OR,OP,GWP,PB,TC,£m
            1,"1,234,567",65,65,8,0,100.0
            2,"987,654",121,60,3,4,100.4
            """;

        var result = CsvHistoryParser.Parse(csv, "Anna", "Anna.csv");

        Assert.Null(result.Error);
        var gw2 = result.Manager!.Gameweeks[2];
        Assert.Equal(121, gw2.OverallPoints);
        Assert.Equal(60, gw2.GameweekPoints);
        Assert.Equal(3, gw2.PointsOnBench);
    }

    [Fact]
    public void Finds_the_header_below_a_title_line_with_a_BOM_semicolons_and_any_column_order()
    {
        const string csv = "﻿Anna's team\r\n\r\nPB;OP;GW;GWP\r\n2;70;1;70\r\n";

        var result = CsvHistoryParser.Parse(csv, "Anna", "Anna.csv");

        var gw1 = Assert.Single(result.Manager!.Gameweeks).Value;
        Assert.Equal((1, 70, 70, 2), (gw1.Gameweek, gw1.OverallPoints, gw1.GameweekPoints, gw1.PointsOnBench));
    }

    [Fact]
    public void Skips_rows_that_are_not_gameweek_results()
    {
        const string csv = """
            GW,OR,OP,GWP,PB,TC,£m
            1,500000,50,50,4,0,100.0
            ,,,,,,
            2,,,,,,
            Total,,50,50,4,0,
            """;

        var result = CsvHistoryParser.Parse(csv, "Anna", "Anna.csv");

        Assert.Equal([1], result.Manager!.Gameweeks.Keys);
    }

    [Fact]
    public void Reports_missing_columns()
    {
        var result = CsvHistoryParser.Parse("GW,OR,OP,TC\n1,1,50,0", "Anna", "Anna.csv");

        Assert.Null(result.Manager);
        Assert.Contains("GWP and PB", result.Error);
    }

    [Fact]
    public void Reports_a_file_without_a_header()
    {
        var result = CsvHistoryParser.Parse("1,2,3\n4,5,6", "Anna", "Anna.csv");

        Assert.Null(result.Manager);
        Assert.NotNull(result.Error);
    }
}
