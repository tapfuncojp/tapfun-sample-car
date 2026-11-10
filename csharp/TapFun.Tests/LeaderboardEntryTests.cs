namespace TapFun.Tests;

public class LeaderboardEntryTests
{
    [Fact]
    public void ReadsTheEntries()
    {
        // As GET /api/v1/leaderboard/entries returns it. note is null when no note was saved.
        const string json = """
            {"result":"success","entries":[
              {"position":1,"userId":"PLYR1","score":9000,"note":"from TapFunSample","createdAt":"2026-11-09T06:00:00.000Z","updatedAt":"2026-11-09T06:00:00.000Z","userName":"\u3042\u3044"},
              {"position":2,"userId":"PLYR2","score":"9007199254740993","note":null,"createdAt":"2026-11-09T06:00:00.000Z","updatedAt":"2026-11-09T06:00:00.000Z","userName":"b"}
            ]}
            """;

        List<LeaderboardEntry> entries = LeaderboardEntry.ParseEntries(json);

        Assert.Equal(2, entries.Count);
        Assert.Equal((1, "PLYR1", "あい", 9000L, "from TapFunSample"), (entries[0].Position, entries[0].UserId, entries[0].UserName, entries[0].Score, entries[0].Note));
        Assert.Equal((2, "PLYR2", "b", 9007199254740993L, ""), (entries[1].Position, entries[1].UserId, entries[1].UserName, entries[1].Score, entries[1].Note));
    }

    [Theory]
    [InlineData("{\"result\":\"success\",\"entries\":[]}")]
    [InlineData("{\"result\":\"failure\",\"message\":\"name is missing\"}")]
    public void NoEntries(string json)
    {
        Assert.Empty(LeaderboardEntry.ParseEntries(json));
    }

    [Theory]
    [InlineData("<html>")]
    [InlineData("{\"entries\":{}}")]
    [InlineData("{\"entries\":[1]}")]
    [InlineData("{\"entries\":[{\"position\":1,\"score\":\"x\"}]}")]
    public void RejectsOtherBodies(string json)
    {
        Assert.Throws<FormatException>(() => LeaderboardEntry.ParseEntries(json));
    }
}
