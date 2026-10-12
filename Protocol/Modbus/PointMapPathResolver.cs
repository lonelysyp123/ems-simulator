using System;
using System.IO;

namespace EssSimulator.Protocol.Modbus
{
    /// <summary>
    /// 解析运行时点表 CSV 路径：优先按设备型号选型（pointmaps/models/{type}/{model}），
    /// 未选型时回退到 <c>pointmaps/models/{类型}/standard/</c>。
    /// 不读取工作目录或输出目录根下的同名 CSV。
    /// </summary>
    public static class PointMapPathResolver
    {
        public static string Resolve(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("点表文件名不能为空", nameof(fileName));

            if (Path.IsPathRooted(fileName) && File.Exists(fileName))
                return fileName;

            var selected = ResolveSelected(fileName);
            if (selected != null)
                return selected;

            string name = Path.GetFileName(fileName);
            foreach (var root in DeviceModelRegistry.CandidateRoots())
            {
                var standard = ResolveStandardModel(root, fileName);
                if (standard != null) return standard;
            }

            throw new FileNotFoundException(
                $"找不到点表文件 `{name}`。请确认 `pointmaps/models/` 下存在对应设备类型的点表，并检查 `configs/topology/device-models.json` 选型。",
                name);
        }

        /// <summary>按设备型号选型解析点表；未选型、类型未声明该文件或型号目录缺文件时返回 null。</summary>
        private static string? ResolveSelected(string fileName)
        {
            var typeId = DeviceModelRegistry.FindTypeForFile(fileName);
            if (typeId == null) return null;

            var dir = DeviceModelRegistry.GetSelectedModelDir(typeId, fileName);
            if (dir == null) return null;

            var candidate = Path.Combine(dir, Path.GetFileName(fileName));
            return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
        }

        /// <summary>兜底解析：返回文件所属设备类型的 standard 型号点表路径，无则返回 null。</summary>
        private static string? ResolveStandardModel(string root, string fileName)
        {
            var typeId = DeviceModelRegistry.FindTypeForFile(fileName);
            if (typeId == null) return null;

            var candidate = Path.Combine(root, "pointmaps", "models", typeId, "standard", Path.GetFileName(fileName));
            return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
        }

        /// <summary>相对某已解析点表，查找同目录或回退 Resolve 的伴随文件（如 bms_rack.csv）。</summary>
        public static string ResolveSibling(string alreadyResolvedMapPath, string siblingFileName)
        {
            if (!string.IsNullOrWhiteSpace(alreadyResolvedMapPath))
            {
                var dir = Path.GetDirectoryName(alreadyResolvedMapPath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    var sibling = Path.Combine(dir, Path.GetFileName(siblingFileName));
                    if (File.Exists(sibling)) return sibling;
                }
            }
            return Resolve(siblingFileName);
        }
    }
}
