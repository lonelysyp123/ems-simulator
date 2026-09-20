using System.Numerics;

namespace EssSimulator.EssDeviceSimModel.Kernel
{
    public enum DomainEventKind
    {
        None,
        Upper,
        Lower
    }

    /// <summary>
    /// 独立宿主中的分域交接：上域出戴维南，下域回 P/Q。
    /// 漏抗归上域；下域 P/Q 晚一拍进入上域；上域事件重跑 a–d，下域事件只重跑 c–d。
    /// 不写入 PlantEngine。
    /// </summary>
    public sealed class SplitDomainHandshake
    {
        public SplitDomainHandshake(IsolatedAcIsland lower, Complex gridEmf, Complex gridZ, Complex theveninZ)
        {
            if (gridZ.Magnitude < PhasorNetwork.MinSeriesImpedanceOhm)
                throw new NetworkSolveException("Upper-domain GridZ must be finite.");
            if (theveninZ.Magnitude < PhasorNetwork.MinSeriesImpedanceOhm)
                throw new NetworkSolveException("Thevenin Zth must be finite.");

            Lower = lower;
            GridEmf = gridEmf;
            GridZ = gridZ;
            TheveninZ = theveninZ;
        }

        public IsolatedAcIsland Lower { get; }
        public Complex GridEmf { get; set; }
        public Complex GridZ { get; }
        public Complex TheveninZ { get; }

        /// <summary>已提交的下域注入（送入上域），本拍上域仍用上一拍值。</summary>
        public Complex CommittedInjectionVa { get; private set; }

        /// <summary>已提交的上域开路电压。下域事件本拍沿用，不按新 P/Q 重算。</summary>
        public Complex CommittedVoc { get; private set; }

        /// <summary>已提交的戴维南源侧电流 I = (Voc − Vbus) / Zth，供下一拍上域压降。</summary>
        public Complex CommittedTheveninCurrent { get; private set; }

        /// <summary>测试钩子：下域求解失败时整组不提交。</summary>
        public bool FailNextLower { get; set; }

        public SplitDomainHandshake CreateShadow()
        {
            return new SplitDomainHandshake(Lower.CloneTopology(), GridEmf, GridZ, TheveninZ);
        }

        public bool TryTick(DomainEventKind kind, out SplitDomainTickResult result)
        {
            Complex voc = kind == DomainEventKind.Lower
                ? VocForLowerOnlyEvent()
                : GridEmf - CommittedTheveninCurrent * GridZ;

            Complex prevS = CommittedInjectionVa;
            Complex prevVoc = CommittedVoc;
            Complex prevI = CommittedTheveninCurrent;

            try
            {
                if (FailNextLower)
                    throw new NetworkSolveException("Forced lower-domain solve failure.");

                Lower.SetThevenin(voc, TheveninZ);
                IslandSolveResult island = Lower.Solve();
                Complex injection = island.InjectionIntoUpperVa;
                Complex iTh = island.TheveninCurrentIntoBus;

                CommittedInjectionVa = injection;
                CommittedVoc = voc;
                CommittedTheveninCurrent = iTh;
                result = new SplitDomainTickResult(voc, island, injection);
                return true;
            }
            catch (NetworkSolveException)
            {
                CommittedInjectionVa = prevS;
                CommittedVoc = prevVoc;
                CommittedTheveninCurrent = prevI;
                result = new SplitDomainTickResult(voc, null, prevS);
                return false;
            }
            finally
            {
                FailNextLower = false;
            }
        }

        private Complex VocForLowerOnlyEvent() =>
            CommittedVoc.Magnitude > 0 ? CommittedVoc : GridEmf;
    }

    public sealed class SplitDomainTickResult
    {
        internal SplitDomainTickResult(Complex voc, IslandSolveResult? lower, Complex injectionIntoUpperVa)
        {
            Voc = voc;
            Lower = lower;
            InjectionIntoUpperVa = injectionIntoUpperVa;
        }

        public Complex Voc { get; }
        public IslandSolveResult? Lower { get; }
        public Complex InjectionIntoUpperVa { get; }
    }
}
