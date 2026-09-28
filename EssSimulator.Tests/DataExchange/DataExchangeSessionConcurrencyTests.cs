using System.Collections.Concurrent;
using EssSimulator.Core;
using EssSimulator.DataExchange;
using EssSimulator.DataExchange.Catalog;
using EssSimulator.DataExchange.Config;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Tests.DataExchange;

public class DataExchangeSessionConcurrencyTests : SimulatorHostTestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Feedback_WaitsForControlSnapshotToFinish(bool immediate)
    {
        using var fixture = new SessionFixture();
        fixture.Slave.PauseNextControlRead = 1;
        Task? publication = null;
        try
        {
            fixture.Session.Start();
            Assert.True(fixture.Slave.ControlReadPaused.Wait(TimeSpan.FromSeconds(5)));
            fixture.Model.Value = 100;
            publication = fixture.PublishIfImmediate(immediate);

            Assert.False(fixture.Slave.FeedbackWritten.Wait(TimeSpan.FromMilliseconds(100)));
            fixture.Slave.ResumeControlRead.Set();
            Assert.True(fixture.Slave.FeedbackWritten.Wait(TimeSpan.FromSeconds(5)));
            if (publication != null)
                await publication.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(fixture.Slave.ControlReadFeedbackValue.Wait(TimeSpan.FromSeconds(5)));
            fixture.Session.Stop();

            Assert.Equal(100, fixture.Model.Value);
            Assert.Empty(fixture.Capture.Events);
        }
        finally
        {
            fixture.Slave.ResumeControlRead.Set();
            if (publication != null)
                await publication.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Control_WaitsForFeedbackWriteAndShadowCommit(bool immediate)
    {
        using var fixture = new SessionFixture();
        Task? publication = null;
        try
        {
            fixture.Session.Start();
            fixture.Slave.PauseNextFeedbackWrite = 1;
            fixture.Model.Value = 100;
            publication = fixture.PublishIfImmediate(immediate);
            Assert.True(fixture.Slave.FeedbackWritePaused.Wait(TimeSpan.FromSeconds(5)));

            Assert.False(fixture.Slave.ControlReadFeedbackValue.Wait(TimeSpan.FromMilliseconds(100)));
            fixture.Slave.ResumeFeedbackWrite.Set();
            if (publication != null)
                await publication.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(fixture.Slave.ControlReadFeedbackValue.Wait(TimeSpan.FromSeconds(5)));
            fixture.Session.Stop();

            Assert.Equal(100, fixture.Model.Value);
            Assert.Empty(fixture.Capture.Events);
        }
        finally
        {
            fixture.Slave.ResumeFeedbackWrite.Set();
            if (publication != null)
                await publication.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class CommandModel
    {
        private double _value = 80;
        public double Value
        {
            get => Volatile.Read(ref _value);
            set => Volatile.Write(ref _value, value);
        }
    }

    private sealed class Capture : IControlPointCapture
    {
        public ConcurrentQueue<object> Events { get; } = new();
        public void OnControlApplied(string serverName, PointBinding binding, object applied, object? previous) =>
            Events.Enqueue(applied);
    }

    private sealed class SessionFixture : IDisposable
    {
        private readonly IControlPointCapture _previousCapture = ControlPointCapture.Current;
        private readonly ManualResetEventSlim _publicationStarted = new();
        public CommandModel Model { get; } = new();
        public Capture Capture { get; } = new();
        public BlockingSlave Slave { get; }
        public DataExchangeSession Session { get; }

        public SessionFixture()
        {
            var entry = new MapEntry
            {
                FunctionCode = 6, Address = 5039, Type = "u16", Size = 16, ParamName = "yt0", Scale = 10
            };
            var binding = new PointBinding
            {
                Entry = entry,
                ParamName = "yt0",
                Target = new DataTarget { RootKey = "command", PropertyPath = "Value" },
                Semantics = ControlSemantics.Hold,
                Effect = ControlEffectId.None
            };
            var catalog = new PointCatalog
            {
                ServerName = "simPvInv1",
                ControlPoints = new[] { binding },
                TelemetryPoints = Array.Empty<PointBinding>(),
                DefaultValues = new Dictionary<string, object>()
            };
            SimulatorHost.Instance.Register("command", Model);
            ControlPointCapture.Current = Capture;
            Slave = new BlockingSlave(entry);
            Session = new DataExchangeSession(Slave, new ModbusParser(new List<MapEntry[]> { new[] { entry } }),
                catalog, new DeviceInfoDto { name = "simPvInv1", slaveId = 1 },
                new DataExchangeOptions { ControlPollIntervalMs = 20, TelemetryIntervalMs = 20 });
        }

        public Task? PublishIfImmediate(bool immediate)
        {
            if (!immediate)
                return null;
            var publication = Task.Run(() =>
            {
                _publicationStarted.Set();
                Session.PublishControlToSlave("yt0", 100);
            });
            _publicationStarted.Wait(TimeSpan.FromSeconds(5));
            return publication;
        }

        public void Dispose()
        {
            Slave.ResumeControlRead.Set();
            Slave.ResumeFeedbackWrite.Set();
            Session.Stop();
            ControlPointCapture.Current = _previousCapture;
            Slave.Dispose();
            _publicationStarted.Dispose();
        }
    }

    private sealed class BlockingSlave(MapEntry entry) : IModbusSlave, IDisposable
    {
        private byte[] _register = ModbusPointCodec.Encode(80, entry, applyScale: true);
        public int PauseNextControlRead;
        public int PauseNextFeedbackWrite;
        public ManualResetEventSlim ControlReadPaused { get; } = new();
        public ManualResetEventSlim ResumeControlRead { get; } = new();
        public ManualResetEventSlim FeedbackWritten { get; } = new();
        public ManualResetEventSlim FeedbackWritePaused { get; } = new();
        public ManualResetEventSlim ResumeFeedbackWrite { get; } = new();
        public ManualResetEventSlim ControlReadFeedbackValue { get; } = new();

        public void DeviceConnect() { }
        public void DeviceDisconnect() { }
        public bool GetCommunicatorState() => true;
        public byte[] Read(string paramName) => Volatile.Read(ref _register);

        public Dictionary<string, object>? Read(byte slaveId = 1)
        {
            var snapshot = Volatile.Read(ref _register);
            if (Convert.ToDouble(ModbusPointCodec.Decode(snapshot, entry)) == 100)
                ControlReadFeedbackValue.Set();
            if (Interlocked.Exchange(ref PauseNextControlRead, 0) == 1)
            {
                ControlReadPaused.Set();
                ResumeControlRead.Wait(TimeSpan.FromSeconds(5));
            }
            return new Dictionary<string, object> { ["yt0"] = snapshot };
        }

        public bool Write(Dictionary<string, object> data, byte slaveId = 1, bool applyScale = true)
        {
            if (!data.TryGetValue("yt0", out var value))
                return true;
            Volatile.Write(ref _register, ModbusPointCodec.Encode(value, entry, applyScale));
            if (Convert.ToDouble(value) == 100)
                FeedbackWritten.Set();
            if (Interlocked.Exchange(ref PauseNextFeedbackWrite, 0) == 1)
            {
                FeedbackWritePaused.Set();
                ResumeFeedbackWrite.Wait(TimeSpan.FromSeconds(5));
            }
            return true;
        }

        public void Dispose()
        {
            ControlReadPaused.Dispose();
            ResumeControlRead.Dispose();
            FeedbackWritten.Dispose();
            FeedbackWritePaused.Dispose();
            ResumeFeedbackWrite.Dispose();
            ControlReadFeedbackValue.Dispose();
        }
    }
}
