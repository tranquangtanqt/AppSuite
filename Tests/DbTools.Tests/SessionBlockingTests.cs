using DbTools.Core;
using DbTools.Models;
using DbTools.Services;
using Npgsql;

namespace DbTools.Tests;

/// <summary>Phiên kết nối: ai chặn ai, đầu chuỗi chặn, chữ cột Chờ / chặn, thời gian, lọc.</summary>
public class SessionBlockingTests
{
    private static readonly SessionLink A = new(10, "idle in transaction", []);
    private static readonly SessionLink B = new(20, "active", [10]);
    private static readonly SessionLink C = new(30, "active", [20]);
    private static readonly SessionLink D = new(40, "idle", []);
    private static readonly SessionLink Self = new(50, "active", [], IsSelf: true);

    [Fact]
    public void Who_blocks_whom()
    {
        var blocks = SessionBlocking.Blocks([A, B, C, D]);
        Assert.Equal([20], blocks[10]);
        Assert.Equal([30], blocks[20]);
        Assert.False(blocks.ContainsKey(40));
        Assert.Equal(BlockState.Blocking, SessionBlocking.StateOf(A, blocks));
        Assert.Equal(BlockState.Blocked, SessionBlocking.StateOf(B, blocks)); // bị chặn ưu tiên hơn đang chặn
        Assert.Equal(BlockState.None, SessionBlocking.StateOf(D, blocks));
    }

    [Fact]
    public void Root_blockers_follow_the_chain_and_survive_cycles()
    {
        var byPid = new[] { A, B, C, D }.ToDictionary(s => s.Pid);
        Assert.Equal([10], SessionBlocking.RootBlockers(30, byPid));
        Assert.Equal([10], SessionBlocking.RootBlockers(20, byPid));
        Assert.Empty(SessionBlocking.RootBlockers(10, byPid));

        // khoá chết: 1 chờ 2, 2 chờ 1 → không lặp mãi, trả 1 phiên trong vòng
        var cycle = new[] { new SessionLink(1, "active", [2]), new SessionLink(2, "active", [1]) }.ToDictionary(s => s.Pid);
        Assert.Equal([2], SessionBlocking.RootBlockers(1, cycle));
        // bị 1 phiên không có trong danh sách chặn (đang lọc theo database khác) → chính phiên đó là đầu chuỗi
        Assert.Equal([99], SessionBlocking.RootBlockers(5, new[] { new SessionLink(5, "active", [99]) }.ToDictionary(s => s.Pid)));
    }

    [Fact]
    public void Block_text()
    {
        var blocks = SessionBlocking.Blocks([A, B, C, D]);
        Assert.Equal("🔒 đang chặn 1 phiên", SessionBlocking.BlockText(A, blocks, "Client", "ClientRead"));
        Assert.Equal("⛔ chờ khoá, bị PID 10 chặn · 🔒 đang chặn 1 phiên", SessionBlocking.BlockText(B, blocks, "Lock", "transactionid"));
        Assert.Equal("", SessionBlocking.BlockText(D, blocks, "Client", "ClientRead"));
        Assert.Equal("chờ IO / DataFileRead", SessionBlocking.BlockText(new SessionLink(60, "active", []), blocks, "IO", "DataFileRead"));
        Assert.Equal("", SessionBlocking.BlockText(new SessionLink(60, "idle", []), blocks, "IO", "DataFileRead"));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData(-1.0, "")]
    [InlineData(0.4, "< 1 giây")]
    [InlineData(8.9, "8 giây")]
    [InlineData(185.0, "3 phút 05 giây")]
    [InlineData(3720.0, "1 giờ 02 phút")]
    [InlineData(183600.0, "2 ngày 3 giờ")]
    public void Duration_text(double? seconds, string expected) => Assert.Equal(expected, SessionBlocking.Duration(seconds));

    [Fact]
    public void Busy_filter_keeps_running_blocking_and_blocked()
    {
        var blocks = SessionBlocking.Blocks([A, B, C, D, Self]);
        Assert.True(SessionBlocking.IsBusy(A, blocks)); // rảnh trong transaction nhưng đang chặn
        Assert.True(SessionBlocking.IsBusy(B, blocks));
        Assert.True(SessionBlocking.IsBusy(C, blocks));
        Assert.False(SessionBlocking.IsBusy(D, blocks));
        Assert.False(SessionBlocking.IsBusy(Self, blocks)); // phiên DbTools dùng để đọc danh sách
    }
}

/// <summary>Chặn khoá thật trên PostgreSQL (cần DBTOOLS_TEST_PG): phiên A giữ khoá, B chờ → thấy B bị A chặn + khoá đang chờ;
/// Huỷ câu của B; A rảnh trong transaction: huỷ câu không gỡ được, ngắt phiên thì được.</summary>
public sealed class SessionBlockingIntegrationTests : IAsyncLifetime
{
    private static readonly string? Server = Environment.GetEnvironmentVariable("DBTOOLS_TEST_PG");
    private readonly string _db = "dbtools_lock_" + Guid.NewGuid().ToString("N")[..10];
    private ConnectionProfile _p = new();

    public async ValueTask InitializeAsync()
    {
        if (string.IsNullOrEmpty(Server))
        {
            return;
        }
        var b = new NpgsqlConnectionStringBuilder(Server);
        var admin = new ConnectionProfile { Host = b.Host!, Port = b.Port, Username = b.Username!, Password = b.Password ?? "", Database = "postgres" };
        await PgAdmin.CreateDatabaseAsync(admin, _db, CancellationToken.None);
        _p = PgAdmin.On(admin, _db);
        await using var c = await Pg.OpenAsync(_p, CancellationToken.None);
        await using var batch = new NpgsqlBatch(c) { BatchCommands = { new("CREATE TABLE item (id int PRIMARY KEY, n int)"), new("INSERT INTO item VALUES (1, 0)") } };
        await batch.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (string.IsNullOrEmpty(Server))
        {
            return;
        }
        var b = new NpgsqlConnectionStringBuilder(Server) { Database = "postgres", Pooling = false };
        await using var c = new NpgsqlConnection(b.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_db} WITH (FORCE)", c);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task Exec(NpgsqlConnection c, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Sees_blocking_cancels_and_terminates()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Server), "Chưa đặt DBTOOLS_TEST_PG.");
        var ct = TestContext.Current.CancellationToken;
        await using var a = await Pg.OpenAsync(_p, ct);
        await using var bConn = await Pg.OpenAsync(_p, ct);
        await Exec(a, "BEGIN");
        await Exec(a, "UPDATE item SET n = 1 WHERE id = 1");
        var aPid = a.ProcessID;
        var bPid = bConn.ProcessID;
        var waiting = Exec(bConn, "UPDATE item SET n = 2 WHERE id = 1"); // bị A chặn

        PgSession? bRow = null;
        for (var i = 0; i < 40 && bRow?.BlockedBy.Count is not > 0; i++)
        {
            await Task.Delay(100, ct);
            bRow = (await PgAdmin.ListSessionsAsync(_p, _db, ct)).SingleOrDefault(s => s.Pid == bPid);
        }
        Assert.NotNull(bRow);
        Assert.Equal([aPid], bRow!.BlockedBy);
        Assert.Equal("Lock", bRow.WaitEventType);
        Assert.True(bRow.QuerySeconds >= 0);
        var sessions = await PgAdmin.ListSessionsAsync(_p, _db, ct);
        var aRow = sessions.Single(s => s.Pid == aPid);
        Assert.Equal("idle in transaction", aRow.State);
        Assert.Null(aRow.QuerySeconds); // không có câu đang chạy
        Assert.True(aRow.XactSeconds >= 0);
        Assert.Equal([aPid], SessionBlocking.RootBlockers(bPid, sessions.ToDictionary(s => s.Pid, s => new SessionLink(s.Pid, s.State, s.BlockedBy))));

        var waits = await PgAdmin.LockWaitsAsync(_p, _db, bPid, ct);
        Assert.Contains(waits, w => w.LockType is "transactionid" or "tuple");

        // Huỷ câu của B: B nhận lỗi 57014, vẫn kết nối
        Assert.Equal(1, await PgAdmin.CancelAsync(_p, [bPid], ct));
        var ex = await Assert.ThrowsAsync<PostgresException>(() => waiting);
        Assert.Equal("57014", ex.SqlState);
        Assert.Equal(System.Data.ConnectionState.Open, bConn.State);

        // A rảnh trong transaction: huỷ câu không làm gì, A vẫn giữ khoá; ngắt phiên thì khoá được thả
        await PgAdmin.CancelAsync(_p, [aPid], ct);
        await Task.Delay(300, ct);
        Assert.Contains(await PgAdmin.ListSessionsAsync(_p, _db, ct), s => s.Pid == aPid && s.State == "idle in transaction");
        Assert.Equal(1, await PgAdmin.TerminateAsync(_p, [aPid], ct));
        await Exec(bConn, "UPDATE item SET n = 2 WHERE id = 1"); // không còn bị chặn
    }
}
