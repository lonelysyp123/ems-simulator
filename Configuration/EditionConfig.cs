namespace EssSimulator.Configuration
{
    /// <summary>
    /// 产品档位开关（appsettings: Simulator.Edition）。
    /// 同一套代码通过 Name 在社区版 / 商业版间切换；发布脚本用不同配置模板写入该节。
    /// 高级能力差异优先体现在 API/菜单屏蔽（白盒切片、3D、组态），拓扑上限仅在 LockTopology 时生效。
    /// </summary>
    public class EditionConfig
    {
        public const string Section = "Simulator:Edition";

        /// <summary>Community / Commercial / Custom（也可用中文：社区版 / 商业版 / 定制版）。</summary>
        public string Name { get; set; } = "Commercial";

        /// <summary>是否锁定电站拓扑规模（社区版预设 true，配合 MaxEssUnits 裁剪）。</summary>
        public bool LockTopology { get; set; }

        /// <summary>允许的最大储能单元数；0 表示不限制。社区版预设 2。</summary>
        public int MaxEssUnits { get; set; }

        /// <summary>是否开放白盒切片等高级 API。社区版强制为 false。</summary>
        public bool AllowDroopSlices { get; set; } = true;

        /// <summary>是否开放主接线 3D 视图。社区版强制为 false。</summary>
        public bool AllowMainline3d { get; set; } = true;

        /// <summary>是否开放组态编辑 / 工程配置（工程模式与点表选型）。社区版强制为 false。</summary>
        public bool AllowTopologyEditor { get; set; } = true;

        public bool IsCommunity =>
            string.Equals(Name, "Community", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Name, "社区版", StringComparison.OrdinalIgnoreCase);

        public bool IsCommercial =>
            string.Equals(Name, "Commercial", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Name, "商业版", StringComparison.OrdinalIgnoreCase);

        public bool IsCustom =>
            string.Equals(Name, "Custom", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Name, "定制版", StringComparison.OrdinalIgnoreCase);

        /// <summary>按 Name 套用档位预设（社区版关闭高级能力并锁定单元上限）。</summary>
        public void ApplyPresets()
        {
            if (!IsCommunity)
                return;

            AllowDroopSlices = false;
            AllowMainline3d = false;
            AllowTopologyEditor = false;
            LockTopology = true;
            if (MaxEssUnits <= 0)
                MaxEssUnits = 2;
        }
    }
}
