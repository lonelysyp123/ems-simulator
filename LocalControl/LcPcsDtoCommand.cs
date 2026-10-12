using EssSimulator.EssSimModelApi.Mappers;

namespace EssSimulator.LocalControl
{
    /// <summary>
    /// emu.csv 只有 2 个协议槽；组内 pcs3/pcs4（扁平下标 ≥2）写 DTO 后必须走命令链，
    /// 否则只改镜像、物理 PCS 仍停机。
    /// </summary>
    internal static class LcPcsDtoCommand
    {
        public static bool WriteAndApply(int unitId, int flatPcsIndex, string dtoField, object value)
        {
            try
            {
                if (!SimServer.SetExtIfVariableVal($"emu{unitId}.PcsList[{flatPcsIndex}].{dtoField}", value))
                    return false;
            }
            catch
            {
                return false;
            }

            return EmuCommandPipeline.TryApplyUnit(unitId);
        }
    }
}
