using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssSimModelApi.BatteryManagementSystem;
using EssSimulator.EssSimModelApi.EnergyManagementSystem;

namespace EssSimulator.Core
{
    /// <summary>
    /// 类型化存取。内部仍用 <c>ess</c> / <c>emuN</c> / <c>bmsN</c>，供 ObjectPathResolver 使用。
    /// </summary>
    public sealed partial class SimulatorHost
    {
        public void RegisterEss(EnergyStorageSystem ess) => Register("ess", ess);

        public EnergyStorageSystem? TryGetEss() => Get<EnergyStorageSystem>("ess");

        public void RegisterEmu(int unit1Based, EnergyManagementData emu)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(unit1Based, 1);
            Register($"emu{unit1Based}", emu);
        }

        public EnergyManagementData? TryGetEmu(int unit1Based) =>
            unit1Based < 1 ? null : Get<EnergyManagementData>($"emu{unit1Based}");

        public void RegisterBms(int unit1Based, BatteryManagementSystemData bms)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(unit1Based, 1);
            Register($"bms{unit1Based}", bms);
        }

        public BatteryManagementSystemData? TryGetBms(int unit1Based) =>
            unit1Based < 1 ? null : Get<BatteryManagementSystemData>($"bms{unit1Based}");
    }
}
