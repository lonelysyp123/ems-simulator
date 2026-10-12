namespace EssSimulator.EssDeviceSimModel.Model
{
    public static class AcQuantityConverter
    {
        public static AcTerminalQuantities ToTerminal(AcInternalQuantities internalQty)
        {
            double uLine = internalQty.LineVoltageV;
            double iLine = internalQty.LineCurrentA;

            return internalQty.Connection switch
            {
                ThreePhaseConnection.Star => new AcTerminalQuantities
                {
                    Connection = ThreePhaseConnection.Star,
                    Vab = uLine,
                    Vbc = uLine,
                    Vca = uLine,
                    Van = uLine / ElectricalConventions.Sqrt3,
                    Vbn = uLine / ElectricalConventions.Sqrt3,
                    Vcn = uLine / ElectricalConventions.Sqrt3,
                    Ia = iLine,
                    Ib = iLine,
                    Ic = iLine
                },
                ThreePhaseConnection.Delta => new AcTerminalQuantities
                {
                    Connection = ThreePhaseConnection.Delta,
                    Vab = uLine,
                    Vbc = uLine,
                    Vca = uLine,
                    Van = uLine,
                    Vbn = uLine,
                    Vcn = uLine,
                    Ia = iLine / ElectricalConventions.Sqrt3,
                    Ib = iLine / ElectricalConventions.Sqrt3,
                    Ic = iLine / ElectricalConventions.Sqrt3
                },
                _ => throw new ArgumentOutOfRangeException(nameof(internalQty.Connection))
            };
        }

        public static AcInternalQuantities FromTerminal(AcTerminalQuantities terminal)
        {
            double uLine = terminal.Vab;
            double iLine = terminal.Connection switch
            {
                ThreePhaseConnection.Star => terminal.Ia,
                ThreePhaseConnection.Delta => terminal.Ia * ElectricalConventions.Sqrt3,
                _ => throw new ArgumentOutOfRangeException(nameof(terminal.Connection))
            };

            return new AcInternalQuantities
            {
                Connection = terminal.Connection,
                LineVoltageV = uLine,
                LineCurrentA = Math.Abs(iLine),
                PhaseAngleDeg = iLine < 0 ? 180.0 : 0.0
            };
        }

        /// <summary>由 P/Q 控制意图换算线电流幅值与相位（用于设备边界或母线汇总前的意图转换）。</summary>
        public static AcInternalQuantities FromLineVoltageAndPower(
            double lineVoltageV,
            double activePowerKw,
            double reactivePowerKvar,
            ThreePhaseConnection connection,
            double frequencyHz = 50.0)
        {
            var (lineCurrentA, phaseAngleDeg) = FromPowerToPhasor(
                lineVoltageV, activePowerKw, reactivePowerKvar);

            return new AcInternalQuantities
            {
                Connection = connection,
                LineVoltageV = lineVoltageV,
                LineCurrentA = lineCurrentA,
                PhaseAngleDeg = phaseAngleDeg,
                FrequencyHz = frequencyHz
            };
        }

        public static AcInternalQuantities FromLineVoltageAndCurrent(
            double lineVoltageV,
            double lineCurrentA,
            ThreePhaseConnection connection,
            double frequencyHz = 50.0)
        {
            return new AcInternalQuantities
            {
                Connection = connection,
                LineVoltageV = lineVoltageV,
                LineCurrentA = Math.Abs(lineCurrentA),
                PhaseAngleDeg = lineCurrentA < 0 ? 180.0 : 0.0,
                FrequencyHz = frequencyHz
            };
        }

        public static (double LineCurrentA, double PhaseAngleDeg) FromPowerToPhasor(
            double lineVoltageV,
            double activePowerKw,
            double reactivePowerKvar)
        {
            if (lineVoltageV <= 1e-9)
                return (0, 0);

            double apparentKva = Math.Sqrt(
                activePowerKw * activePowerKw + reactivePowerKvar * reactivePowerKvar);
            if (apparentKva <= 1e-9)
                return (0, 0);

            double lineCurrentA = LineCurrentFromApparent(lineVoltageV, apparentKva);
            double phaseAngleDeg = Math.Atan2(reactivePowerKvar, activePowerKw) * 180.0 / Math.PI;
            return (lineCurrentA, phaseAngleDeg);
        }

        public static double LineCurrentFromApparent(double lineVoltageV, double apparentKva)
        {
            if (lineVoltageV <= 1e-9 || apparentKva <= 0)
                return 0;

            return apparentKva * 1000.0 / (lineVoltageV * ElectricalConventions.Sqrt3);
        }

        /// <summary>功率不变时把线电流从 from 电压折到 to 电压：I2 = I1 · V1 / V2。</summary>
        public static double ReferLineCurrent(double lineCurrentA, double fromLineVoltageV, double toLineVoltageV)
        {
            if (fromLineVoltageV <= 1e-9 || toLineVoltageV <= 1e-9)
                return 0;
            return lineCurrentA * fromLineVoltageV / toLineVoltageV;
        }

        public static double ComputeActivePowerKw(double lineVoltageV, double lineCurrentA, double phaseAngleDeg)
        {
            if (!InputsFinite(lineVoltageV, lineCurrentA, phaseAngleDeg))
                return 0;
            if (lineVoltageV <= 1e-9 || Math.Abs(lineCurrentA) <= 1e-9)
                return 0;

            return lineVoltageV * lineCurrentA * ElectricalConventions.Sqrt3 / 1000.0
                   * Math.Cos(phaseAngleDeg * Math.PI / 180.0);
        }

        public static double ComputeReactivePowerKvar(double lineVoltageV, double lineCurrentA, double phaseAngleDeg)
        {
            if (!InputsFinite(lineVoltageV, lineCurrentA, phaseAngleDeg))
                return 0;
            if (lineVoltageV <= 1e-9 || Math.Abs(lineCurrentA) <= 1e-9)
                return 0;

            return lineVoltageV * lineCurrentA * ElectricalConventions.Sqrt3 / 1000.0
                   * Math.Sin(phaseAngleDeg * Math.PI / 180.0);
        }

        public static double ComputeApparentPowerKva(double lineVoltageV, double lineCurrentA)
        {
            if (!double.IsFinite(lineVoltageV) || !double.IsFinite(lineCurrentA))
                return 0;
            return Math.Abs(ComputeActivePowerKw(lineVoltageV, lineCurrentA, 0))
                > 1e-9 || Math.Abs(lineCurrentA) > 1e-9
                    ? Math.Abs(lineVoltageV * lineCurrentA * ElectricalConventions.Sqrt3) / 1000.0
                    : 0;
        }

        public static double ComputePowerFactor(double lineVoltageV, double lineCurrentA, double phaseAngleDeg)
        {
            if (!InputsFinite(lineVoltageV, lineCurrentA, phaseAngleDeg))
                return 1.0;

            double apparentKva = ComputeApparentPowerKva(lineVoltageV, lineCurrentA);
            if (apparentKva <= 1e-9)
                return 1.0;

            return ComputeActivePowerKw(lineVoltageV, lineCurrentA, phaseAngleDeg) / apparentKva;
        }

        private static bool InputsFinite(double lineVoltageV, double lineCurrentA, double phaseAngleDeg) =>
            double.IsFinite(lineVoltageV) && double.IsFinite(lineCurrentA) && double.IsFinite(phaseAngleDeg);

        /// <summary>
        /// 电表/联调用有符号功率因数：幅值 = |P|/S，符号与无功 Q 同号（与充放电方向无关）。
        /// 约定与 <see cref="ElectricalConventions.ReactivePowerCapacitiveSign"/> 一致：
        /// Q&gt;0（容性）→ PF&gt;0；Q&lt;0（感性）→ PF&lt;0；Q≈0 → PF≥0。
        /// </summary>
        public static double ComputeSignedPowerFactor(double activePowerKw, double reactivePowerKvar)
        {
            double apparentKva = Math.Sqrt(
                activePowerKw * activePowerKw + reactivePowerKvar * reactivePowerKvar);
            if (apparentKva <= 1e-9)
                return 1.0;

            double magnitude = Math.Abs(activePowerKw) / apparentKva;
            if (Math.Abs(reactivePowerKvar) <= 1e-9)
                return Math.Clamp(magnitude, 0.0, 1.0);

            // 功率因数正负与无功同号：Q>0 → PF>0，Q<0 → PF<0
            return Math.Clamp(Math.Sign(reactivePowerKvar) * magnitude, -1.0, 1.0);
        }
    }
}
