using Clay.Engine;

namespace Clay.Tests;

public class CompoundUnitTests
{
    private static LineResult Eval(string document, bool physics = false) =>
        new Calculator { PhysicsUnits = physics }.Evaluate(document)[^1];

    [Theory]
    [InlineData("5 m/s * 4 s", "20 m")]
    [InlineData("5 m/s × 4 s", "20 m")]
    [InlineData("4 s * 5 m/s", "20 m")]
    [InlineData("100 km / 2 h", "50 km/h")]
    [InlineData("60 mph * 30 min", "30 mi")]
    [InlineData("5 km/h * 30 min", "2.5 km")]
    [InlineData("2 kW * 3 h", "6 kWh")]
    [InlineData("6 kWh / 3 h", "2 kW")]
    [InlineData("100 J / 2 s", "50 W")]
    [InlineData("2 W * 3 s", "6 J")]
    [InlineData("10 km / 5 km", "2")]
    [InlineData("20 m/s * 3", "60 m/s")]
    [InlineData("product(2 m, 3 m)", "6 m²")]
    [InlineData("2 kg * 3 m/s^2", "6 kg·m/s²")]
    [InlineData("60 / 2 min", "30 min⁻¹")]
    public void MultipliesAndDividesUnits(string expression, string expected) =>
        AssertResult(Eval(expression), expected);

    [Theory]
    [InlineData("3 m * 4 m", "12 m²")]
    [InlineData("3 ft * 4 ft", "12 ft²")]
    [InlineData("2 m * 3 ft", "1.8288 m²")]
    [InlineData("3 ft * 2 m", "19.685039 ft²")]
    [InlineData("2 m * 3 m * 4 m", "24 m³")]
    [InlineData("1 acre / 100 ft in ft", "435.6 ft")]
    [InlineData("10 m * 1 ha", "100,000 m³")]
    public void MixedUnitsUseTheLeftUnit(string expression, string expected) =>
        AssertResult(Eval(expression), expected);

    [Theory]
    [InlineData("5 l / 100 km", "0.05 l/km")]
    [InlineData("400 km / 40 l", "10 km/l")]
    [InlineData("1 km per l", "1 km/l")]
    [InlineData("30 mi / 1 gal in km/l", "12.754311 km/l")]
    [InlineData("1 m² * 20 mm", "0.02 m³")]
    [InlineData("1000 kg/m^3 * 2 l", "2 kg")]
    public void KeepsRatesBetweenVolumeAndLength(string expression, string expected) =>
        AssertResult(Eval(expression), expected);

    [Theory]
    [InlineData("$0.1234 per kWh", "$0.1234/kWh")]
    [InlineData("rate = $0.12/kWh\nrate", "$0.12/kWh")]
    [InlineData("$0.12/kWh * 500 kWh", "$60.00")]
    [InlineData("1 / 2 kWh", "0.5 kWh⁻¹")]
    public void NamesCompoundDenominators(string expression, string expected) =>
        AssertResult(Eval(expression), expected);

    [Theory]
    [InlineData("5 m / s", "5 m/s")]
    [InlineData("60 miles per hour", "60 mph")]
    [InlineData("$15 per hour", "$15/h")]
    [InlineData("$15 per hour * 40 h", "$600.00")]
    [InlineData("8 h * $15/h", "$120.00")]
    [InlineData("9.8 m/s^2 * 2 s", "19.6 m/s")]
    [InlineData("9.8 m/s² * 2 s", "19.6 m/s")]
    [InlineData("2 m^2/s * 3 s", "6 m²")]
    [InlineData("1 kg·m/s² * 1 s", "1 kg·m/s")]
    [InlineData("5 m^2", "5 m²")]
    [InlineData("(3 m)^2", "9 m²")]
    [InlineData("sqrt(9 m²)", "3 m")]
    [InlineData("sqrt(16 m^2)", "4 m")]
    [InlineData("cbrt(27 m³)", "3 m")]
    public void ParsesUnitExpressions(string expression, string expected) =>
        AssertResult(Eval(expression), expected);

    [Theory]
    [InlineData("10 m/s in km/h", "36 km/h")]
    [InlineData("1 m/s^2 in ft/s^2", "3.28084 ft/s²")]
    [InlineData("100 km/h in miles per hour", "62.137119 mph")]
    [InlineData("1 kWh in J", "3,600,000 J")]
    [InlineData("1 m³ in l", "1,000 l")]
    [InlineData("1 gal in l", "3.785412 l")]
    [InlineData("1 lb in g", "453.59237 g")]
    public void ConvertsCompoundUnits(string expression, string expected) =>
        AssertResult(Eval(expression), expected);

    [Theory]
    [InlineData("v = 3\n2 V", "2 V")]
    [InlineData("a = 3\n2 A * 3 V", "6 W")]
    [InlineData("n = 4\n5 N * 2 m", "10 J")]
    public void PhysicsSymbolsBeatCaseInsensitiveVariables(string document, string expected) =>
        AssertResult(Eval(document, physics: true), expected);

    [Theory]
    [InlineData("5 m^0", "5")]
    [InlineData("(5 m)^0", "1")]
    [InlineData("5 in^2", "5 in²")]
    [InlineData("5 inch^2", "5 in²")]
    [InlineData("(2 inch)^2", "4 in²")]
    [InlineData("4 in²", "4 in²")]
    [InlineData("4 in² * 2 in", "8 in³")]
    [InlineData("sqrt(1 ha)", "100 m")]
    [InlineData("cbrt(8 l)", "0.2 m")]
    [InlineData("sqrt(4 acre)", "127.229814 m")]
    public void HandlesZeroPowersInchesAndLeafRoots(string expression, string expected) =>
        AssertResult(Eval(expression), expected);

    [Theory]
    [InlineData("20 °C * 2 m")]
    [InlineData("2 m / 20 °C")]
    [InlineData("(2 °C)^2")]
    [InlineData("sqrt(2 m)")]
    [InlineData("sqrt(2 ha * 1 m)")]
    [InlineData("2 m + 3 s")]
    [InlineData("5 m/s in km")]
    [InlineData("1 K/s in °C per s")]
    [InlineData("1 m/s in m per °C")]
    public void RejectsInvalidUnitArithmetic(string expression) =>
        Assert.Equal(LineKind.Error, Eval(expression).Kind);

    [Theory]
    [InlineData("5 N * 2 m", "10 J")]
    [InlineData("2 kg * 3 m/s^2", "6 N")]
    [InlineData("12 V / 2 Ω", "6 A")]
    [InlineData("2 A * 3 V", "6 W")]
    [InlineData("60 Hz * 2 s", "120")]
    [InlineData("10 J / 2 N", "5 m")]
    [InlineData("10 N / 2 m²", "5 Pa")]
    [InlineData("1 kN in N", "1,000 N")]
    [InlineData("1 kg·m/s² in N", "1 N")]
    [InlineData("100 W / 2 V", "50 A")]
    [InlineData("5 kN", "5 kN")]
    [InlineData("0.5 as a %", "50%")]
    public void PhysicsUnitsWhenEnabled(string expression, string expected) =>
        AssertResult(Eval(expression, physics: true), expected);

    [Fact]
    public void PhysicsSymbolsAreVariablesWhenDisabled()
    {
        AssertResult(Eval("N = 5\nN * 2"), "10");
        AssertResult(Eval("V = 3\n2 V"), "6");
    }

    [Fact]
    public void PhysicsSymbolsAreReservedWhenEnabled()
    {
        var result = Eval("N = 5", physics: true);
        Assert.Equal(LineKind.Error, result.Kind);
        Assert.Contains("reserved", result.Text);
    }

    [Fact]
    public void PhysicsSettingDoesNotLeakBetweenEvaluations()
    {
        AssertResult(Eval("5 N * 2 m", physics: true), "10 J");
        Assert.False(UnitTable.PhysicsEnabled);
        Assert.Null(UnitTable.Lookup("N"));
    }

    private static void AssertResult(LineResult result, string expected)
    {
        Assert.True(result.Kind == LineKind.Result, $"{result.Kind}: {result.Text}");
        Assert.Equal(expected, result.Text);
    }
}
