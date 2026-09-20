using System;

namespace EssSimulator.EssDeviceSimModel
{
    /// <summary>
    /// 逻辑仿真时钟：dt 与墙钟无关。Host 的 PeriodicTimer 只控制推进速率。
    /// IntegrationDt = Dt × multiplier，倍数仍只放大 SOC/电能积分（阶段 1 已知偏差）。
    /// </summary>
    public sealed class SimulationClock
    {
        public SimulationClock(TimeSpan dt, double integrationMultiplier = 1.0, DateTime? startUtc = null)
        {
            if (dt <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(dt), "逻辑步长必须为正。");

            Dt = dt;
            double mult = integrationMultiplier > 0 ? integrationMultiplier : 1.0;
            IntegrationDt = TimeSpan.FromTicks(Math.Max(1, (long)(dt.Ticks * mult)));
            SimTime = startUtc ?? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
            if (SimTime.Kind == DateTimeKind.Unspecified)
                SimTime = DateTime.SpecifyKind(SimTime, DateTimeKind.Utc);
        }

        public TimeSpan Dt { get; }
        public TimeSpan IntegrationDt { get; }
        public DateTime SimTime { get; private set; }
        public int TickCount { get; private set; }

        /// <summary>推进一拍逻辑时间，返回本拍 (simTime, dt, integrationDt)。</summary>
        public (DateTime SimTime, TimeSpan Dt, TimeSpan IntegrationDt) Tick()
        {
            SimTime = SimTime.Add(Dt);
            TickCount++;
            return (SimTime, Dt, IntegrationDt);
        }
    }
}
