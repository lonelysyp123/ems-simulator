namespace EssSimulator.Configuration
{
    /// <summary>B/S 架构 Web 服务配置（对应 appsettings.json: Simulator.Web 节）。</summary>
    public class WebConfig
    {
        public const string Section = "Simulator:Web";

        /// <summary>HTTP 监听端口（默认 5050；macOS 上 5000 常被 AirPlay 占用）。</summary>
        public int HttpPort { get; set; } = 5050;

        /// <summary>HTTP 监听地址前缀（默认 0.0.0.0，全网卡）。</summary>
        public string HttpBaseUrl { get; set; } = "http://0.0.0.0";

        /// <summary>是否托管 wwwroot/ 静态前端文件。</summary>
        public bool StaticFiles { get; set; } = true;

        /// <summary>CORS 允许来源；空数组表示允许开发代理（dev 模式自动放行 Vite 默认端口）。</summary>
        public List<string> CorsOrigins { get; set; } = new();

        /// <summary>实时快照推送间隔（ms，主接线/BMS/连接）；控制变更可额外触发立即推送。</summary>
        public int SnapshotIntervalMs { get; set; } = 200;

        /// <summary>是否默认开启白盒切片采集（运行中仍可通过 API 开关）。</summary>
        public bool DroopSliceCaptureEnabled { get; set; }

        /// <summary>白盒切片内存环缓冲容量。</summary>
        public int DroopSliceMaxCount { get; set; } = 500;

        /// <summary>
        /// 是否启用 HTTP API Key 鉴权（保护 /api/*，/api/health 豁免）。
        /// 部署到对外可达环境时建议开启；密钥用环境变量注入，勿写入公开配置仓库。
        /// </summary>
        public bool ApiKeyEnabled { get; set; }

        /// <summary>API Key 明文；启用时必填。推荐环境变量 Simulator__Web__ApiKey。</summary>
        public string ApiKey { get; set; } = "";
    }
}
