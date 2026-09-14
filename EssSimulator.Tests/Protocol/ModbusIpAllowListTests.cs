using System.Net;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Tests;

public class ModbusIpAllowListTests
{
    private static ModbusIpAllowList MustCreate(bool allowLoopback, params string[] addresses)
    {
        Assert.True(ModbusIpAllowList.TryCreate(allowLoopback, addresses, out var list, out var errors),
            string.Join("; ", errors));
        return list;
    }

    [Fact]
    public void EmptyAddresses_AllowsAnyIp()
    {
        var list = MustCreate(false);
        Assert.True(list.IsUnrestricted);
        Assert.True(list.IsAllowed(IPAddress.Parse("10.1.2.3")));
        Assert.True(list.IsAllowed(IPAddress.Parse("8.8.8.8")));
        Assert.True(list.IsAllowed(IPAddress.Loopback));
    }

    [Fact]
    public void ExactIpv4_AllowsOnlyThatAddress()
    {
        var list = MustCreate(false, "10.0.0.5");
        Assert.True(list.IsAllowed(IPAddress.Parse("10.0.0.5")));
        Assert.False(list.IsAllowed(IPAddress.Parse("10.0.0.6")));
        Assert.False(list.IsAllowed(IPAddress.Loopback));
    }

    [Fact]
    public void Cidr_MatchesSubnet()
    {
        var list = MustCreate(false, "192.168.10.0/24");
        Assert.True(list.IsAllowed(IPAddress.Parse("192.168.10.1")));
        Assert.True(list.IsAllowed(IPAddress.Parse("192.168.10.254")));
        Assert.False(list.IsAllowed(IPAddress.Parse("192.168.11.1")));
        Assert.False(list.IsAllowed(IPAddress.Parse("10.0.0.1")));
    }

    [Fact]
    public void Cidr_NonCanonicalNetworkAddress_StillMatches()
    {
        var list = MustCreate(false, "192.168.10.50/24");
        Assert.True(list.IsAllowed(IPAddress.Parse("192.168.10.1")));
        Assert.False(list.IsAllowed(IPAddress.Parse("192.168.11.1")));
    }

    [Fact]
    public void MappedIpv6_MatchesIpv4Rule()
    {
        var list = MustCreate(false, "10.0.0.5");
        var mapped = IPAddress.Parse("::ffff:10.0.0.5");
        Assert.True(list.IsAllowed(mapped));
        Assert.False(list.IsAllowed(IPAddress.Parse("::ffff:10.0.0.6")));
    }

    [Fact]
    public void AllowLoopback_True_AllowsLocalhostWhenListNonEmpty()
    {
        var list = MustCreate(true, "10.0.0.5");
        Assert.True(list.IsAllowed(IPAddress.Loopback));
        Assert.True(list.IsAllowed(IPAddress.IPv6Loopback));
        Assert.False(list.IsAllowed(IPAddress.Parse("10.0.0.6")));
    }

    [Fact]
    public void AllowLoopback_False_RejectsLocalhostUnlessListed()
    {
        var denied = MustCreate(false, "10.0.0.5");
        Assert.False(denied.IsAllowed(IPAddress.Loopback));

        var listed = MustCreate(false, "127.0.0.1");
        Assert.True(listed.IsAllowed(IPAddress.Loopback));
    }

    [Fact]
    public void TryCreate_RejectsInvalidEntries()
    {
        Assert.False(ModbusIpAllowList.TryCreate(true, new[] { "not-an-ip" }, out _, out var errors));
        Assert.Contains(errors, e => e.Contains("not-an-ip"));

        Assert.False(ModbusIpAllowList.TryCreate(true, new[] { "10.0.0.1/99" }, out _, out var cidrErrors));
        Assert.Contains(cidrErrors, e => e.Contains("前缀"));
    }

    [Fact]
    public void TryCreate_DedupesAndSplitsPastedText()
    {
        Assert.True(ModbusIpAllowList.TryCreate(true, new[] { "10.0.0.1, 10.0.0.1", "10.0.0.2" }, out var list, out _));
        Assert.Equal(new[] { "10.0.0.1", "10.0.0.2" }, list.Addresses);
    }

    [Fact]
    public void SamePolicy_EmptyListsAreEqualRegardlessOfLoopback()
    {
        var a = MustCreate(true);
        var b = MustCreate(false);
        Assert.True(a.SamePolicy(b));
    }

    [Fact]
    public void LoadFromPath_MissingFile_IsUnrestricted()
    {
        var list = ModbusIpAllowList.LoadFromPath(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"), out var error);
        Assert.Null(error);
        Assert.True(list.IsUnrestricted);
    }

    [Fact]
    public void SaveAndLoad_RoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "modbus-allowlist-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var original = MustCreate(false, "10.1.2.3", "192.168.0.0/16");
            ModbusIpAllowList.SaveToPath(path, original);
            var loaded = ModbusIpAllowList.LoadFromPath(path, out var error);
            Assert.Null(error);
            Assert.True(original.SamePolicy(loaded));
            Assert.False(loaded.IsAllowed(IPAddress.Loopback));
            Assert.True(loaded.IsAllowed(IPAddress.Parse("10.1.2.3")));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
