using System;
using System.Collections.Generic;
using log4net;

namespace EssSimulator.EssDeviceSimModel
{
    /// <summary>
    /// 外部控制写入的邮箱。主循环开启延迟后，同一路径只保留最后一次设定，
    /// 在 <see cref="PlantEngine.Step"/> 开头写入模型；主循环未启动时立即执行。
    /// </summary>
    public static class PlantControlMailbox
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(PlantControlMailbox));
        private static readonly object Gate = new();
        private static readonly Dictionary<string, Action> Pending = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Applying = new(StringComparer.OrdinalIgnoreCase);
        private static int _deferralEnabled;

        public static bool DeferralEnabled
        {
            get => Volatile.Read(ref _deferralEnabled) != 0;
            set => Volatile.Write(ref _deferralEnabled, value ? 1 : 0);
        }

        /// <summary>该路径已收下设定、尚未写入模型（含本步正在写入）。</summary>
        public static bool IsPending(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            lock (Gate)
                return Pending.ContainsKey(key) || Applying.Contains(key);
        }

        /// <summary>本步正在执行的写入已经被更新的设定替换。</summary>
        public static bool IsSuperseded(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            lock (Gate)
                return Pending.ContainsKey(key);
        }

        public static void Submit(string key, Action apply)
        {
            if (apply == null)
                throw new ArgumentNullException(nameof(apply));
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("控制路径不能为空", nameof(key));

            if (!DeferralEnabled)
            {
                apply();
                return;
            }

            lock (Gate)
            {
                if (Volatile.Read(ref _deferralEnabled) == 0)
                {
                    // 关延迟发生在入锁之前：出锁后立即执行，避免写进再也无人排空的队列。
                }
                else
                {
                    Pending[key] = apply;
                    return;
                }
            }

            apply();
        }

        public static void Flush()
        {
            KeyValuePair<string, Action>[] batch;
            lock (Gate)
            {
                if (Pending.Count == 0)
                    return;

                batch = new KeyValuePair<string, Action>[Pending.Count];
                int i = 0;
                foreach (var pair in Pending)
                {
                    batch[i++] = pair;
                    Applying.Add(pair.Key);
                }

                Pending.Clear();
            }

            try
            {
                foreach (var pair in batch)
                {
                    if (IsSuperseded(pair.Key))
                        continue;

                    try
                    {
                        pair.Value();
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"电站步进前应用控制失败: {pair.Key}", ex);
                    }
                }
            }
            finally
            {
                lock (Gate)
                {
                    foreach (var pair in batch)
                        Applying.Remove(pair.Key);
                }
            }
        }

        public static void Reset()
        {
            DeferralEnabled = false;
            lock (Gate)
            {
                Pending.Clear();
                Applying.Clear();
            }
        }
    }
}
