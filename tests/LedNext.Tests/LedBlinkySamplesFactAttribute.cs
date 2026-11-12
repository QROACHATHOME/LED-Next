namespace LedNext.Tests;

/// <summary>
/// A test that needs LEDBlinky's own files in <c>import-samples/ledblinky</c>. They are LEDBlinky's files, not ours
/// to share, so the repository doesn't hold them (they are git-ignored). Copy them from a LEDBlinky install to run
/// these tests (see <c>import-samples/README.md</c>); without them the tests are skipped, not failed.
/// </summary>
public sealed class LedBlinkySamplesFactAttribute : FactAttribute
{
    public static string Folder => Path.Combine(LedBlinkyImportTests.RepoRoot(), "import-samples", "ledblinky");

    public static bool Present =>
        File.Exists(Path.Combine(Folder, "LEDBlinkyControls.xml")) && File.Exists(Path.Combine(Folder, "MameOutputs.ini"));

    public LedBlinkySamplesFactAttribute()
    {
        if (!Present)
            Skip = "Needs LEDBlinky's files in import-samples/ledblinky (not in the repository; see import-samples/README.md).";
    }
}
