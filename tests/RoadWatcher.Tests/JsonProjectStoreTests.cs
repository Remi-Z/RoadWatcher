using RoadWatcher.Core;
using RoadWatcher.Infrastructure;

namespace RoadWatcher.Tests;

public sealed class JsonProjectStoreTests
{
    [Fact]
    public async Task Save_and_open_round_trips_schema_and_identity()
    {
        var directory = Path.Combine(Path.GetTempPath(), "roadwatcher-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var expected = new ProjectDocument { Title = "Toronto ride" };
            var store = new JsonProjectStore();

            await store.SaveAsync(expected, directory);
            var actual = await store.OpenAsync(directory);

            Assert.Equal(1, actual.SchemaVersion);
            Assert.Equal(expected.ProjectId, actual.ProjectId);
            Assert.Equal("Toronto ride", actual.Title);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}

