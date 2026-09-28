namespace EssSimulator.EssDeviceSimModel.Svg
{
    /// <summary>点表根对象 <c>svg</c>。设定值原样保留，夹限只出现在实际无功上。</summary>
    public sealed class SvgProtocolData
    {
        private readonly SvgDevice _device;

        public SvgProtocolData(SvgDevice device) => _device = device;

        public SvgDevice Device => _device;

        public ushort RunState => _device.RunState;
        public double LineVoltageAbV => _device.LineVoltageAbV;
        public double LineVoltageBcV => _device.LineVoltageBcV;
        public double LineVoltageCaV => _device.LineVoltageCaV;
        public double PhaseACurrentA => _device.PhaseACurrentA;
        public double PhaseBCurrentA => _device.PhaseBCurrentA;
        public double PhaseCCurrentA => _device.PhaseCCurrentA;
        public double ReactivePowerKvar => _device.ReactivePowerKvar;
        public double PowerFactor => _device.PowerFactor;
        public double FrequencyHz => _device.FrequencyHz;
        public double RatedCapacityKvar => _device.RatedCapacityKvar;
        public double AvailableReactiveUpperKvar => _device.AvailableReactiveUpperKvar;

        public ushort RunCommand
        {
            get => _device.RunCommand;
            set => _device.RunCommand = value;
        }

        public double ReactiveSetpointKvar
        {
            get => _device.ReactiveSetpointKvar;
            set => _device.ReactiveSetpointKvar = value;
        }
    }
}
