namespace EssSimulator.Core
{
    public enum ExternalControlOwner
    {
        None = 0,
        ThirdPartyEms = 1,
        EmsStrategy = 2
    }

    /// <summary>
    /// 第三方 EMS 或电站 EMS 策略占用控制权时，拦截 Modbus 控制管道、LC 回写、dpc/内部直控等外部指令。
    /// </summary>
    public static class ExternalControlGate
    {
        public const string ThirdPartyBlockedMessage = "第三方 EMS 占用中，已拒绝外部指令";
        public const string StrategyBlockedMessage = "EMS 策略占用中，已拒绝外部指令";
        public const string BlockedMessage = ThirdPartyBlockedMessage;

        private static int _owner;

        public static ExternalControlOwner Owner =>
            (ExternalControlOwner)Volatile.Read(ref _owner);

        public static bool IsBlocked => Owner != ExternalControlOwner.None;

        public static string CurrentMessage => Owner switch
        {
            ExternalControlOwner.EmsStrategy => StrategyBlockedMessage,
            ExternalControlOwner.ThirdPartyEms => ThirdPartyBlockedMessage,
            _ => ""
        };

        public static void SetBlocked(bool blocked)
        {
            if (blocked)
                TryOccupy(ExternalControlOwner.ThirdPartyEms, out _);
            else
                Release(ExternalControlOwner.ThirdPartyEms);
        }

        public static bool TryOccupy(ExternalControlOwner owner, out string message)
        {
            if (owner == ExternalControlOwner.None)
            {
                Reset();
                message = "";
                return true;
            }

            var current = Owner;
            if (current == ExternalControlOwner.None || current == owner)
            {
                Volatile.Write(ref _owner, (int)owner);
                message = "";
                return true;
            }

            message = CurrentMessage;
            return false;
        }

        public static void Release(ExternalControlOwner owner)
        {
            if (Owner == owner)
                Reset();
        }

        public static void Reset() =>
            Volatile.Write(ref _owner, (int)ExternalControlOwner.None);

        public static bool TryAllow(out string message)
        {
            if (!IsBlocked)
            {
                message = "";
                return true;
            }

            message = CurrentMessage;
            return false;
        }
    }
}
