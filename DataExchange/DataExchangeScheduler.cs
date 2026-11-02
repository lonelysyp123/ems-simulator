using System.Diagnostics;
using log4net;

namespace EssSimulator.DataExchange
{
    /// <summary>
    /// 点表交互的共享调度器。
    ///
    /// 原来每个设备会话（<see cref="DataExchangeSession"/>）各起 3~4 条专用线程做
    /// 遥测 / 簇遥测 / 控制 / 反馈轮询：10MW 工程约 50 个会话就是 ~190 条线程，
    /// 300MW 测试工程约 300 个会话则接近 1000 条。这里改成**每条管道一条常驻线程**，
    /// 所有会话的同一管道在同一条线程上按各自周期轮流执行，线程数与会话数无关。
    ///
    /// 语义保持：每个会话仍按自己的间隔（遥测 100/500ms、控制 ~100ms）被驱动；
    /// 同一管道内顺序执行，跨管道仍并行（与原来多线程时的并发关系一致）。
    /// </summary>
    internal sealed class DataExchangeScheduler
    {
        public static readonly DataExchangeScheduler Instance = new();

        private static readonly ILog Log = LogManager.GetLogger(typeof(DataExchangeScheduler));

        internal enum Pipeline
        {
            Telemetry = 0,
            RackTelemetry = 1,
            Control = 2,
            Feedback = 3
        }

        private const int PipelineCount = 4;
        private const int MaxSleepMs = 50;
        private const int IdleSleepMs = 200;

        private sealed class Job
        {
            public required object Owner { get; init; }
            public required int IntervalMs { get; init; }
            public required Action Tick { get; init; }

            /// <summary>下一次到期时间（Stopwatch 时间戳）。由所属管道的线程独占写入。</summary>
            public long NextDue;
        }

        private readonly object _gate = new();
        private readonly List<Job>?[] _queues = new List<Job>?[PipelineCount];
        private readonly Job[][] _snapshots =
        {
            Array.Empty<Job>(), Array.Empty<Job>(), Array.Empty<Job>(), Array.Empty<Job>()
        };
        private readonly Thread?[] _threads = new Thread?[PipelineCount];

        /// <summary>当前存活的调度线程数（正常恒为 4）。</summary>
        internal int WorkerThreadCount
        {
            get
            {
                lock (_gate)
                    return _threads.Count(t => t is { IsAlive: true });
            }
        }

        internal int JobCount(Pipeline pipeline)
        {
            lock (_gate)
                return _queues[(int)pipeline]?.Count ?? 0;
        }

        /// <summary>注册（或替换）某个所有者在某条管道上的周期任务。</summary>
        public void Register(object owner, Pipeline pipeline, int intervalMs, Action tick)
        {
            ArgumentNullException.ThrowIfNull(owner);
            ArgumentNullException.ThrowIfNull(tick);

            lock (_gate)
            {
                int index = (int)pipeline;
                var list = _queues[index] ??= new List<Job>();

                // 同一会话重复 Start（link off→on）时替换旧任务，避免重复调度
                int existing = list.FindIndex(j => ReferenceEquals(j.Owner, owner));
                var job = new Job
                {
                    Owner = owner,
                    IntervalMs = Math.Max(10, intervalMs),
                    Tick = tick
                };

                if (existing >= 0)
                    list[existing] = job;
                else
                    list.Add(job);

                RebuildSnapshot(pipeline);
                EnsureThread(pipeline);
            }
        }

        /// <summary>移除该所有者的全部周期任务。</summary>
        public void Unregister(object owner)
        {
            lock (_gate)
            {
                for (int index = 0; index < PipelineCount; index++)
                {
                    var list = _queues[index];
                    if (list == null || list.RemoveAll(j => ReferenceEquals(j.Owner, owner)) == 0)
                        continue;

                    Volatile.Write(ref _snapshots[index], list.ToArray());
                }
            }
        }

        private void RebuildSnapshot(Pipeline pipeline)
        {
            int index = (int)pipeline;
            Volatile.Write(ref _snapshots[index], (_queues[index] ?? new List<Job>()).ToArray());
        }

        private void EnsureThread(Pipeline pipeline)
        {
            int index = (int)pipeline;
            if (_threads[index] is { IsAlive: true })
                return;

            var thread = new Thread(() => RunLoop(pipeline))
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = $"DataExchange-{pipeline}"
            };
            _threads[index] = thread;
            thread.Start();

            if (pipeline == Pipeline.Telemetry)
                Log.Info("[DataExchange] 共享调度线程已启动：遥测/簇遥测/控制/反馈各 1 条，会话数增长不再增加线程");
        }

        private void RunLoop(Pipeline pipeline)
        {
            int index = (int)pipeline;

            while (true)
            {
                var snapshot = Volatile.Read(ref _snapshots[index]);
                long now = Stopwatch.GetTimestamp();
                long nextDue = long.MaxValue;

                for (int i = 0; i < snapshot.Length; i++)
                {
                    var job = snapshot[i];
                    if (job.NextDue > now)
                    {
                        nextDue = Math.Min(nextDue, job.NextDue);
                        continue;
                    }

                    job.NextDue = now + MsToTimestamp(job.IntervalMs);
                    nextDue = Math.Min(nextDue, job.NextDue);

                    try
                    {
                        job.Tick();
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[DataExchange] {pipeline} 调度执行失败", ex);
                    }
                }

                Thread.Sleep(SleepMs(now, nextDue, snapshot.Length == 0));
            }
        }

        private static int SleepMs(long now, long nextDue, bool empty)
        {
            if (empty || nextDue == long.MaxValue)
                return IdleSleepMs;

            long remainingTicks = nextDue - now;
            if (remainingTicks <= 0)
                return 1;

            double ms = remainingTicks * 1000.0 / Stopwatch.Frequency;
            return (int)Math.Clamp(ms, 1, MaxSleepMs);
        }

        private static long MsToTimestamp(int ms) =>
            (long)(ms * (Stopwatch.Frequency / 1000.0));
    }
}
