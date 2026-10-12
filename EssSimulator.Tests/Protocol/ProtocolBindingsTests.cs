using EssSimulator.Protocol.Iec61850;

namespace EssSimulator.Tests.Protocol;

public class ProtocolBindingsTests
{
    [Fact]
    public void Default_SimEmu_IsBothProtocols()
    {
        var bindings = new ProtocolBindings();
        var protocols = bindings.ProtocolsFor("simEmu1");
        Assert.Contains(Iec61850Protocols.Modbus, protocols);
        Assert.Contains(Iec61850Protocols.Iec61850, protocols);
        Assert.True(bindings.Allows("simEmu1", Iec61850Protocols.Iec61850));
        Assert.False(bindings.Allows("simBms1", Iec61850Protocols.Iec61850));
    }

    [Fact]
    public void DefaultPort_StepsFromBase()
    {
        Assert.Equal(8102, ProtocolBindings.DefaultIec61850Port(1, 8102, 1));
        Assert.Equal(8104, ProtocolBindings.DefaultIec61850Port(3, 8102, 1));
    }

    [Fact]
    public void IedName_MatchesSimIndex()
    {
        Assert.Equal("TRNA_PCS01", Iec61850Mapping.IedNameFor("simEmu1"));
        Assert.Equal("TRNA_PCS12", Iec61850Mapping.IedNameFor("simEmu12"));
    }
}
