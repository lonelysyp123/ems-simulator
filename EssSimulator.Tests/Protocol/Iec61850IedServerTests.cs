using EssSimulator.DataExchange.Adapters;
using EssSimulator.Protocol.Iec61850;
using IEC61850.Client;
using IEC61850.Common;

namespace EssSimulator.Tests.Protocol;

public class Iec61850IedServerTests
{
    private static string MappingPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "pointmaps", "models", "emu", "iec61850", "mapping.csv");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("mapping.csv");
    }

    private sealed class FakeStore : IProtocolPointStore
    {
        public readonly Dictionary<string, object> Values = new(StringComparer.OrdinalIgnoreCase);

        public event EventHandler<ProtocolPointChangedEventArgs>? PointsChanged;

        public void WriteDefaults(IReadOnlyDictionary<string, object> defaults) => WritePoints(defaults);

        public void WritePoints(IReadOnlyDictionary<string, object> values, byte? slaveId = null, bool applyScale = true)
        {
            foreach (var pair in values)
                Values[pair.Key] = pair.Value;
            PointsChanged?.Invoke(this, new ProtocolPointChangedEventArgs(values));
        }

        public Dictionary<string, object> ReadAllControlRaw(IReadOnlyList<string> paramNames, byte? slaveId = null)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in paramNames)
            {
                if (Values.TryGetValue(name, out var v))
                    result[name] = v;
            }

            return result;
        }

        public object? ReadParsedPoint(string paramName, byte? slaveId = null) =>
            Values.TryGetValue(paramName, out var v) ? v : null;

        public object? GetCachedValue(string paramName) => ReadParsedPoint(paramName);

        public void RaiseTelemetry(string paramName, object value) =>
            WritePoints(new Dictionary<string, object> { [paramName] = value });
    }

    [NativeLibraryFact]
    public void GetSetOperate_UseParamNameShadow()
    {
        var mapping = Iec61850Mapping.Load(MappingPath());
        var store = new FakeStore();
        store.Values["yc27"] = 120.5;
        store.Values["yk3"] = 0;
        var writes = new List<(string Name, object Value)>();

        using var ied = new Iec61850IedServer("simEmu1", 0, mapping);
        ied.Attach(store, (name, value) =>
        {
            writes.Add((name, value));
            store.Values[name] = value;
        });

        Assert.Contains("PCS", ied.GetServerDirectory());
        Assert.Contains(ied.GetLogicalDeviceDirectory("PCS"), n => n.Contains("MMXU1", StringComparison.OrdinalIgnoreCase));

        Assert.True(ied.TryGetDataValues(new[] { "PCS/MMXU1.TotW.mag.f" }, out var values, out var err), err);
        Assert.Equal(120.5, Convert.ToDouble(values.Values.Single()));

        Assert.True(ied.TryOperate("PCS/GGIO1.SPCSO1", true, out err), err);
        Assert.Equal("yk3", writes.Last().Name);
        Assert.Equal(1, Convert.ToDouble(writes.Last().Value));

        Assert.True(ied.TrySetDataValues(
            new Dictionary<string, object?> { ["PCS/DRCC1.OutWSet.setMag.f"] = 80.0 },
            out err), err);
        Assert.Equal("yt0", writes.Last().Name);
    }

    // 说明：libiec61850 1.6.2 的 .NET 客户端在 MMS GetNameList（GetServerDirectory /
    // GetLogicalNodeDirectory）上拿不到响应——跨进程表现为超时（IED_ERROR_TIMEOUT = 20），
    // 客户端与服务端同进程时直接让测试宿主 SIGSEGV。GetDataValues（读）、控制 Operate、
    // RCB 读写都正常。因此目录类断言改走服务端模型，其余仍走真实 MMS 链路。
    [NativeLibraryFact]
    public void TcpClient_AssociateReadOperateAndUrcb()
    {
        var mapping = Iec61850Mapping.Load(MappingPath());
        var store = new FakeStore();
        store.Values["yc27"] = 10;
        using var ied = new Iec61850IedServer("simEmu1", 0, mapping) { GooseInterfaceId = "none" };
        ied.Attach(store, (name, value) => store.Values[name] = value);
        Assert.True(ied.Start());
        Assert.True(ied.SpcHandlerCount >= 2, $"SPC 控制对象未挂上 ParamName，count={ied.SpcHandlerCount}");
        Assert.Equal(7, ied.GooseEntryCount);

        using var client = new IedConnection();
        client.Connect("127.0.0.1", ied.Port);

        // 目录断言走服务端模型（见方法上方说明）
        Assert.Contains(ied.GetServerDirectory(), n => n.Contains("PCS", StringComparison.OrdinalIgnoreCase));

        string totW = $"{ied.IedName}PCS/MMXU1.TotW.mag.f";
        float power = client.ReadFloatValue(totW, FunctionalConstraint.MX);
        Assert.Equal(10f, power, 1);

        using var ctrl = client.CreateControlObject($"{ied.IedName}PCS/GGIO1.SPCSO1");
        Assert.True(ctrl.Operate(true));
        var operateDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < operateDeadline && !store.Values.ContainsKey("yk3"))
            Thread.Sleep(20);
        Assert.True(store.Values.ContainsKey("yk3"), "Operate 后点影子未收到 yk3");
        Assert.Equal(1, Convert.ToDouble(store.Values["yk3"]));

        var reports = new List<ReasonForInclusion>();
        using var rcb = client.GetReportControlBlock($"{ied.IedName}PCS/LLN0.RP.{Iec61850PcsModel.UrcbName}");
        rcb.GetRCBValues();
        rcb.InstallReportHandler((report, _) =>
        {
            var values = report.GetDataSetValues();
            for (int i = 0; i < values.Size(); i++)
            {
                var reason = report.GetReasonForInclusion(i);
                if (reason != ReasonForInclusion.REASON_NOT_INCLUDED)
                    reports.Add(reason);
            }
        }, null);
        rcb.SetResv(true);
        rcb.SetTrgOps(TriggerOptions.DATA_CHANGED | TriggerOptions.INTEGRITY | TriggerOptions.GI);
        rcb.SetIntgPd(200);
        rcb.SetRptEna(true);
        rcb.SetRCBValues();

        store.RaiseTelemetry("yc27", 99.0);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline && !reports.Contains(ReasonForInclusion.REASON_DATA_CHANGE))
            Thread.Sleep(20);
        Assert.Contains(ReasonForInclusion.REASON_DATA_CHANGE, reports);

        var integrityDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < integrityDeadline && !reports.Contains(ReasonForInclusion.REASON_INTEGRITY))
            Thread.Sleep(20);
        Assert.Contains(ReasonForInclusion.REASON_INTEGRITY, reports);

        client.Abort();
    }

    [NativeLibraryFact]
    public void DeviceHasNoGooseControlBlock()
    {
        var mapping = Iec61850Mapping.Load(MappingPath());
        using var ied = new Iec61850IedServer("simEmu1", 0, mapping) { GooseInterfaceId = "none" };
        ied.Attach(_ => null, (_, _) => { });
        Assert.True(ied.Start());

        // 设备侧模型不含 GOOSE 控制块/数据集（目录断言见上一用例的说明）
        Assert.Null(ied.Model.GetModelNodeByObjectReference(
            $"{Iec61850Mapping.LogicalDeviceName}/LLN0.{Iec61850PcsModel.GoCbName}"));
        Assert.Null(ied.Model.GetModelNodeByObjectReference(
            $"{Iec61850Mapping.LogicalDeviceName}/LLN0.{Iec61850PcsModel.GooseDataSetName}"));

        // 链路上的普通数据访问不受影响
        using var client = new IedConnection();
        client.Connect("127.0.0.1", ied.Port);
        var totW = client.ReadFloatValue($"{ied.IedName}PCS/MMXU1.TotW.mag.f", FunctionalConstraint.MX);
        Assert.Equal(0f, totW, 1);
        client.Abort();
    }

    [NativeLibraryFact]
    public void ApplyGoose_WritesYkAndYtThroughControlCallback()
    {
        var mapping = Iec61850Mapping.Load(MappingPath());
        var writes = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        using var ied = new Iec61850IedServer("simEmu1", 0, mapping) { GooseInterfaceId = "none" };
        ied.Attach(_ => null, (name, value) => writes[name] = value);

        var values = new object?[] { true, true, 80.0, -5.0, 0.99, 690.0, 50.02 };
        Assert.True(ied.TryApplyGoose(1, false, "EMS_PCS01PCS/LLN0.GoCB1", values, out var reason), reason);
        Assert.Equal(1, Convert.ToDouble(writes["yk2"]));
        Assert.Equal(1, Convert.ToDouble(writes["yk3"]));
        Assert.Equal(80.0, Convert.ToDouble(writes["yt0"]));
        Assert.Equal(50.02, Convert.ToDouble(writes["yt4"]), 3);
        Assert.Equal(1u, ied.LastGooseStNum);

        Assert.False(ied.TryApplyGoose(1, false, "EMS_PCS01PCS/LLN0.GoCB1", values, out reason));
        Assert.Equal("stNum", reason);
        Assert.False(ied.TryApplyGoose(2, false, Iec61850PcsModel.LocalGoCbRef(ied.IedName), values, out reason));
        Assert.Equal("local-gocb", reason);
    }
}
