using System;
using LinuxHub.Features.UpdateCheck.Services;
using Xunit;

namespace LinuxHub.Tests.Features.UpdateCheck
{
    public class ReleaseVersionParserTests
    {
        [Theory]
        [InlineData("v1.2.4", 1, 2, 4)]
        [InlineData("v0.0.1", 0, 0, 1)]
        [InlineData("v10.20.30", 10, 20, 30)]
        [InlineData(" v1.2.4 ", 1, 2, 4)]
        public void TryParseTag_ReadsTheThreeNumbersAfterThePrefix(
            string tag, int major, int minor, int build)
        {
            bool parsed = ReleaseVersionParser.TryParseTag(tag, out Version version);

            Assert.True(parsed);
            Assert.Equal(new Version(major, minor, build), version);
        }

        [Theory]
        [InlineData("1.2.4")]      // sem o prefixo "v"
        [InlineData("v1.2")]       // componentes de menos
        [InlineData("v1.2.3.4")]   // componentes de mais
        [InlineData("vabc")]
        [InlineData("v1.x.4")]
        [InlineData("v1.-2.3")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void TryParseTag_RejectsMalformedTagInsteadOfGuessing(string? tag)
        {
            bool parsed = ReleaseVersionParser.TryParseTag(tag, out _);

            Assert.False(parsed);
        }

        [Fact]
        public void IsOutdated_TrueWhenRunningIsBehind()
        {
            Assert.True(ReleaseVersionParser.IsOutdated(new Version(1, 2, 4), new Version(1, 3, 0)));
        }

        [Fact]
        public void IsOutdated_FalseWhenRunningMatchesLatest()
        {
            Assert.False(ReleaseVersionParser.IsOutdated(new Version(1, 2, 4), new Version(1, 2, 4)));
        }

        [Fact]
        public void IsOutdated_FalseWhenRunningIsAhead()
        {
            Assert.False(ReleaseVersionParser.IsOutdated(new Version(1, 3, 0), new Version(1, 2, 4)));
        }

        /// <summary>
        /// Comparar tags como texto colocaria "1.10.0" antes de "1.9.0" na ordem alfabética —
        /// o usuário nunca seria avisado de uma release que cruzasse a dezena.
        /// </summary>
        [Fact]
        public void IsOutdated_ComparesNumericallyNotAlphabetically()
        {
            Assert.True(ReleaseVersionParser.IsOutdated(new Version(1, 9, 0), new Version(1, 10, 0)));
        }

        /// <summary>
        /// O caso que a normalização existe para proteger: o assembly de &lt;Version&gt;1.2.4&lt;/Version&gt;
        /// vira Version(1,2,4,0), e a tag "v1.2.4" vira Version(1,2,4) com Revision = -1. Comparados
        /// crus, Version(1,2,4) &lt; Version(1,2,4,0) — a mesma versão pareceria diferente. Ver
        /// decisão 3 do design.md.
        /// </summary>
        [Fact]
        public void IsOutdated_TreatsFourComponentAssemblyVersionAsEqualToThreeComponentTag()
        {
            var assemblyVersion = new Version(1, 2, 4, 0);
            Assert.True(ReleaseVersionParser.TryParseTag("v1.2.4", out Version tagVersion));

            // A premissa que torna este teste necessário: cruas, elas não são iguais.
            Assert.True(tagVersion < assemblyVersion);

            Assert.False(ReleaseVersionParser.IsOutdated(assemblyVersion, tagVersion));
            Assert.Equal(
                ReleaseVersionParser.Normalize(assemblyVersion),
                ReleaseVersionParser.Normalize(tagVersion));
        }

        [Fact]
        public void IsOutdated_StillDetectsUpdateWithFourComponentAssemblyVersion()
        {
            Assert.True(ReleaseVersionParser.IsOutdated(new Version(1, 2, 4, 0), new Version(1, 2, 5)));
        }

        [Fact]
        public void Normalize_DropsRevisionAndFillsMissingBuild()
        {
            Assert.Equal(new Version(1, 2, 4), ReleaseVersionParser.Normalize(new Version(1, 2, 4, 7)));
            Assert.Equal(new Version(1, 2, 0), ReleaseVersionParser.Normalize(new Version(1, 2)));
        }
    }
}
