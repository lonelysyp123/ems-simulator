namespace EssSimulator.Web.Topology
{
    /// <summary>内置组态基础模板（电网 / 母线 / 断路器 / 变压器 / 电表 / 负载 / EMU 虚拟单元 / PCS / 光伏单元 / BMS / 直流母线）。</summary>
    public static class TopologyTemplates
    {
        public static IReadOnlyList<TopologyTemplate> All { get; } = BuildAll();

        public static TopologyTemplate? Get(string id) =>
            All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

        public const string TransformerId = "transformer";
        public const string SplitTransformerId = "split_transformer";
        /// <summary>每个 EMU 可绑定的双耳变压器上限（对应 4 台 PCS / 2 台箱变）。</summary>
        public const int MaxSplitTransformersPerEmu = 2;

        public static bool IsTransformerLike(string? templateId) =>
            string.Equals(templateId, TransformerId, StringComparison.OrdinalIgnoreCase)
            || IsSplitTransformer(templateId);

        public static bool IsSplitTransformer(string? templateId) =>
            string.Equals(templateId, SplitTransformerId, StringComparison.OrdinalIgnoreCase);

        public static bool IsSplitLeftEarPort(string? portId) =>
            portId != null && portId.StartsWith("ear_l_", StringComparison.OrdinalIgnoreCase);

        public static bool IsSplitRightEarPort(string? portId) =>
            portId != null && portId.StartsWith("ear_r_", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 交流快捷成组键。双耳变压器左右耳同在下侧，须各自成组；其余端口回落为空串，仍按 Side 分组。
        /// </summary>
        public static string AcPhaseBundleKey(string? portId)
        {
            if (IsSplitLeftEarPort(portId)) return "ear_l";
            if (IsSplitRightEarPort(portId)) return "ear_r";
            return "";
        }

        private static IReadOnlyList<TopologyTemplate> BuildAll() => new List<TopologyTemplate>
        {
            BuildGrid(),
            BuildAcBus(),
            BuildAcBreaker(),
            BuildTransformer(),
            BuildSplitTransformer(),
            BuildAcMeter(),
            BuildLoad(),
            BuildEmu(),
            BuildEmuGroup(),
            BuildPcs(),
            BuildPvUnit(),
            BuildBms(),
            BuildDcBus()
        };

        private static TopologyTemplate BuildGrid() => new()
        {
            Id = "grid",
            Name = "电网",
            Category = "电源",
            Description = "三相电压源。三个拐角表示 A/B/C 相，默认 220kV，可通过参数修改输出电压。",
            IsVoltageSource = true,
            Ports =
            {
                new() { Id = "a", Label = "A", Kind = "ac_phase", Phase = "A", Side = "bottom", Offset = 0.2, VoltageParam = "outputVoltage", IsVoltageSourcePort = true },
                new() { Id = "b", Label = "B", Kind = "ac_phase", Phase = "B", Side = "bottom", Offset = 0.5, VoltageParam = "outputVoltage", IsVoltageSourcePort = true },
                new() { Id = "c", Label = "C", Kind = "ac_phase", Phase = "C", Side = "bottom", Offset = 0.8, VoltageParam = "outputVoltage", IsVoltageSourcePort = true }
            },
            Parameters =
            {
                new() { Key = "outputVoltage", Label = "输出线电压", Type = "number", Unit = "V", Min = 100, Description = "三相线电压额定值" },
                new() { Key = "frequencyHz", Label = "频率", Type = "number", Unit = "Hz", Min = 45, Max = 65 },
                new() { Key = "shortCircuitMva", Label = "短路容量", Type = "number", Unit = "MVA", Min = 1 }
            },
            DefaultParameters = new Dictionary<string, object?>
            {
                ["outputVoltage"] = 220000d,
                ["frequencyHz"] = 50d,
                ["shortCircuitMva"] = 750d
            }
        };

        private static TopologyTemplate BuildAcBus() => new()
        {
            Id = "ac_bus",
            Name = "三相母线",
            Category = "母线",
            Description = "由 A/B/C 三条独立线组成。上侧拐角每相仅接一台电压源（电网或变压器二次侧）或三相断路器；下侧拐角可挂多台负荷设备。带电由上一级传递：上一级带电且路径合闸，则本段母线带电。同一母线禁止两个电压源。",
            IsVoltageSource = false,
            Ports =
            {
                new() { Id = "a", Label = "A", Kind = "ac_phase", Phase = "A", Side = "top", Offset = 0.2 },
                new() { Id = "b", Label = "B", Kind = "ac_phase", Phase = "B", Side = "top", Offset = 0.5 },
                new() { Id = "c", Label = "C", Kind = "ac_phase", Phase = "C", Side = "top", Offset = 0.8 },
                new() { Id = "a2", Label = "A'", Kind = "ac_phase", Phase = "A", Side = "bottom", Offset = 0.2 },
                new() { Id = "b2", Label = "B'", Kind = "ac_phase", Phase = "B", Side = "bottom", Offset = 0.5 },
                new() { Id = "c2", Label = "C'", Kind = "ac_phase", Phase = "C", Side = "bottom", Offset = 0.8 }
            },
            Parameters =
            {
                new() { Key = "nominalVoltage", Label = "额定电压（只读/推导）", Type = "number", Unit = "V", Description = "由接入电压源决定；也可预填期望值用于匹配校验" },
                new() { Key = "name", Label = "名称", Type = "string" }
            },
            DefaultParameters = new Dictionary<string, object?>
            {
                ["nominalVoltage"] = 0d,
                ["name"] = "AC母线"
            }
        };

        /// <summary>
        /// 三相断路器：上/下各 A/B/C 拐角，中间为三相联动开关。
        /// 串联接入交流回路；对母线带电判定为透明穿越（合闸时电压源可经断路器给母线供电）。
        /// </summary>
        private static TopologyTemplate BuildAcBreaker() => new()
        {
            Id = "ac_breaker",
            Name = "三相断路器",
            Category = "开关",
            Description = "三相开关模型：上方 A/B/C 三个拐角，下方 A/B/C 三个拐角，中间为联动开关。可串联在电网与母线、母线与设备之间。",
            IsVoltageSource = false,
            Ports =
            {
                new() { Id = "a", Label = "A", Kind = "ac_phase", Phase = "A", Side = "top", Offset = 0.2, VoltageParam = "ratedVoltage" },
                new() { Id = "b", Label = "B", Kind = "ac_phase", Phase = "B", Side = "top", Offset = 0.5, VoltageParam = "ratedVoltage" },
                new() { Id = "c", Label = "C", Kind = "ac_phase", Phase = "C", Side = "top", Offset = 0.8, VoltageParam = "ratedVoltage" },
                new() { Id = "a2", Label = "A'", Kind = "ac_phase", Phase = "A", Side = "bottom", Offset = 0.2, VoltageParam = "ratedVoltage" },
                new() { Id = "b2", Label = "B'", Kind = "ac_phase", Phase = "B", Side = "bottom", Offset = 0.5, VoltageParam = "ratedVoltage" },
                new() { Id = "c2", Label = "C'", Kind = "ac_phase", Phase = "C", Side = "bottom", Offset = 0.8, VoltageParam = "ratedVoltage" }
            },
            Parameters =
            {
                new() { Key = "ratedVoltage", Label = "额定线电压", Type = "number", Unit = "V", Min = 100 },
                new() { Key = "closed", Label = "合闸", Type = "boolean", Description = "三相联动；分闸时下游母线视为未带电（非主断时仅组态显示）" },
                new() { Key = "isMainBreaker", Label = "作为主断路器", Type = "boolean", Description = "勾选后与电站概览主断路器状态绑定；全工程有且仅能指定一个" },
                new() { Key = "emuId", Label = "所属 EMU 储能单元", Type = "emu_select", Description = "可选；选择后本断路器归入该 EMU 虚拟单元，作为其单元高压断路器；每个 EMU 至多 1 台" },
                new() { Key = "groupId", Label = "所属 EMU 分组", Type = "group_select", Description = "可选；选择后本断路器归入该 EMU 分组（须先选所属 EMU），作为组级断路器协议镜像；每个分组至多 1 台" },
                new() { Key = "name", Label = "名称", Type = "string" }
            },
            DefaultParameters = new Dictionary<string, object?>
            {
                ["ratedVoltage"] = 220000d,
                ["closed"] = true,
                ["isMainBreaker"] = false,
                ["name"] = "断路器"
            }
        };

        private static TopologyTemplate BuildTransformer() => new()
        {
            Id = "transformer",
            Name = "变压器",
            Category = "变电",
            Description = "上下各三相拐角。通过一次/二次电压配置成各类变压器，要求上大下小。一次侧从母线取电（非电压源），二次侧向下游母线供电；接入母线时该侧电压须与母线匹配。",
            IsVoltageSource = false,
            Ports =
            {
                // 一次侧：受电端，不计入母线电压源
                new() { Id = "pri_a", Label = "H-A", Kind = "ac_phase", Phase = "A", Side = "top", Offset = 0.2, VoltageParam = "primaryVoltage", IsVoltageSourcePort = false },
                new() { Id = "pri_b", Label = "H-B", Kind = "ac_phase", Phase = "B", Side = "top", Offset = 0.5, VoltageParam = "primaryVoltage", IsVoltageSourcePort = false },
                new() { Id = "pri_c", Label = "H-C", Kind = "ac_phase", Phase = "C", Side = "top", Offset = 0.8, VoltageParam = "primaryVoltage", IsVoltageSourcePort = false },
                // 二次侧：向低压母线供电
                new() { Id = "sec_a", Label = "L-A", Kind = "ac_phase", Phase = "A", Side = "bottom", Offset = 0.2, VoltageParam = "secondaryVoltage", IsVoltageSourcePort = true },
                new() { Id = "sec_b", Label = "L-B", Kind = "ac_phase", Phase = "B", Side = "bottom", Offset = 0.5, VoltageParam = "secondaryVoltage", IsVoltageSourcePort = true },
                new() { Id = "sec_c", Label = "L-C", Kind = "ac_phase", Phase = "C", Side = "bottom", Offset = 0.8, VoltageParam = "secondaryVoltage", IsVoltageSourcePort = true }
            },
            Parameters =
            {
                new() { Key = "primaryVoltage", Label = "一次（上）线电压", Type = "number", Unit = "V", Min = 100 },
                new() { Key = "secondaryVoltage", Label = "二次（下）线电压", Type = "number", Unit = "V", Min = 100 },
                new() { Key = "ratedPowerKva", Label = "额定容量", Type = "number", Unit = "kVA", Min = 1 },
                new() { Key = "impedancePercent", Label = "短路阻抗", Type = "number", Unit = "%", Min = 0.1 },
                new() { Key = "noLoadLossW", Label = "空载损耗", Type = "number", Unit = "W", Min = 0 },
                new() { Key = "loadLossW", Label = "负载损耗", Type = "number", Unit = "W", Min = 0 },
                new() { Key = "emuId", Label = "所属 EMU 储能单元", Type = "emu_select", Description = "可选；选择后本变压器归入该 EMU 虚拟单元，作为其单元变压器；同一 EMU 可绑定多台" },
                new() { Key = "groupId", Label = "所属 EMU 分组", Type = "group_select", Description = "可选；选择后本变压器归入该 EMU 分组（须先选所属 EMU），作为组级变压器；同一分组可绑定多台" }
            },
            DefaultParameters = new Dictionary<string, object?>
            {
                ["primaryVoltage"] = 220000d,
                ["secondaryVoltage"] = 35000d,
                ["ratedPowerKva"] = 31500d,
                ["impedancePercent"] = 4d,
                ["noLoadLossW"] = 100d,
                ["loadLossW"] = 200d
            }
        };

        /// <summary>
        /// 双耳变压器（分裂绕组箱变）：高压一组三相，低压左右耳各一组三相。
        /// 须绑定 EMU；每 EMU 至多 2 台。不做无功不平衡保护。
        /// </summary>
        private static TopologyTemplate BuildSplitTransformer() => new()
        {
            Id = SplitTransformerId,
            Name = "双耳变压器",
            Category = "变电",
            Description = "高压一侧、低压左右两耳（分裂绕组）。两耳须接到不同 690V 母线，禁止共母线；须归属 EMU，每个储能单元至多 2 台。",
            IsVoltageSource = false,
            Ports =
            {
                new() { Id = "pri_a", Label = "H-A", Kind = "ac_phase", Phase = "A", Side = "top", Offset = 0.2, VoltageParam = "primaryVoltage", IsVoltageSourcePort = false },
                new() { Id = "pri_b", Label = "H-B", Kind = "ac_phase", Phase = "B", Side = "top", Offset = 0.5, VoltageParam = "primaryVoltage", IsVoltageSourcePort = false },
                new() { Id = "pri_c", Label = "H-C", Kind = "ac_phase", Phase = "C", Side = "top", Offset = 0.8, VoltageParam = "primaryVoltage", IsVoltageSourcePort = false },
                new() { Id = "ear_l_a", Label = "左-A", Kind = "ac_phase", Phase = "A", Side = "bottom", Offset = 0.15, VoltageParam = "secondaryVoltage", IsVoltageSourcePort = true },
                new() { Id = "ear_l_b", Label = "左-B", Kind = "ac_phase", Phase = "B", Side = "bottom", Offset = 0.25, VoltageParam = "secondaryVoltage", IsVoltageSourcePort = true },
                new() { Id = "ear_l_c", Label = "左-C", Kind = "ac_phase", Phase = "C", Side = "bottom", Offset = 0.35, VoltageParam = "secondaryVoltage", IsVoltageSourcePort = true },
                new() { Id = "ear_r_a", Label = "右-A", Kind = "ac_phase", Phase = "A", Side = "bottom", Offset = 0.65, VoltageParam = "secondaryVoltage", IsVoltageSourcePort = true },
                new() { Id = "ear_r_b", Label = "右-B", Kind = "ac_phase", Phase = "B", Side = "bottom", Offset = 0.75, VoltageParam = "secondaryVoltage", IsVoltageSourcePort = true },
                new() { Id = "ear_r_c", Label = "右-C", Kind = "ac_phase", Phase = "C", Side = "bottom", Offset = 0.85, VoltageParam = "secondaryVoltage", IsVoltageSourcePort = true }
            },
            Parameters =
            {
                new() { Key = "primaryVoltage", Label = "一次（上）线电压", Type = "number", Unit = "V", Min = 100 },
                new() { Key = "secondaryVoltage", Label = "二次（耳）线电压", Type = "number", Unit = "V", Min = 100 },
                new() { Key = "ratedPowerKva", Label = "额定容量（总）", Type = "number", Unit = "kVA", Min = 1, Description = "两耳合计；默认 6300 kVA，单耳一半" },
                new() { Key = "splitRatio", Label = "分裂比（左耳）", Type = "number", Min = 0.1, Max = 0.9, Description = "左耳容量占比，默认 0.5" },
                new() { Key = "impedancePercent", Label = "穿越短路阻抗", Type = "number", Unit = "%", Min = 0.1 },
                new() { Key = "splitImpedancePercent", Label = "分裂阻抗", Type = "number", Unit = "%", Min = 0.1, Description = "两低压绕组之间的阻抗，用于估算环流" },
                new() { Key = "noLoadLossW", Label = "空载损耗", Type = "number", Unit = "W", Min = 0 },
                new() { Key = "loadLossW", Label = "负载损耗", Type = "number", Unit = "W", Min = 0 },
                new() { Key = "emuId", Label = "所属 EMU 储能单元", Type = "emu_select", Description = "必选；本双耳变压器归入该 EMU，作为其单元箱变；每个 EMU 至多 2 台" },
                new() { Key = "groupId", Label = "所属 EMU 分组", Type = "group_select", Description = "可选；选择后本变压器归入该 EMU 分组（须先选所属 EMU）" }
            },
            DefaultParameters = new Dictionary<string, object?>
            {
                ["primaryVoltage"] = 35000d,
                ["secondaryVoltage"] = 690d,
                ["ratedPowerKva"] = 6300d,
                ["splitRatio"] = 0.5d,
                ["impedancePercent"] = 6d,
                ["splitImpedancePercent"] = 8d,
                ["noLoadLossW"] = 80d,
                ["loadLossW"] = 400d
            }
        };

        /// <summary>
        /// 三相电表：上方三相拐角在模型层同时表示 PT 与 CT（统一测量抽头）。
        /// 属性中仍分别配置 PT 变比与 CT 变比；连线只需接母线三相一次即可同时取得电压与电流。
        /// </summary>
        private static TopologyTemplate BuildAcMeter() => new()
        {
            Id = "ac_meter",
            Name = "三相电表",
            Category = "测量",
            Description = "上方三个拐角为一次侧统一测量抽头（同时含 PT/CT）：接母线 A/B/C 即可。二次侧（PT 100V / CT 5A）由仿真固定，组态无需填写。",
            IsVoltageSource = false,
            Ports =
            {
                new() { Id = "pt_a", Label = "PT/CT-A", Kind = "ac_phase", Phase = "A", Side = "top", Offset = 0.2, VoltageParam = "ptPrimaryVoltage" },
                new() { Id = "pt_b", Label = "PT/CT-B", Kind = "ac_phase", Phase = "B", Side = "top", Offset = 0.5, VoltageParam = "ptPrimaryVoltage" },
                new() { Id = "pt_c", Label = "PT/CT-C", Kind = "ac_phase", Phase = "C", Side = "top", Offset = 0.8, VoltageParam = "ptPrimaryVoltage" }
            },
            Parameters =
            {
                // 三拐角只建模一次侧挂点；二次额定在运行时固定为 PT 100V / CT 5A
                new() { Key = "ptPrimaryVoltage", Label = "额定线电压（一次）", Type = "number", Unit = "V", Min = 100, Description = "被测母线一次线电压，须与母线匹配" },
                new() { Key = "ctPrimaryCurrent", Label = "额定电流（一次）", Type = "number", Unit = "A", Min = 1, Description = "CT 一次额定电流" },
                new() { Key = "accuracyClass", Label = "精度等级", Type = "string" },
                new() { Key = "isPccMeter", Label = "作为并网点电表", Type = "boolean", Description = "勾选后与电站概览电表数据绑定；全工程有且仅能指定一个" },
                new() { Key = "emuId", Label = "所属 EMU 储能单元", Type = "emu_select", Description = "可选；选择后本电表归入该 EMU 虚拟单元，作为其单元电表；每个 EMU 至多 1 台" },
                new() { Key = "groupId", Label = "所属 EMU 分组", Type = "group_select", Description = "可选；选择后本电表归入该 EMU 分组（须先选所属 EMU），作为组级电表协议镜像；每个分组可绑定多台" }
            },
            DefaultParameters = new Dictionary<string, object?>
            {
                ["ptPrimaryVoltage"] = 220000d,
                ["ctPrimaryCurrent"] = 2000d,
                ["accuracyClass"] = "0.2S",
                ["isPccMeter"] = false
            }
        };

        /// <summary>
        /// 站用/馈线负载：纯有功消耗模型（有功仅允许 ≤0），无功可感性/容性。
        /// 应用组态后绑定电站概览「35kV 负载」有功/无功。
        /// </summary>
        private static TopologyTemplate BuildLoad() => new()
        {
            Id = "load",
            Name = "负载",
            Category = "负荷",
            Description = "有功消耗模型：有功仅能从电网取电（设定值 ≤0，负=消耗）；无功可正可负（感性/容性）。初始化默认有功/无功均为 0，并与主接线电站概览负载数据绑定。",
            IsVoltageSource = false,
            Ports =
            {
                new() { Id = "a", Label = "A", Kind = "ac_phase", Phase = "A", Side = "top", Offset = 0.2, VoltageParam = "ratedVoltage" },
                new() { Id = "b", Label = "B", Kind = "ac_phase", Phase = "B", Side = "top", Offset = 0.5, VoltageParam = "ratedVoltage" },
                new() { Id = "c", Label = "C", Kind = "ac_phase", Phase = "C", Side = "top", Offset = 0.8, VoltageParam = "ratedVoltage" }
            },
            Parameters =
            {
                new() { Key = "ratedVoltage", Label = "额定线电压", Type = "number", Unit = "V", Min = 100, Description = "接入母线电压等级，须与母线匹配" },
                new()
                {
                    Key = "activePowerKw",
                    Label = "有功功率",
                    Type = "number",
                    Unit = "kW",
                    Max = 0,
                    Description = "仅允许 ≤0：负值=消耗有功，禁止向电网释放（正值）"
                },
                new()
                {
                    Key = "reactivePowerKvar",
                    Label = "无功功率",
                    Type = "number",
                    Unit = "kvar",
                    Description = "可正可负：正/负分别对应容性或感性无功（与仿真约定一致）"
                },
                new() { Key = "name", Label = "名称", Type = "string" }
            },
            DefaultParameters = new Dictionary<string, object?>
            {
                ["ratedVoltage"] = 220000d,
                ["activePowerKw"] = 0d,
                ["reactivePowerKvar"] = 0d,
                ["name"] = "站用负载"
            }
        };

        /// <summary>
        /// EMU 储能单元（虚拟）：无图形、无端口，作为 PCS / 断路器 / 电表的归属容器；
        /// 设备通过 emuId 参数下拉框归入。单元变参数属单元级，在此配置。
        /// </summary>
        private static TopologyTemplate BuildEmu() => new()
        {
            Id = "emu",
            Name = "EMU 储能单元",
            Category = "储能",
            Description = "虚拟储能单元：拖入后画布不显示图形，在左侧「EMU 储能单元」列表中管理；PCS 变流器支路、断路器、电表通过其「所属 EMU 储能单元」下拉框归入本单元（断路器/电表各至多 1 台）。",
            IsVoltageSource = false,
            IsVirtual = true,
            Parameters =
            {
                new() { Key = "unitXfPrimaryV", Label = "单元变一次电压", Type = "number", Unit = "V" },
                new() { Key = "unitXfSecondaryV", Label = "单元变二次电压", Type = "number", Unit = "V" },
                new() { Key = "unitXfRatedKva", Label = "单元变额定容量", Type = "number", Unit = "kVA", Min = 1 },
                new() { Key = "name", Label = "名称", Type = "string" }
            },
            DefaultParameters = new Dictionary<string, object?>
            {
                ["unitXfPrimaryV"] = 35000d,
                ["unitXfSecondaryV"] = 690d,
                ["unitXfRatedKva"] = 6300d,
                ["name"] = "EMU储能单元"
            }
        };

        /// <summary>
        /// EMU 分组（虚拟）：EMU 内的协议分层容器（EMU → group → PCS 支路），
        /// 无图形、无端口；设备通过 groupId 参数归入。纯协议聚合，不影响电气接线。
        /// </summary>
        private static TopologyTemplate BuildEmuGroup() => new()
        {
            Id = "emu_group",
            Name = "EMU 分组",
            Category = "储能",
            Description = "EMU 内 PCS 组（虚拟）：拖入后画布不显示图形，在左侧「EMU 储能单元」列表中管理；PCS 变流器支路、断路器、电表通过其「所属 EMU 分组」下拉框归入本组（组级断路器至多 1 台，电表可多台）。",
            IsVoltageSource = false,
            IsVirtual = true,
            Parameters =
            {
                new() { Key = "emuId", Label = "所属 EMU 储能单元", Type = "emu_select", Description = "本分组归属的 EMU 储能单元" },
                new() { Key = "name", Label = "名称", Type = "string" }
            },
            DefaultParameters = new Dictionary<string, object?>
            {
                ["name"] = "EMU分组"
            }
        };

        /// <summary>
        /// PCS 变流器支路：一台 PCS 由两条支路组成；本节点是一条支路。
        /// 上三相 AC 接集电母线，下正/负 DC 接 BMS（或直流母线）；通过 emuId 归入 EMU。
        /// </summary>
        private static TopologyTemplate BuildPcs() => new()
        {
            Id = "pcs",
            Name = "PCS 变流器支路",
            Category = "储能",
            Description = "PCS 的一条变流器支路（两条支路组成一台 PCS）：上三相 AC 接入 35kV 集电母线，下正/负两路 DC 接 BMS 电池堆（可经直流母线）；在参数中选择所属 EMU 储能单元。",
            IsVoltageSource = false,
            Ports =
            {
                new() { Id = "ac_a", Label = "AC-A", Kind = "ac_phase", Phase = "A", Side = "top", Offset = 0.2, VoltageParam = "acVoltage" },
                new() { Id = "ac_b", Label = "AC-B", Kind = "ac_phase", Phase = "B", Side = "top", Offset = 0.5, VoltageParam = "acVoltage" },
                new() { Id = "ac_c", Label = "AC-C", Kind = "ac_phase", Phase = "C", Side = "top", Offset = 0.8, VoltageParam = "acVoltage" },
                new() { Id = "dc_pos", Label = "DC+", Kind = "dc_pos", Side = "bottom", Offset = 0.35 },
                new() { Id = "dc_neg", Label = "DC−", Kind = "dc_neg", Side = "bottom", Offset = 0.65 }
            },
            Parameters =
            {
                new() { Key = "emuId", Label = "所属 EMU 储能单元", Type = "emu_select", Description = "选择后本支路归入该 EMU 虚拟单元" },
                new() { Key = "groupId", Label = "所属 EMU 分组", Type = "group_select", Description = "可选；选择后本 PCS 支路归入该 PCS 组（须先选所属 EMU；未选时支路直挂 EMU）" },
                new() { Key = "acVoltage", Label = "交流侧线电压", Type = "number", Unit = "V", Min = 100, Description = "接入 AC 母线侧额定，默认 35kV" },
                new() { Key = "pcsRatedPowerKw", Label = "PCS 额定功率", Type = "number", Unit = "kW" },
                new() { Key = "pcsMaxPowerKw", Label = "PCS 最大功率", Type = "number", Unit = "kW" },
                new() { Key = "pcsEfficiency", Label = "PCS 效率", Type = "number", Min = 0.5, Max = 1 },
                new() { Key = "dcVoltageMin", Label = "直流电压下限", Type = "number", Unit = "V" },
                new() { Key = "dcVoltageMax", Label = "直流电压上限", Type = "number", Unit = "V" },
                new() { Key = "name", Label = "名称", Type = "string" }
            },
            DefaultParameters = new Dictionary<string, object?>
            {
                ["acVoltage"] = 35000d,
                ["pcsRatedPowerKw"] = 1725d,
                ["pcsMaxPowerKw"] = 1897.5d,
                ["pcsEfficiency"] = 0.99d,
                ["dcVoltageMin"] = 1000d,
                ["dcVoltageMax"] = 1500d,
                ["name"] = "PCS变流器支路"
            }
        };

        /// <summary>
        /// 光伏单元：30 块组件串联为一簇 × 16 簇为单台逆变器；
        /// 一台 35kV/690V 箱变下挂 16 台 320 kW 单向逆变器。
        /// </summary>
        private static TopologyTemplate BuildPvUnit() => new()
        {
            Id = "pv_unit",
            Name = "光伏单元",
            Category = "光伏",
            Description = "内部简化为：单台逆变器 30 块 TSM-NEG21C.20Q 串联为一簇 × 16 簇、320 kW / 690 V 只放电；一台 35kV/690V 5120 kVA 箱变下挂 16 台逆变器。上方三相交流接入 35kV 集电母线。",
            IsVoltageSource = false,
            Ports =
            {
                new() { Id = "ac_a", Label = "AC-A", Kind = "ac_phase", Phase = "A", Side = "top", Offset = 0.2, VoltageParam = "acVoltage" },
                new() { Id = "ac_b", Label = "AC-B", Kind = "ac_phase", Phase = "B", Side = "top", Offset = 0.5, VoltageParam = "acVoltage" },
                new() { Id = "ac_c", Label = "AC-C", Kind = "ac_phase", Phase = "C", Side = "top", Offset = 0.8, VoltageParam = "acVoltage" }
            },
            Parameters =
            {
                new() { Key = "acVoltage", Label = "交流侧线电压", Type = "number", Unit = "V", Min = 100, Description = "接入 AC 母线侧额定，默认 35kV（箱变高压侧）" },
                new() { Key = "unitXfPrimaryV", Label = "箱变一次电压", Type = "number", Unit = "V" },
                new() { Key = "unitXfSecondaryV", Label = "箱变二次电压", Type = "number", Unit = "V", Description = "逆变器交流侧，默认 690 V（与储能 PCS 相同）" },
                new() { Key = "unitXfRatedKva", Label = "箱变额定容量", Type = "number", Unit = "kVA", Min = 1, Description = "默认 16×320 kW = 5120 kVA" },
                new() { Key = "moduleModel", Label = "组件型号", Type = "string" },
                new() { Key = "operatingYears", Label = "投运年限", Type = "number", Unit = "年", Min = 0, Description = "0 为新产品；满 1 年后按组件首年/逐年衰减缩放电流" },
                new() { Key = "groundAlbedo", Label = "地面反照率", Type = "number", Min = 0, Max = 1, Description = "方阵 A/B 反照率初值；0 关闭双面增益。G_rear = 反照率 × 平面辐照" },
                new() { Key = "modulesPerString", Label = "每簇串联块数", Type = "number", Min = 1 },
                new() { Key = "stringCount", Label = "单台逆变器簇数", Type = "number", Min = 1 },
                new() { Key = "inverterCount", Label = "箱变下逆变器台数", Type = "number", Min = 1, Description = "同一台 35kV/690V 箱变低压侧并联的单向逆变器数量" },
                new() { Key = "inverterRatedPowerKw", Label = "单台逆变器额定功率", Type = "number", Unit = "kW", Min = 1 },
                new() { Key = "inverterMaxPowerKw", Label = "单台逆变器最大功率", Type = "number", Unit = "kW", Min = 1 },
                new() { Key = "inverterEfficiency", Label = "逆变器效率", Type = "number", Min = 0.5, Max = 1 },
                new() { Key = "inverterAcVoltage", Label = "逆变器交流线电压", Type = "number", Unit = "V", Min = 100 },
                new() { Key = "dcVoltageMin", Label = "直流电压下限", Type = "number", Unit = "V" },
                new() { Key = "dcVoltageMax", Label = "直流电压上限", Type = "number", Unit = "V" }
            },
            DefaultParameters = new Dictionary<string, object?>
            {
                ["acVoltage"] = 35000d,
                ["unitXfPrimaryV"] = 35000d,
                ["unitXfSecondaryV"] = 690d,
                ["unitXfRatedKva"] = 5120d,
                ["moduleModel"] = EssDeviceSimModel.Pv.TrinaPvModuleCatalog.Neg21c20qModel,
                ["operatingYears"] = 0d,
                ["groundAlbedo"] = 0d,
                ["modulesPerString"] = 30d,
                ["stringCount"] = 16d,
                ["inverterCount"] = 16d,
                ["inverterRatedPowerKw"] = 320d,
                ["inverterMaxPowerKw"] = 352d,
                ["inverterEfficiency"] = 0.99d,
                ["inverterAcVoltage"] = 690d,
                ["dcVoltageMin"] = 500d,
                ["dcVoltageMax"] = 1500d
            }
        };

        private static TopologyTemplate BuildBms() => new()
        {
            Id = "bms",
            Name = "BMS 电池堆",
            Category = "储能",
            Description = "电池堆模型：电芯→Pack→簇→堆。参数对齐现有 BmsDeviceConfig。上方正/负两路 DC 拐角。",
            IsVoltageSource = false,
            Ports =
            {
                new() { Id = "dc_pos", Label = "DC+", Kind = "dc_pos", Side = "top", Offset = 0.35 },
                new() { Id = "dc_neg", Label = "DC−", Kind = "dc_neg", Side = "top", Offset = 0.65 }
            },
            Parameters =
            {
                new() { Key = "name", Label = "名称", Type = "string" },
                new() { Key = "clusterCount", Label = "簇数", Type = "number", Min = 1 },
                new() { Key = "packCount", Label = "每簇 Pack 数", Type = "number", Min = 1 },
                new() { Key = "cellSeriesCount", Label = "Pack 串联电芯数", Type = "number", Min = 1 },
                new() { Key = "cellParallelCount", Label = "并联数", Type = "number", Min = 1 },
                new() { Key = "cellNominalVoltage", Label = "电芯标称电压", Type = "number", Unit = "V" },
                new() { Key = "cellNominalCapacity", Label = "电芯标称容量", Type = "number", Unit = "Ah" },
                new() { Key = "cellInitialSoc", Label = "初始 SOC", Type = "number", Min = 0, Max = 1 },
                new() { Key = "rackInternalResistance", Label = "堆内阻", Type = "number", Unit = "Ω" }
            },
            DefaultParameters = new Dictionary<string, object?>
            {
                ["name"] = "BMS",
                ["clusterCount"] = 12d,
                ["packCount"] = 4d,
                ["cellSeriesCount"] = 104d,
                ["cellParallelCount"] = 1d,
                ["cellNominalVoltage"] = 3.2d,
                ["cellNominalCapacity"] = 314d,
                ["cellInitialSoc"] = 0.5d,
                ["rackInternalResistance"] = 0.02d
            }
        };

        private static TopologyTemplate BuildDcBus() => new()
        {
            Id = "dc_bus",
            Name = "直流母线",
            Category = "母线",
            Description = "直流母线：上下各一对正/负拐角，同极性可挂多台设备；正极只能接正极，负极只能接负极。",
            IsVoltageSource = false,
            Ports =
            {
                new() { Id = "pos_t", Label = "+", Kind = "dc_pos", Side = "top", Offset = 0.35 },
                new() { Id = "neg_t", Label = "−", Kind = "dc_neg", Side = "top", Offset = 0.65 },
                new() { Id = "pos_b", Label = "+", Kind = "dc_pos", Side = "bottom", Offset = 0.35 },
                new() { Id = "neg_b", Label = "−", Kind = "dc_neg", Side = "bottom", Offset = 0.65 }
            },
            Parameters =
            {
                new() { Key = "nominalVoltage", Label = "标称直流电压", Type = "number", Unit = "V", Min = 0 },
                new() { Key = "name", Label = "名称", Type = "string" }
            },
            DefaultParameters = new Dictionary<string, object?>
            {
                ["nominalVoltage"] = 1200d,
                ["name"] = "DC母线"
            }
        };
    }
}
