namespace DbTools.Core;

/// <summary>Tình trạng chờ / chặn của 1 phiên.</summary>
public enum BlockState
{
    None,
    /// <summary>Đang chặn phiên khác (giữ khoá người khác cần).</summary>
    Blocking,
    /// <summary>Đang chờ khoá do phiên khác giữ.</summary>
    Blocked,
}

/// <summary>1 phiên tối giản để tính ai chặn ai (unit test không cần Npgsql).</summary>
public sealed record SessionLink(int Pid, string State, IReadOnlyList<int> BlockedBy, bool IsSelf = false);

/// <summary>
/// Trang Phiên kết nối: ai chặn ai (từ pg_blocking_pids), chữ hiện ở cột Chờ / chặn, thời gian chạy, lọc "chỉ phiên đang chạy /
/// chặn / bị chặn". Không phụ thuộc DB / giao diện.
/// </summary>
public static class SessionBlocking
{
    /// <summary>PID → các PID nó đang chặn.</summary>
    public static Dictionary<int, List<int>> Blocks(IEnumerable<SessionLink> sessions)
    {
        var result = new Dictionary<int, List<int>>();
        foreach (var s in sessions)
        {
            foreach (var blocker in s.BlockedBy.Distinct())
            {
                if (!result.TryGetValue(blocker, out var list))
                {
                    result[blocker] = list = [];
                }
                list.Add(s.Pid);
            }
        }
        return result;
    }

    /// <summary>Đầu chuỗi chặn của 1 phiên bị chặn: đi ngược theo BlockedBy tới phiên không bị ai chặn (bỏ vòng lặp khoá chết).
    /// Phiên không bị chặn → rỗng.</summary>
    public static List<int> RootBlockers(int pid, IReadOnlyDictionary<int, SessionLink> byPid)
    {
        var roots = new List<int>();
        var seen = new HashSet<int> { pid };
        var queue = new Queue<int>(byPid.TryGetValue(pid, out var s) ? s.BlockedBy : []);
        while (queue.Count > 0)
        {
            var b = queue.Dequeue();
            if (!seen.Add(b))
            {
                continue;
            }
            if (byPid.TryGetValue(b, out var bs) && bs.BlockedBy.Count > 0 && !bs.BlockedBy.All(seen.Contains))
            {
                foreach (var next in bs.BlockedBy)
                {
                    queue.Enqueue(next);
                }
            }
            else
            {
                roots.Add(b);
            }
        }
        return roots;
    }

    public static BlockState StateOf(SessionLink s, IReadOnlyDictionary<int, List<int>> blocks) =>
        s.BlockedBy.Count > 0 ? BlockState.Blocked
        : blocks.ContainsKey(s.Pid) ? BlockState.Blocking
        : BlockState.None;

    /// <summary>Chữ ở cột Chờ / chặn. Bị chặn và đồng thời chặn người khác → ghi cả hai.</summary>
    public static string BlockText(SessionLink s, IReadOnlyDictionary<int, List<int>> blocks, string waitEventType, string waitEvent)
    {
        var parts = new List<string>();
        if (s.BlockedBy.Count > 0)
        {
            parts.Add($"⛔ chờ khoá, bị PID {string.Join(", ", s.BlockedBy.Distinct())} chặn");
        }
        if (blocks.TryGetValue(s.Pid, out var victims))
        {
            parts.Add($"🔒 đang chặn {victims.Count} phiên");
        }
        if (parts.Count == 0 && waitEventType.Length > 0 && waitEventType != "Client" && s.State == "active")
        {
            parts.Add($"chờ {waitEventType} / {waitEvent}");
        }
        return string.Join(" · ", parts);
    }

    /// <summary>"8 giây", "3 phút 05 giây", "1 giờ 02 phút", "2 ngày 3 giờ".</summary>
    public static string Duration(double? seconds)
    {
        if (seconds is not { } s || s < 0)
        {
            return string.Empty;
        }
        var t = TimeSpan.FromSeconds(Math.Floor(s));
        return t.TotalDays >= 1 ? $"{(int)t.TotalDays} ngày {t.Hours} giờ"
            : t.TotalHours >= 1 ? $"{(int)t.TotalHours} giờ {t.Minutes:00} phút"
            : t.TotalMinutes >= 1 ? $"{t.Minutes} phút {t.Seconds:00} giây"
            : s < 1 ? "< 1 giây"
            : $"{t.Seconds} giây";
    }

    /// <summary>Ô lọc "Chỉ phiên đang chạy / chặn / bị chặn": câu đang chạy (trừ phiên của DbTools), đang chặn, bị chặn.
    /// Rảnh trong transaction mà đang chặn người khác vẫn hiện (thủ phạm hay gặp).</summary>
    public static bool IsBusy(SessionLink s, IReadOnlyDictionary<int, List<int>> blocks) =>
        (s.State == "active" && !s.IsSelf) || s.BlockedBy.Count > 0 || blocks.ContainsKey(s.Pid);
}
