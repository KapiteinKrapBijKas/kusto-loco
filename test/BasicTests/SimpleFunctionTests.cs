using FluentAssertions;

namespace BasicTests;

[TestClass]
public class SimpleFunctionTests : TestMethods
{
    [TestMethod]
    public async Task TrimStart()
    {
        var query = @"print trim_start(@'a+','aaainnerbbba')";
        var result = await LastLineOfResult(query);
        result.Should().Be("innerbbba");
    }

    [TestMethod]
    public async Task TrimEnd()
    {
        var query = @"print trim_end(@'b+','baaainnerbbb')";
        var result = await LastLineOfResult(query);
        result.Should().Be("baaainner");
    }

    [TestMethod]
    public async Task Trim()
    {
        var query = @"print trim(@'a+','aaaainneraaaa')";
        var result = await LastLineOfResult(query);
        result.Should().Be("inner");
    }

    [TestMethod]
    public async Task Case()
    {
        var query = @" 
datatable(Size:int) [7] 
| extend S= case(Size <= 3, 'Small',                        
                 Size <= 10, 'Medium', 
                             'Large')";
        var result = await LastLineOfResult(query);
        result.Should().Contain("Medium");
    }

    [TestMethod]
    public async Task CaseDefault()
    {
        var query = @" 
datatable(Size:int) [50] 
|extend S= case(Size <= 3, 'Small',                        
              Size <= 10, 'Medium', 
                          'Large')";
        var result = await LastLineOfResult(query);
        result.Should().Contain("Large");
    }

    [TestMethod]
    public async Task GeoDistance2PointsScalar()
    {
        var query = @" print distance_in_meters = geo_distance_2points(-122.407628, 47.578557, -118.275287, 34.019056)";
        var result = await LastLineOfResult(query);
        result.Should().Contain("15467");
    }

    [TestMethod]
    public async Task GeoPointToGeoHashScalar()
    {
        var query = @"print geohash = geo_point_to_geohash(139.806115, 35.554128, 12)";
        var result = await LastLineOfResult(query);
        result.Should().Contain("xn76m27ty9g4");
    }

    [TestMethod]
    public async Task GeoPointToGeoHashScalarWithDefault()
    {
        var query = @"print geohash = geo_point_to_geohash(139.806115, 35.554128)";
        var result = await LastLineOfResult(query);
        result.Should().Be("xn76m");
    }


    [TestMethod]
    public async Task GeoHashToCentralPointScalar()
    {
        var query = @"print point = geo_geohash_to_central_point('sunny')
                      | extend coordinates = point.coordinates
                      | project longitude = coordinates[0]";
        var result = await LastLineOfResult(query);
        result.Should().Contain("42.4731445");
    }


    [TestMethod]
    public async Task SplitScalar()
    {
        var query = @"print c=split('this.is.a.string.and.I.need.the.last.part', '.')[-1]";
        var result = await LastLineOfResult(query);
        result.Should().Be("part");
    }
}