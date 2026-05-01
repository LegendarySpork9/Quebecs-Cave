using FluentAssertions;
using QuebecsCave.Core.Util;

namespace QuebecsCave.Core.Tests.Util;

[TestClass]
public sealed class SlugGeneratorTests
{
    [TestMethod]
    public void Slugify_LowercasesInput()
    {
        SlugGenerator.Slugify("STARDEW").Should().Be("stardew");
    }

    [TestMethod]
    public void Slugify_ReplacesSpacesWithHyphens()
    {
        SlugGenerator.Slugify("Stardew Valley").Should().Be("stardew-valley");
    }

    [TestMethod]
    public void Slugify_CollapsesRunsOfSeparators()
    {
        SlugGenerator.Slugify("Stardew Valley: Year One!!").Should().Be("stardew-valley-year-one");
    }

    [TestMethod]
    public void Slugify_StripsDiacritics()
    {
        SlugGenerator.Slugify("Pokémon Café").Should().Be("pokemon-cafe");
    }

    [TestMethod]
    public void Slugify_TrimsLeadingAndTrailingSeparators()
    {
        SlugGenerator.Slugify("---Hello World---").Should().Be("hello-world");
        SlugGenerator.Slugify("!!Quebec's Cave!!").Should().Be("quebec-s-cave");
    }

    [TestMethod]
    public void Slugify_KeepsAlphanumeric()
    {
        SlugGenerator.Slugify("Half-Life 2").Should().Be("half-life-2");
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("\t\n")]
    public void Slugify_EmptyOrWhitespace_ReturnsEmpty(string? input)
    {
        SlugGenerator.Slugify(input!).Should().Be("");
    }

    [TestMethod]
    public void Slugify_AllSymbols_ReturnsEmpty()
    {
        SlugGenerator.Slugify("!!!@@@###").Should().Be("");
    }

    [TestMethod]
    public void Slugify_UnicodeOutsideLatinIsStripped()
    {
        SlugGenerator.Slugify("ハロー World").Should().Be("world");
    }
}
