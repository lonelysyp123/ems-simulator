namespace EssSimulator.Core
{
    /// <summary>控制侧变更后请求立即推一帧 UI 快照。Web 层实现；无宿主时为 no-op。</summary>
    public interface IUiSnapshotNotifier
    {
        void RequestImmediatePush();
    }

    public static class UiSnapshotNotifier
    {
        private static IUiSnapshotNotifier _current = NoOpUiSnapshotNotifier.Instance;

        public static IUiSnapshotNotifier Current
        {
            get => _current;
            set => _current = value ?? NoOpUiSnapshotNotifier.Instance;
        }

        public static void RequestImmediatePush() => _current.RequestImmediatePush();

        public static void Reset() => _current = NoOpUiSnapshotNotifier.Instance;
    }

    public sealed class NoOpUiSnapshotNotifier : IUiSnapshotNotifier
    {
        public static readonly NoOpUiSnapshotNotifier Instance = new();
        public void RequestImmediatePush() { }
    }
}
