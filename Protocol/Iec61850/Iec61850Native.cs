using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using IEC61850.GOOSE.Subscriber;

namespace EssSimulator.Protocol.Iec61850
{
    /// <summary>
    /// 解析 libiec61850 原生库。C# 包装的 DllImport 名为 <c>iec61850</c>
    ///（macOS 找 <c>libiec61850.dylib</c>，Linux 找 <c>libiec61850.so</c>，Windows 找 <c>iec61850.dll</c>）。
    /// </summary>
    internal static class Iec61850Native
    {
        internal const string DllImportName = "iec61850";

        private static readonly object Gate = new();
        private static bool _registered;
        private static IntPtr _handle;
        private static bool? _hasGooseSubscriberDestroy;

        [ModuleInitializer]
        internal static void Init() => EnsureLoaded();

        internal static void EnsureLoaded()
        {
            lock (Gate)
            {
                if (_registered)
                    return;

                var assembly = typeof(IEC61850.Server.IedServer).Assembly;
                NativeLibrary.SetDllImportResolver(assembly, Resolve);
                _registered = true;
            }
        }

        /// <summary>按候选路径预加载，供启动日志判断 Windows 单文件包是否漏带原生库。</summary>
        internal static bool TryEnsureNativeLibrary(out string detail)
        {
            EnsureLoaded();
            var candidates = ListCandidatePaths();
            var existing = new List<string>();
            foreach (var path in candidates)
            {
                if (!File.Exists(path))
                    continue;
                existing.Add(path);
                if (NativeLibrary.TryLoad(path, out var handle))
                {
                    _handle = handle;
                    detail = path;
                    return true;
                }
            }

            string fileName = NativeFileName();
            detail = existing.Count > 0
                ? $"已找到但无法加载 {fileName}：{string.Join("; ", existing)}"
                : $"未找到 {fileName}。请确认它与 EssSimulator.exe 同目录（或 runtimes/win-x64/native/）。已搜索 {candidates.Count} 处。";
            return false;
        }

        internal static IReadOnlyList<string> ListCandidatePaths() => CandidatePaths().ToList();

        private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (!IsIec61850(libraryName))
                return IntPtr.Zero;

            foreach (var path in CandidatePaths())
            {
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle))
                {
                    _handle = handle;
                    return handle;
                }
            }

            return IntPtr.Zero;
        }

        /// <summary>当前加载的原生库是否导出 GOOSE 订阅析构。缺失时不可创建 GooseSubscriber，否则 GC 终结器会打崩进程。</summary>
        internal static bool HasGooseSubscriberDestroy()
        {
            lock (Gate)
            {
                if (_hasGooseSubscriberDestroy.HasValue)
                    return _hasGooseSubscriberDestroy.Value;

                if (!TryEnsureNativeLibrary(out _))
                {
                    _hasGooseSubscriberDestroy = false;
                    return false;
                }

                _hasGooseSubscriberDestroy = _handle != IntPtr.Zero
                    && NativeLibrary.TryGetExport(_handle, "GooseSubscriber_destroy", out _);
                return _hasGooseSubscriberDestroy.Value;
            }
        }

        /// <summary>
        /// 空 GoCbRef 时必须 setObserver，否则 goCBRefLen=0 永远匹配不上报文里的 GoCB。
        /// C# 包装未暴露此 API，直接调原生导出。
        /// </summary>
        internal static bool TrySetGooseSubscriberObserver(GooseSubscriber subscriber)
        {
            if (subscriber == null)
                return false;
            EnsureLoaded();
            var field = typeof(GooseSubscriber).GetField("self", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field?.GetValue(subscriber) is not IntPtr self || self == IntPtr.Zero)
                return false;
            if (_handle == IntPtr.Zero || !NativeLibrary.TryGetExport(_handle, "GooseSubscriber_setObserver", out var fn)
                || fn == IntPtr.Zero)
            {
                return false;
            }

            var setObserver = Marshal.GetDelegateForFunctionPointer<GooseSubscriberSetObserver>(fn);
            setObserver(self);
            return true;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void GooseSubscriberSetObserver(IntPtr self);

        private static bool IsIec61850(string libraryName) =>
            libraryName.Equals(DllImportName, StringComparison.OrdinalIgnoreCase)
            || libraryName.Equals("libiec61850", StringComparison.OrdinalIgnoreCase)
            || libraryName.Equals("libiec61850.dylib", StringComparison.OrdinalIgnoreCase)
            || libraryName.Equals("libiec61850.so", StringComparison.OrdinalIgnoreCase)
            || libraryName.Equals("iec61850.dll", StringComparison.OrdinalIgnoreCase);

        internal static string NativeFileName() =>
            OperatingSystem.IsWindows() ? "iec61850.dll"
            : OperatingSystem.IsLinux() ? "libiec61850.so"
            : "libiec61850.dylib";

        private static IEnumerable<string> CandidatePaths()
        {
            string fileName = NativeFileName();
            var rids = HostRids();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var root in SearchRoots())
            {
                foreach (var rid in rids)
                {
                    TryAdd(seen, Path.Combine(root, "runtimes", rid, "native", fileName));
                    TryAdd(seen, Path.Combine(root, "lib61850", "native", rid, fileName));
                }

                TryAdd(seen, Path.Combine(root, fileName));
            }

            return seen;
        }

        private static IEnumerable<string> SearchRoots()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var start in new[]
                     {
                         AppContext.BaseDirectory,
                         Directory.GetCurrentDirectory()
                     })
            {
                if (string.IsNullOrWhiteSpace(start))
                    continue;

                var dir = new DirectoryInfo(Path.GetFullPath(start));
                while (dir != null)
                {
                    if (seen.Add(dir.FullName))
                        yield return dir.FullName;
                    dir = dir.Parent;
                }
            }

            foreach (var extra in NativeDllSearchDirectories())
            {
                if (seen.Add(extra))
                    yield return extra;
            }
        }

        private static IEnumerable<string> NativeDllSearchDirectories()
        {
            if (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") is not string data
                || string.IsNullOrWhiteSpace(data))
            {
                yield break;
            }

            foreach (var part in data.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var dir = part.Trim();
                if (dir.Length == 0)
                    continue;
                yield return Path.GetFullPath(dir);
            }
        }

        private static IEnumerable<string> HostRids()
        {
            if (OperatingSystem.IsWindows())
            {
                yield return "win-x64";
                yield break;
            }

            if (OperatingSystem.IsMacOS())
            {
                if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
                    yield return "osx-arm64";
                else
                    yield return "osx-x64";
                yield return "osx-universal";
                yield return RuntimeInformation.RuntimeIdentifier;
                yield break;
            }

            if (OperatingSystem.IsLinux())
            {
                if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
                    yield return "linux-arm64";
                else
                    yield return "linux-x64";
                yield return RuntimeInformation.RuntimeIdentifier;
            }
        }

        private static void TryAdd(HashSet<string> seen, string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
                seen.Add(Path.GetFullPath(path));
        }
    }
}
