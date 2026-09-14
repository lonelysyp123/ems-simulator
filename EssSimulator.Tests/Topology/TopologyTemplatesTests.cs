using EssSimulator.Web.Topology;
using Xunit;

namespace EssSimulator.Tests.Topology;

public class TopologyTemplatesTests
{
    [Fact]
    public void All_includes_pv_unit_named_光伏单元()
    {
        var t = TopologyTemplates.Get("pv_unit");
        Assert.NotNull(t);
        Assert.Equal("光伏单元", t!.Name);
        Assert.Equal("光伏", t.Category);
        Assert.False(t.IsVoltageSource);

        Assert.Equal(3, t.Ports.Count);
        Assert.Collection(t.Ports,
            p => { Assert.Equal("ac_a", p.Id); Assert.Equal("A", p.Phase); Assert.Equal("top", p.Side); },
            p => { Assert.Equal("ac_b", p.Id); Assert.Equal("B", p.Phase); Assert.Equal("top", p.Side); },
            p => { Assert.Equal("ac_c", p.Id); Assert.Equal("C", p.Phase); Assert.Equal("top", p.Side); });

        Assert.Equal(30d, TopologyParamHelper.GetDouble(t.DefaultParameters, "modulesPerString"));
        Assert.Equal(16d, TopologyParamHelper.GetDouble(t.DefaultParameters, "stringCount"));
        Assert.Equal(16d, TopologyParamHelper.GetDouble(t.DefaultParameters, "inverterCount"));
        Assert.Equal(320d, TopologyParamHelper.GetDouble(t.DefaultParameters, "inverterRatedPowerKw"));
        Assert.Equal(690d, TopologyParamHelper.GetDouble(t.DefaultParameters, "inverterAcVoltage"));
        Assert.Equal(690d, TopologyParamHelper.GetDouble(t.DefaultParameters, "unitXfSecondaryV"));
        Assert.Equal(35000d, TopologyParamHelper.GetDouble(t.DefaultParameters, "acVoltage"));
        Assert.Equal(5120d, TopologyParamHelper.GetDouble(t.DefaultParameters, "unitXfRatedKva"));
        Assert.Equal("TSM-NEG21C.20Q", TopologyParamHelper.GetString(t.DefaultParameters, "moduleModel"));
        Assert.Equal(0d, TopologyParamHelper.GetDouble(t.DefaultParameters, "operatingYears"));
        Assert.Equal(0d, TopologyParamHelper.GetDouble(t.DefaultParameters, "groundAlbedo"));
    }

    [Fact]
    public void Split_transformer_is_named_双耳变压器_with_nine_ports()
    {
        var t = TopologyTemplates.Get("split_transformer");
        Assert.NotNull(t);
        Assert.Equal("双耳变压器", t!.Name);
        Assert.Equal("变电", t.Category);
        Assert.False(t.IsVoltageSource);
        Assert.Equal(9, t.Ports.Count);
        Assert.Collection(t.Ports,
            p => { Assert.Equal("pri_a", p.Id); Assert.False(p.IsVoltageSourcePort); Assert.Equal("top", p.Side); },
            p => { Assert.Equal("pri_b", p.Id); Assert.False(p.IsVoltageSourcePort); },
            p => { Assert.Equal("pri_c", p.Id); Assert.False(p.IsVoltageSourcePort); },
            p => { Assert.Equal("ear_l_a", p.Id); Assert.True(p.IsVoltageSourcePort); Assert.Equal("secondaryVoltage", p.VoltageParam); },
            p => { Assert.Equal("ear_l_b", p.Id); Assert.True(p.IsVoltageSourcePort); },
            p => { Assert.Equal("ear_l_c", p.Id); Assert.True(p.IsVoltageSourcePort); },
            p => { Assert.Equal("ear_r_a", p.Id); Assert.True(p.IsVoltageSourcePort); },
            p => { Assert.Equal("ear_r_b", p.Id); Assert.True(p.IsVoltageSourcePort); },
            p => { Assert.Equal("ear_r_c", p.Id); Assert.True(p.IsVoltageSourcePort); });

        Assert.Equal(35000d, TopologyParamHelper.GetDouble(t.DefaultParameters, "primaryVoltage"));
        Assert.Equal(690d, TopologyParamHelper.GetDouble(t.DefaultParameters, "secondaryVoltage"));
        Assert.Equal(6300d, TopologyParamHelper.GetDouble(t.DefaultParameters, "ratedPowerKva"));
        Assert.Equal(0.5d, TopologyParamHelper.GetDouble(t.DefaultParameters, "splitRatio"));
        Assert.True(TopologyTemplates.IsTransformerLike("transformer"));
        Assert.True(TopologyTemplates.IsTransformerLike("split_transformer"));
        Assert.True(TopologyTemplates.IsSplitTransformer("split_transformer"));
        Assert.False(TopologyTemplates.IsTransformerLike("pcs"));
        Assert.True(TopologyTemplates.IsSplitLeftEarPort("ear_l_a"));
        Assert.True(TopologyTemplates.IsSplitRightEarPort("ear_r_c"));
        Assert.False(TopologyTemplates.IsSplitLeftEarPort("pri_a"));
        Assert.Equal("ear_l", TopologyTemplates.AcPhaseBundleKey("ear_l_a"));
        Assert.Equal("ear_r", TopologyTemplates.AcPhaseBundleKey("ear_r_c"));
        Assert.Equal("", TopologyTemplates.AcPhaseBundleKey("pri_a"));
    }

    [Fact]
    public void Pcs_is_named_PCS变流器支路()
    {
        var t = TopologyTemplates.Get("pcs");
        Assert.NotNull(t);
        Assert.Equal("pcs", t!.Id);
        Assert.Equal("PCS 变流器支路", t.Name);
        Assert.Equal("PCS变流器支路", TopologyParamHelper.GetString(t.DefaultParameters, "name"));
        Assert.Contains("支路", t.Description);
    }

    [Fact]
    public void All_lists_split_transformer_in_substation_category()
    {
        Assert.Contains(TopologyTemplates.All, t => t.Id == "split_transformer" && t.Category == "变电");
        Assert.Equal(2, TopologyTemplates.MaxSplitTransformersPerEmu);
        Assert.Equal("split_transformer", TopologyTemplates.SplitTransformerId);
    }

    [Fact]
    public void Original_transformer_ports_and_defaults_unchanged()
    {
        var t = TopologyTemplates.Get("transformer");
        Assert.NotNull(t);
        Assert.Equal("变压器", t!.Name);
        Assert.Equal(6, t.Ports.Count);
        Assert.Equal(220000d, TopologyParamHelper.GetDouble(t.DefaultParameters, "primaryVoltage"));
        Assert.Equal(35000d, TopologyParamHelper.GetDouble(t.DefaultParameters, "secondaryVoltage"));
        Assert.Equal(31500d, TopologyParamHelper.GetDouble(t.DefaultParameters, "ratedPowerKva"));
    }
}
