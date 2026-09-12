using netDxf.Entities;
using netDxf.Header;
using Xunit;

namespace netDxf.Tests;

public sealed class VersionRoundTripTests
{
    public static TheoryData<DxfVersion, bool> SupportedFormats
    {
        get
        {
            var data = new TheoryData<DxfVersion, bool>();
            foreach (var version in new[]
            {
                DxfVersion.AutoCad2000, DxfVersion.AutoCad2004,
                DxfVersion.AutoCad2007, DxfVersion.AutoCad2010,
                DxfVersion.AutoCad2013, DxfVersion.AutoCad2018
            })
            {
                data.Add(version, false);
                data.Add(version, true);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(SupportedFormats))]
    public void GeometryRoundTripsThroughEveryAdvertisedFormat(DxfVersion version, bool binary)
    {
        var document = new DxfDocument(version);
        var line = new Line(new Vector3(-1.25, 2.5, 3.75), new Vector3(10, -20, 30));
        var circle = new Circle(new Vector3(7, 8, 9), 2.5);
        document.Entities.Add(line);
        document.Entities.Add(circle);

        using var stream = new MemoryStream();
        Assert.True(document.Save(stream, binary));
        Assert.True(stream.CanRead);
        Assert.True(stream.Length > 0);
        stream.Position = 0;
        Assert.Equal(version, DxfDocument.CheckDxfFileVersion(stream, out var detectedBinary));
        Assert.Equal(binary, detectedBinary);

        stream.Position = 0;
        var loaded = DxfDocument.Load(stream);
        Assert.NotNull(loaded);
        Assert.Equal(version, loaded.DrawingVariables.AcadVer);
        var loadedLine = Assert.Single(loaded.Entities.Lines);
        var loadedCircle = Assert.Single(loaded.Entities.Circles);
        Assert.Equal(line.StartPoint, loadedLine.StartPoint);
        Assert.Equal(line.EndPoint, loadedLine.EndPoint);
        Assert.Equal(circle.Center, loadedCircle.Center);
        Assert.Equal(circle.Radius, loadedCircle.Radius);
        Assert.NotNull(loaded.GetObjectByHandle(loadedLine.Handle));
    }
}
