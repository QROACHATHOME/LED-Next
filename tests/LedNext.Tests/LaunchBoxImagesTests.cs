using LedNext.Core.Import.LaunchBox;

namespace LedNext.Tests;

public sealed class LaunchBoxImagesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lednext-lbimages-{Guid.NewGuid():N}");

    private string Touch(params string[] parts)
    {
        var path = Path.Combine(new[] { _root }.Concat(parts).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[] { 1 });
        return path;
    }

    [Fact]
    public void FindsAGamesPicturesByTitleInTheFolderLaunchBoxUses()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Data"));
        // The Arcade control panels were moved to another folder in LaunchBox's settings.
        File.WriteAllText(Path.Combine(_root, "Data", "Platforms.xml"), """
            <?xml version="1.0" standalone="yes"?>
            <LaunchBox>
              <Platform><Name>Arcade</Name></Platform>
              <PlatformFolder>
                <MediaType>Arcade - Control Panel</MediaType>
                <FolderPath>Images\Arcade\CP</FolderPath>
                <Platform>Arcade</Platform>
              </PlatformFolder>
            </LaunchBox>
            """);
        var second = Touch("Images", "Arcade", "CP", "Street Fighter II_ The World Warrior-02.jpg");
        var first = Touch("Images", "Arcade", "CP", "Street Fighter II_ The World Warrior-01.png");
        var japan = Touch("Images", "Arcade", "CP", "Japan", "Street Fighter II_ The World Warrior-01.png");
        Touch("Images", "Arcade", "CP", "Street Fighter II_ The World Warrior-01.txt"); // not a picture
        var info = Touch("Images", "Arcade", "Arcade - Controls Information",
            "Ms. Pac-Man.3f2504e0-4f89-11d3-9a0c-0305e82c3301-01.png"); // default folder; a GUID in the name

        var images = new LaunchBoxImages(_root);
        Assert.Equal(new[] { first, second, japan },
            images.Find("Arcade", "Street Fighter II: The World Warrior", LaunchBoxImages.ControlPanel));
        // Platform names compare without case (Windows folders do).
        Assert.Equal(info, Assert.Single(images.Find("ARCADE", "Ms. Pac-Man", LaunchBoxImages.ControlsInformation)), ignoreCase: true);
        Assert.Empty(images.Find("Arcade", "Street Fighter II", LaunchBoxImages.ControlPanel)); // not a prefix match
        Assert.Empty(images.Find("Sega Genesis", "Ms. Pac-Man", LaunchBoxImages.ControlPanel));  // no folder

        // Pictures added later are found after Refresh.
        var added = Touch("Images", "Arcade", "CP", "Galaga-01.png");
        Assert.Empty(images.Find("Arcade", "Galaga", LaunchBoxImages.ControlPanel));
        images.Refresh();
        Assert.Equal(new[] { added }, images.Find("Arcade", "Galaga", LaunchBoxImages.ControlPanel));
    }

    [Fact]
    public void WithoutPlatformsXmlTheDefaultFoldersAreUsed()
    {
        var file = Touch("Images", "Arcade", "Arcade - Control Panel", "1942-01.png");
        Assert.Equal(new[] { file }, new LaunchBoxImages(_root).Find("Arcade", "1942", LaunchBoxImages.ControlPanel));
        Assert.Equal("Street Fighter II_ The World Warrior", LaunchBoxImages.StripSuffix("Street Fighter II_ The World Warrior-01"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }
}
