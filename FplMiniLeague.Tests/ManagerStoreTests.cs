using System.Text;

namespace FplMiniLeague.Tests;

public sealed class ManagerStoreTests : IDisposable
{
    private const string ValidCsv = "GW,OR,OP,GWP,PB,TC,£m\n1,\"1,000\",60,60,4,0,100.0\n";

    private readonly TempDataFolder _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public async Task An_imported_csv_is_saved_and_shown_straight_away()
    {
        var changed = 0;
        _data.Store.Changed += () => changed++;

        var error = await _data.Store.ImportAsync("Anna_Jensen.csv", Stream(ValidCsv));

        Assert.Null(error);
        Assert.True(File.Exists(Path.Combine(_data.Path, "Anna_Jensen.csv")));
        Assert.Equal("Anna Jensen", Assert.Single(_data.Store.Current.Managers).Name);
        Assert.True(changed > 0);
    }

    [Fact]
    public async Task Importing_the_same_file_name_again_replaces_that_manager()
    {
        await _data.Store.ImportAsync("Anna.csv", Stream(ValidCsv));
        await _data.Store.ImportAsync("Anna.csv", Stream(ValidCsv + "2,\"900\",130,70,1,0,100.1\n"));

        var anna = Assert.Single(_data.Store.Current.Managers);
        Assert.Equal(130, anna.Gameweeks[2].OverallPoints);
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_is_not_saved()
    {
        var error = await _data.Store.ImportAsync("Anna.csv", Stream("hello,world\n1,2\n"));

        Assert.NotNull(error);
        Assert.Empty(Directory.GetFiles(_data.Path));
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData(".csv")]
    public async Task Only_named_csv_files_are_accepted(string fileName)
    {
        Assert.NotNull(await _data.Store.ImportAsync(fileName, Stream(ValidCsv)));
        Assert.Empty(Directory.GetFiles(_data.Path));
    }

    [Fact]
    public async Task A_path_in_the_file_name_cannot_escape_the_data_folder()
    {
        await _data.Store.ImportAsync("../../Anna.csv", Stream(ValidCsv));

        Assert.True(File.Exists(Path.Combine(_data.Path, "Anna.csv")));
    }

    [Fact]
    public async Task Deleting_a_manager_removes_their_file()
    {
        await _data.Store.ImportAsync("Anna.csv", Stream(ValidCsv));

        _data.Store.Delete("Anna.csv");

        Assert.Empty(_data.Store.Current.Managers);
        Assert.Empty(Directory.GetFiles(_data.Path));
    }

    private static MemoryStream Stream(string text) => new(Encoding.UTF8.GetBytes(text));
}
