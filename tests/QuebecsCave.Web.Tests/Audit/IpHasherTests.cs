using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using QuebecsCave.Web.Audit;

namespace QuebecsCave.Web.Tests.Audit;

[TestClass]
public sealed class IpHasherTests
{
    [TestMethod]
    public void Hash_NullIp_ReturnsEmptyHash()
    {
        var expected = SHA256.HashData(Array.Empty<byte>());
        IpHasher.Hash(null).Should().Equal(expected);
    }

    [TestMethod]
    public void Hash_SameIp_IsDeterministic()
    {
        var a = IpHasher.Hash(IPAddress.Parse("203.0.113.5"));
        var b = IpHasher.Hash(IPAddress.Parse("203.0.113.5"));
        a.Should().Equal(b);
    }

    [TestMethod]
    public void Hash_DifferentIps_ProduceDifferentHashes()
    {
        var a = IpHasher.Hash(IPAddress.Parse("203.0.113.5"));
        var b = IpHasher.Hash(IPAddress.Parse("198.51.100.7"));
        a.Should().NotEqual(b);
    }

    [TestMethod]
    public void Hash_IsSha256OfStringRepresentation()
    {
        var ip = IPAddress.Parse("203.0.113.5");
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(ip.ToString()));
        IpHasher.Hash(ip).Should().Equal(expected);
    }

    [TestMethod]
    public void Hash_OutputIs32Bytes()
    {
        IpHasher.Hash(IPAddress.Loopback).Length.Should().Be(32);
    }

    [TestMethod]
    public void HashFromHeader_NullOrEmpty_ReturnsNull()
    {
        IpHasher.HashFromHeader(null).Should().BeNull();
        IpHasher.HashFromHeader("").Should().BeNull();
    }

    [TestMethod]
    public void HashFromHeader_ValidHex_DecodesToBytes()
    {
        var bytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var hex = Convert.ToHexString(bytes);
        IpHasher.HashFromHeader(hex).Should().Equal(bytes);
    }

    [TestMethod]
    public void HashFromHeader_InvalidHex_ReturnsNull()
    {
        IpHasher.HashFromHeader("not-hex!").Should().BeNull();
        IpHasher.HashFromHeader("ABC").Should().BeNull(); // odd length
    }
}
