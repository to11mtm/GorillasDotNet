using Gorillas.Core;
using Gorillas.Core.Model;
using Gorillas.Core.Simulation;

namespace Gorillas.Core.Tests;

public class SkylineGeneratorTests
{
    [Fact]
    public void SameSeedAndRoundProduceIdenticalSkyline()
    {
        var a = SkylineGenerator.Generate(GameSettings.Default, 12345, 1);
        var b = SkylineGenerator.Generate(GameSettings.Default, 12345, 1);

        Assert.Equal(a.Buildings, b.Buildings);
    }

    [Fact]
    public void DifferentRoundsProduceDifferentSkylines()
    {
        var a = SkylineGenerator.Generate(GameSettings.Default, 12345, 1);
        var b = SkylineGenerator.Generate(GameSettings.Default, 12345, 2);

        Assert.NotEqual(a.Buildings, b.Buildings);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(9876543210UL)]
    [InlineData(ulong.MaxValue)]
    public void BuildingsTileTheFullWidthWithoutGaps(ulong seed)
    {
        var settings = GameSettings.Default;
        var skyline = SkylineGenerator.Generate(settings, seed, 1);

        Assert.Equal(0, skyline.Buildings[0].Left, 6);
        Assert.Equal(settings.Width, skyline.Buildings[^1].Right, 6);

        for (var i = 1; i < skyline.Buildings.Count; i++)
        {
            Assert.Equal(skyline.Buildings[i - 1].Right, skyline.Buildings[i].Left, 6);
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(42UL)]
    [InlineData(777777UL)]
    public void BuildingHeightsStayWithinConfiguredBounds(ulong seed)
    {
        var settings = GameSettings.Default;
        var skyline = SkylineGenerator.Generate(settings, seed, 3);

        Assert.All(skyline.Buildings, b =>
        {
            Assert.InRange(b.Height, settings.MinBuildingHeight, settings.MaxBuildingHeight);
            Assert.True(b.Width > 0);
        });
    }

    [Fact]
    public void GorillasStandOnTheSecondAndSecondToLastRooftops()
    {
        var skyline = SkylineGenerator.Generate(GameSettings.Default, 5150, 1);
        var gorillas = SkylineGenerator.PlaceGorillas(skyline);

        Assert.Equal(2, gorillas.Count);
        Assert.Equal(1, gorillas[0].BuildingIndex);
        Assert.Equal(skyline.Buildings.Count - 2, gorillas[1].BuildingIndex);
        Assert.Equal(skyline.Buildings[1].CenterX, gorillas[0].Feet.X, 6);
        Assert.Equal(skyline.Buildings[1].Height, gorillas[0].Feet.Y, 6);
        Assert.True(gorillas[0].Feet.X < gorillas[1].Feet.X);
    }
}
