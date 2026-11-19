using Ardea.Platform.Persistence;

namespace Ardea.Platform.Tests.Persistence;

public sealed class MasterDataOptionsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ardea-master-data-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void A_data_folder_without_a_catalogue_next_to_the_program_is_skipped_for_the_repository_one()
    {
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(_root).FullName, "Ardea.slnx"), "");
        Directory.CreateDirectory(Path.Combine(_root, "data", "catalog"));
        var program = Directory.CreateDirectory(Path.Combine(_root, "tests", "bin")).FullName;
        Directory.CreateDirectory(Path.Combine(program, "Data"));

        Assert.Equal(Path.Combine(_root, "data"), MasterDataOptions.FindDefault(program));
    }

    [Fact]
    public void A_data_folder_with_a_catalogue_next_to_the_program_wins()
    {
        var program = Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;
        Directory.CreateDirectory(Path.Combine(program, "data", "catalog"));

        Assert.Equal(Path.Combine(program, "data"), MasterDataOptions.FindDefault(program));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
