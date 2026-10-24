namespace Stellaegis.Primitives.Tests;

public class PreferenceTests
{
    [Fact]
    public void A_choice_accepts_its_declared_values_only()
    {
        var theme = Preference.Choice("theme", ["light", "dark", "system"], "system");

        Assert.Equal(PreferenceKind.Choice, theme.Kind);
        Assert.Equal(["light", "dark", "system"], theme.Choices);
        Assert.Equal("system", theme.Default);
        Assert.True(theme.Validate("dark").IsSuccess);
        Assert.True(theme.Validate("Dark").IsFailure);
        Assert.True(theme.Validate("sepia").IsFailure);
    }

    [Fact]
    public void A_color_is_accepted_in_lowercase_hexadecimal_only()
    {
        var accent = Preference.Color("accent");

        Assert.Null(accent.Default);
        Assert.True(accent.Validate("#3b82f6").IsSuccess);
        Assert.True(accent.Validate("#3B82F6").IsFailure);
        Assert.True(accent.Validate("3b82f6").IsFailure);
        Assert.True(accent.Validate("#3b8").IsFailure);
        Assert.True(accent.Validate("#3b82f6\n").IsFailure);
    }

    [Fact]
    public void A_number_is_accepted_in_range_and_in_its_canonical_form()
    {
        var zoom = Preference.Number("zoom", 50, 200, 100);

        Assert.Equal("100", zoom.Default);
        Assert.Equal(50, zoom.Minimum);
        Assert.Equal(200, zoom.Maximum);
        Assert.True(zoom.Validate("50").IsSuccess);
        Assert.True(zoom.Validate("200").IsSuccess);
        Assert.True(zoom.Validate("49").IsFailure);
        Assert.True(zoom.Validate("201").IsFailure);
        Assert.True(zoom.Validate("0120").IsFailure);
        Assert.True(zoom.Validate("+120").IsFailure);
        Assert.True(zoom.Validate("120.0").IsFailure);
        Assert.True(Preference.Number("offset", -10, 10).Validate("-5").IsSuccess);
        Assert.True(Preference.Number("offset", -10, 10).Validate("-0").IsFailure);
    }

    [Fact]
    public void A_boolean_is_true_or_false()
    {
        var compact = Preference.Boolean("compact", false);

        Assert.Equal("false", compact.Default);
        Assert.True(compact.Validate("true").IsSuccess);
        Assert.True(compact.Validate("True").IsFailure);
        Assert.True(compact.Validate("1").IsFailure);
        Assert.Null(Preference.Boolean("compact").Default);
    }

    [Fact]
    public void A_refused_value_names_the_form_expected()
    {
        var error = Preference.Color("accent").Validate("#3B82F6").Error!;

        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(Preference.InvalidValueCode, error.Code);
        Assert.Contains("'#3B82F6' is not a valid value for accent: a color, #rrggbb in lowercase", error.Message);
        Assert.True(Preference.Color("accent").Validate(null).IsFailure);
    }

    [Fact]
    public void A_declaration_that_does_not_hold_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Preference.Color("Accent"));
        Assert.Throws<ArgumentException>(() => Preference.Color("accent-color"));
        Assert.Throws<ArgumentException>(() => Preference.Color("accent", "#3B82F6"));
        Assert.Throws<ArgumentException>(() => Preference.Choice("theme", []));
        Assert.Throws<ArgumentException>(() => Preference.Choice("theme", ["light", "light"]));
        Assert.Throws<ArgumentException>(() => Preference.Choice("theme", ["light", "Dark"]));
        Assert.Throws<ArgumentException>(() => Preference.Choice("theme", ["light", "dark"], "system"));
        Assert.Throws<ArgumentNullException>(() => Preference.Choice("theme", null!));
        Assert.Throws<ArgumentNullException>(() => Preference.Choice("theme", ["light", null!]));
        Assert.Throws<ArgumentException>(() => Preference.Number("zoom", 200, 50));
        Assert.Throws<ArgumentException>(() => Preference.Number("zoom", 50, 200, 300));
    }

    [Fact]
    public void Two_preferences_are_equal_when_their_definitions_are()
    {
        Assert.Equal(Preference.Choice("theme", ["light", "dark"], "light"), Preference.Choice("theme", ["light", "dark"], "light"));
        Assert.NotEqual(Preference.Choice("theme", ["light", "dark"], "light"), Preference.Choice("theme", ["dark", "light"], "light"));
        Assert.NotEqual(Preference.Choice("theme", ["light", "dark"], "light"), Preference.Choice("theme", ["light", "dark"]));
        Assert.NotEqual(Preference.Number("zoom", 50, 200), Preference.Number("zoom", 50, 150));
        Assert.NotEqual(Preference.Color("accent"), Preference.Boolean("accent"));
        Assert.Equal("theme", Preference.Choice("theme", ["light"]).ToString());
    }
}
