namespace Nuvio.Core.Tests;

internal static class TestData
{
    public static string ReadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
}
