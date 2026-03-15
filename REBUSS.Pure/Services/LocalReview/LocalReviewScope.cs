namespace REBUSS.Pure.Services.LocalReview
{
    /// <summary>
    /// Defines the scope of a local self-review: which changes to inspect.
    /// </summary>
    public sealed class LocalReviewScope
    {
        private LocalReviewScope(LocalReviewScopeKind kind, string? baseBranch = null)
        {
            Kind = kind;
            BaseBranch = baseBranch;
        }

        /// <summary>The kind of local change set to review.</summary>
        public LocalReviewScopeKind Kind { get; }

        /// <summary>
        /// The base branch or ref to diff against.
        /// Only used when <see cref="Kind"/> is <see cref="LocalReviewScopeKind.BranchDiff"/>.
        /// </summary>
        public string? BaseBranch { get; }

        /// <summary>
        /// Staged changes only (index vs HEAD).
        /// </summary>
        public static LocalReviewScope Staged() => new(LocalReviewScopeKind.Staged);

        /// <summary>
        /// All uncommitted changes in tracked files (staged + unstaged) vs HEAD.
        /// Untracked files are excluded.
        /// </summary>
        public static LocalReviewScope WorkingTree() => new(LocalReviewScopeKind.WorkingTree);

        /// <summary>
        /// All commits on the current branch that are not yet on <paramref name="baseBranch"/>,
        /// plus any uncommitted changes in the working tree.
        /// </summary>
        public static LocalReviewScope BranchDiff(string baseBranch) =>
            new(LocalReviewScopeKind.BranchDiff, baseBranch);

        /// <summary>
        /// Parses a user-supplied scope string into a <see cref="LocalReviewScope"/>.
        /// Accepted values: "staged", "working-tree" (default), or any branch/ref name
        /// treated as the base for a branch diff.
        /// </summary>
        public static LocalReviewScope Parse(string? scope)
        {
            return scope?.ToLowerInvariant() switch
            {
                null or "" or "working-tree" => WorkingTree(),
                "staged" => Staged(),
                _ => BranchDiff(scope)
            };
        }

        /// <inheritdoc />
        public override string ToString() => Kind switch
        {
            LocalReviewScopeKind.Staged => "staged",
            LocalReviewScopeKind.WorkingTree => "working-tree",
            LocalReviewScopeKind.BranchDiff => $"branch-diff:{BaseBranch}",
            _ => Kind.ToString()
        };
    }

    /// <summary>
    /// Discriminator for <see cref="LocalReviewScope"/>.
    /// </summary>
    public enum LocalReviewScopeKind
    {
        /// <summary>Staged index vs HEAD.</summary>
        Staged,
        /// <summary>Working tree vs HEAD (staged + unstaged).</summary>
        WorkingTree,
        /// <summary>Current branch tip vs a named base branch/ref.</summary>
        BranchDiff
    }
}
