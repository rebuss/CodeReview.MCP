using DiffPlex;

namespace REBUSS.Pure.Core.Shared;

/// <summary>
/// Myers-based diff algorithm backed by <see cref="Differ"/> from the DiffPlex library.
/// Produces a minimal edit list compatible with <see cref="IDiffAlgorithm"/>.
/// </summary>
public class DiffPlexDiffAlgorithm : IDiffAlgorithm
{
    private readonly Differ _differ = new();

    public IReadOnlyList<DiffEdit> ComputeEdits(string[] oldLines, string[] newLines)
    {
        var oldText = string.Join('\n', oldLines);
        var newText = string.Join('\n', newLines);

        var diffResult = _differ.CreateLineDiffs(oldText, newText, ignoreWhitespace: false);

        var edits = new List<DiffEdit>(oldLines.Length + newLines.Length);
        int oldIdx = 0;
        int newIdx = 0;

        foreach (var block in diffResult.DiffBlocks)
        {
            // Context lines before this block
            while (oldIdx < block.DeleteStartA && newIdx < block.InsertStartB)
                edits.Add(new DiffEdit(' ', oldIdx++, newIdx++));

            // Deleted lines
            for (int i = 0; i < block.DeleteCountA; i++)
                edits.Add(new DiffEdit('-', oldIdx++, newIdx));

            // Inserted lines
            for (int i = 0; i < block.InsertCountB; i++)
                edits.Add(new DiffEdit('+', oldIdx, newIdx++));
        }

        // Trailing context lines
        while (oldIdx < oldLines.Length && newIdx < newLines.Length)
            edits.Add(new DiffEdit(' ', oldIdx++, newIdx++));

        return edits;
    }
}
