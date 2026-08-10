using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ModuleB.Models;

namespace ModuleB.Services;

/// <summary>
/// Indexes .xlsx/.xlsm files under a root folder into SQLite (per-cell/shape text + sheet/cell
/// location + last-write time), so MainViewModel.Search reads cached content instead of re-parsing
/// every workbook on every search, and FileMatchesDialog can list exactly which cells matched.
/// A file is only re-parsed when its LastWriteTimeUtc is newer than what was last indexed, or when it
/// was indexed under an older <see cref="CurrentIndexVersion"/> - bump that constant whenever
/// ExcelCellExtractor/ExcelShapeExtractor's extraction logic changes, so already-indexed files get
/// backfilled even though their LastWriteTimeUtc on disk hasn't moved.
/// </summary>
public sealed class ExcelIndexService
{
    private const int CurrentIndexVersion = 2;

    private readonly FileIndexRepository _repository = new();

    public Task IndexFolderAsync(string rootPath) => Task.Run(() => IndexFolder(rootPath));

    private void IndexFolder(string rootPath)
    {
        var excelFiles = Directory.EnumerateFiles(rootPath, "*.xlsx", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(rootPath, "*.xlsm", SearchOption.AllDirectories))
            .ToList();

        var seenPaths = new HashSet<string>(excelFiles, StringComparer.OrdinalIgnoreCase);

        foreach (var file in excelFiles)
        {
            var lastWriteUtc = File.GetLastWriteTimeUtc(file);
            var indexedAt = _repository.GetLastWriteTimeUtc(file);
            var indexedVersion = _repository.GetIndexVersion(file);
            if (indexedAt.HasValue && indexedAt.Value >= lastWriteUtc && indexedVersion == CurrentIndexVersion)
            {
                continue;
            }

            var relative = Path.GetRelativePath(rootPath, file);
            var segments = relative.Split(Path.DirectorySeparatorChar);
            var group = segments.Length > 1 ? segments[0] : "(root)";

            var cells = ExcelCellExtractor.ExtractCells(file);
            var content = string.Join('\n', cells.Select(c => c.Text));

            _repository.Upsert(rootPath, group, file, Path.GetFileName(file), content, lastWriteUtc, CurrentIndexVersion);
            _repository.ReplaceCells(file, cells);
        }

        _repository.DeleteMissing(rootPath, seenPaths);
    }

    /// <summary>
    /// Returns matches as plain data (group name + path), not <see cref="SearchResultItem"/>: that
    /// type builds a SolidColorBrush, a WinUI object that must be constructed on the UI thread, and
    /// this method is meant to run off-thread via Task.Run. Callers build SearchResultItem after
    /// hopping back to the UI thread.
    /// </summary>
    public List<(string GroupName, string FullPath)> Search(string rootPath, string groupFilter, string keywordQuery)
    {
        var results = new List<(string GroupName, string FullPath)>();

        foreach (var file in _repository.GetByRoot(rootPath))
        {
            if (!QueryMatcher.Matches(file.GroupName, groupFilter))
            {
                continue;
            }

            var content = string.IsNullOrEmpty(file.Content) ? file.FileName : file.Content;
            if (!QueryMatcher.Matches(content, keywordQuery))
            {
                continue;
            }

            results.Add((file.GroupName, file.FullPath));
        }

        return results
            .OrderBy(r => r.GroupName)
            .ThenBy(r => Path.GetFileName(r.FullPath))
            .ToList();
    }

    /// <summary>
    /// Lists the cells of an already-indexed file that contain any of the search keyword's terms
    /// (AND/OR grouping is ignored here - a term can legitimately live in a different cell than the
    /// other terms of its AND group). Falls back to every non-empty cell when no keyword was typed.
    /// </summary>
    public List<CellMatchItem> GetMatchingCells(string fullPath, string keywordQuery)
    {
        var terms = QueryMatcher.ExtractTerms(keywordQuery);
        var cells = _repository.GetCells(fullPath);

        var matched = terms.Count == 0
            ? cells
            : cells.Where(c => terms.Any(t => c.Text.Contains(t, StringComparison.OrdinalIgnoreCase))).ToList();

        return matched
            .OrderBy(c => c.SheetName)
            .ThenBy(c => c.RowIndex)
            .ThenBy(c => c.ColumnIndex)
            .Select(c => new CellMatchItem(fullPath, c.SheetName, c.CellReference, c.Text))
            .ToList();
    }
}
