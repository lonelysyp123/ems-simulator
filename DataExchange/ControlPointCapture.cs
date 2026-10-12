using EssSimulator.DataExchange.Catalog;

namespace EssSimulator.DataExchange
{
    /// <summary>控制点成功写入仿真后的旁路观察（如白盒切片）。默认 no-op。</summary>
    public interface IControlPointCapture
    {
        void OnControlApplied(string serverName, PointBinding binding, object applied, object? previous);
    }

    public static class ControlPointCapture
    {
        private static IControlPointCapture _current = NoOpControlPointCapture.Instance;

        public static IControlPointCapture Current
        {
            get => _current;
            set => _current = value ?? NoOpControlPointCapture.Instance;
        }

        public static void Reset() => _current = NoOpControlPointCapture.Instance;
    }

    public sealed class NoOpControlPointCapture : IControlPointCapture
    {
        public static readonly NoOpControlPointCapture Instance = new();

        public void OnControlApplied(string serverName, PointBinding binding, object applied, object? previous)
        {
        }
    }
}
