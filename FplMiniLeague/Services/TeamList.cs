using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace FplMiniLeague.Services;

/// <summary>
/// Reads teams.txt: the FPL team IDs in the mini-league, one per line. Anything after a # is a note.
/// A pasted FPL address works too (the ID is taken from its /entry/ID/ part). The order of the file is
/// the order colours are handed out in, so adding a team never repaints anyone else.
/// </summary>
public static partial class TeamList
{
    public sealed record Result(IReadOnlyList<int> Ids, IReadOnlyList<string> Problems);

    public static Result Read(string path)
    {
        if (!File.Exists(path))
        {
            return new([], [$"{Path.GetFileName(path)} wasn't found at {path}."]);
        }

        return Parse(File.ReadAllText(path), Path.GetFileName(path));
    }

    public static Result Parse(string text, string fileName = "teams.txt")
    {
        var ids = new List<int>();
        var problems = new List<string>();
        var lines = text.TrimStart('﻿').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var hash = line.IndexOf('#');
            if (hash >= 0)
            {
                line = line[..hash];
            }

            foreach (var token in line.Split([' ', '\t', '\r', ',', ';'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (ParseId(token) is int id)
                {
                    if (!ids.Contains(id))
                    {
                        ids.Add(id);
                    }
                }
                else
                {
                    var shown = token.Length > 60 ? token[..60] + "…" : token;
                    problems.Add($"{fileName} line {i + 1}: \"{shown}\" isn't an FPL team ID. Use the number from the team's FPL address, e.g. 2985395 from /entry/2985395/history.");
                }
            }
        }

        return new(ids, problems);
    }

    /// <summary>SHA-256 of the file as it is on disk (empty when it's missing), to notice edits.</summary>
    public static string Hash(string path) =>
        File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant() : "";

    private static int? ParseId(string token)
    {
        if (token.All(char.IsAsciiDigit))
        {
            return int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
        }

        var match = EntryInLink().Match(token);
        return match.Success && int.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var linked) && linked > 0
            ? linked
            : null;
    }

    [GeneratedRegex(@"entry/(\d{1,9})(?:/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex EntryInLink();
}
