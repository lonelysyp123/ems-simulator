namespace EssSimulator.EssSimModelApi.EnergyManagementSystem
{

    /// <summary>
    /// 数字输入状态
    /// </summary>
    public class DigitalInputStatus
    {
        public int Index { get; set; }
        public string Description { get; set; }
        public bool Value { get; set; }
        public bool Triggered { get; set; }
    }
}
