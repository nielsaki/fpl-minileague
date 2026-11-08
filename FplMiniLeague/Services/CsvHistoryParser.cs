using System.Text;
using FplMiniLeague.Models;

namespace FplMiniLeague.Services;

/// <summary>
/// Reads one manager's gameweek-history CSV (columns GW, OR, OP, GWP, PB, TC, £m, in any order).
/// Only GW, OP, GWP and PB are needed for the dashboard; the rest are ignored. Lines before the header
/// row are skipped, the delimiter (comma, semicolon or tab) is detected from the header, and numbers
/// may carry thousands separators ("1,234").
/// </summary>
public static class CsvHistoryParser
{
    public sealed record Result(ManagerHistory? Manager, string? Error);

    public static Result Parse(string text, string managerName, string sourceFile)
    {
        var lines = text.TrimStart('﻿').Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        var headerIndex = -1;
        var delimiter = ',';
        Dictionary<string, int> columns = [];
        for (var i = 0; i < lines.Count && headerIndex < 0; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var candidate = DetectDelimiter(lines[i]);
            var names = SplitLine(lines[i], candidate).Select(NormaliseHeader).ToList();
            if (names.Contains("GW") && names.Contains("OP"))
            {
                headerIndex = i;
                delimiter = candidate;
                for (var c = 0; c < names.Count; c++)
                {
                    columns.TryAdd(names[c], c);
                }
            }
        }

        if (headerIndex < 0)
        {
            return new(null, "no header row with GW and OP columns was found.");
        }

        var missing = new[] { "GWP", "PB" }.Where(c => !columns.ContainsKey(c)).ToList();
        if (missing.Count > 0)
        {
            return new(null, $"the {string.Join(" and ", missing)} column{(missing.Count > 1 ? "s are" : " is")} missing.");
        }

        var entries = new Dictionary<int, GameweekEntry>();
        foreach (var line in lines.Skip(headerIndex + 1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = SplitLine(line, delimiter);
            // A row without a gameweek number, overall points or gameweek points (a totals line, a
            // gameweek not played yet) isn't a gameweek result.
            if (ParseInt(Field(fields, columns["GW"])) is not int gw || gw < 1
                || ParseInt(Field(fields, columns["OP"])) is not int op
                || ParseInt(Field(fields, columns["GWP"])) is not int gwp)
            {
                continue;
            }

            var bench = ParseInt(Field(fields, columns["PB"])) ?? 0;
            entries[gw] = new GameweekEntry(gw, op, gwp, bench);
        }

        if (entries.Count == 0)
        {
            return new(null, "the file has a header but no gameweek rows.");
        }

        return new(new ManagerHistory { Name = managerName, SourceFile = sourceFile, Gameweeks = entries }, null);
    }

    private static string NormaliseHeader(string header) =>
        header.Trim().Trim('"').Replace(" ", "").Replace(".", "").ToUpperInvariant();

    private static string Field(IReadOnlyList<string> fields, int index) => index < fields.Count ? fields[index] : "";

    private static char DetectDelimiter(string line)
    {
        var counts = new Dictionary<char, int> { [','] = 0, [';'] = 0, ['\t'] = 0 };
        var quoted = false;
        foreach (var ch in line)
        {
            if (ch == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && counts.ContainsKey(ch))
            {
                counts[ch]++;
            }
        }

        return counts.MaxBy(kv => kv.Value).Key;
    }

    /// <summary>Splits a CSV line, honouring double quotes ("1,234" stays one field; "" is a quote).</summary>
    private static List<string> SplitLine(string line, char delimiter)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (ch == delimiter && !quoted)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }

    /// <summary>A whole number from a cell such as "57", "1,234", "GW 5" or "-4"; null if it has no digits.</summary>
    private static int? ParseInt(string cell)
    {
        var trimmed = cell.Trim();
        var digits = new string(trimmed.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 0 || !int.TryParse(digits, out var value))
        {
            return null;
        }

        return trimmed.StartsWith('-') || trimmed.StartsWith('−') ? -value : value;
    }
}
