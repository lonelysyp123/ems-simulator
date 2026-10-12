using EssSimulator.Protocol.Iec61850;

namespace EssSimulator.Tests.Protocol;

/// <summary>
/// 需要随仓 libiec61850 原生库的用例。Linux 当前不随仓 .so，缺少库时跳过而不是 DllNotFound。
/// </summary>
public sealed class NativeLibraryFactAttribute : FactAttribute
{
    public NativeLibraryFactAttribute()
    {
        if (!Iec61850Native.TryEnsureNativeLibrary(out var detail))
            Skip = detail;
    }
}
