using EssSimulator.Configuration;
using EssSimulator.Core;
using EssSimulator.DataExchange.Config;
using EssSimulator.Display;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssSimModelApi;
using EssSimulator.Licensing;
using EssSimulator.LocalControl;
using EssSimulator.Web;
using EssSimulator.Web.Topology;
using log4net;
using log4net.Appender;
using log4net.Config;
using log4net.Repository.Hierarchy;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace EssSimulator
{
    public class Program
    {
        private static Stream? LoadJsonWithCommentsAsStream(string path, bool optional)
        {
            if (!File.Exists(path))
            {
                if (optional) return null;
                throw new FileNotFoundException($"未找到配置文件: {path}", path);
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            var docOptions = new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            using var doc = JsonDocument.Parse(json, docOptions);
            var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                doc.RootElement.WriteTo(writer);
            }
            ms.Position = 0;
            return ms;
        }

        private static bool IsSimulatorReady(int expectedServerCount, bool expectLocalControl, SimulatorConfig cfg)
        {
            var store = SimulatorHost.Instance;
            if (!store.Contains("ess") ||
                !store.Contains("em") ||
                !store.Contains("simEm"))
            {
                return false;
            }

            if (cfg.EffectiveEssUnitCount > 0 &&
                (!store.Contains("bms1") ||
                 !store.Contains("emu1") ||
                 !store.Contains("simBms1") ||
                 !store.Contains("simEmu1")))
            {
                return false;
            }

            if (cfg.PvUnitCount > 0 &&
                (!store.Contains("pv1") || !store.Contains("simPv1")))
            {
                return false;
            }

            if (expectLocalControl && !store.Contains("simLc1"))
                return false;

            if (SimServer.serverListenInfo.Count < expectedServerCount)
                return false;

            try
            {
                var totalActivePower = SimServer.GetExtIfVariableVal("em.TotalActivePower");
                if (totalActivePower == null)
                    return false;

                if (cfg.EffectiveEssUnitCount > 0)
                {
                    var soc = SimServer.GetExtIfVariableVal("bms1.BatteryStacks[0].SOC");
                    var pcsList = SimServer.GetExtIfVariableVal("ess._pcsList") as System.Collections.ICollection;
                    if (soc == null || pcsList == null || pcsList.Count == 0)
                        return false;
                }

                if (cfg.PvUnitCount > 0)
                {
                    var pvPower = SimServer.GetExtIfVariableVal("pv1.ActivePowerKw");
                    if (pvPower == null)
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async Task WaitForSimulatorReadyAsync(SimulatorConfig cfg, CancellationToken cancellationToken)
        {
            int expectedBmsCount = cfg.UnitCount;
            int expectedEmuCount = EmuProtocolLayout.Count(cfg);
            int expectedPvCount = cfg.PvUnitCount * 2; // Logger + 电表
            int expectedLcCount = 0;
            if (cfg.Protocol.EnableLocalControl && cfg.EffectiveEssUnitCount > 0)
                expectedLcCount = cfg.EffectiveEssUnitCount;
            int expectedServerCount = expectedBmsCount + expectedEmuCount + expectedPvCount + expectedLcCount + 1;
            var timeout = TimeSpan.FromSeconds(60);
            var start = DateTime.UtcNow;

            while ((DateTime.UtcNow - start) < timeout)
            {
                if (IsSimulatorReady(expectedServerCount, cfg.Protocol.EnableLocalControl, cfg))
                {
                    LogManager.GetLogger(typeof(Program)).Info("[Program] 仿真器已就绪");
                    return;
                }
                try { await Task.Delay(500, cancellationToken); }
                catch (TaskCanceledException) { return; }
            }

            LogManager.GetLogger(typeof(Program)).Warn("初始化等待超时，仍将启动 Web 服务。若 dpc 初始读取异常，请稍后重试。");
        }

        private static bool EnforceLicenseOrExit(IConfiguration configuration)
        {
            var edition = configuration.GetSection(EditionConfig.Section).Get<EditionConfig>() ?? new EditionConfig();
            edition.ApplyPresets();
            var lic = configuration.GetSection(LicenseConfig.Section).Get<LicenseConfig>() ?? new LicenseConfig();
            bool hasExplicit = configuration.GetSection(LicenseConfig.Section)
                .GetSection(nameof(LicenseConfig.Required)).Exists();
            bool required = hasExplicit ? lic.Required : !edition.IsCommunity;
            if (!required)
                return true;

            string fileName = string.IsNullOrWhiteSpace(lic.FileName) ? "license.txt" : lic.FileName.Trim();
            string cwdPath = Path.Combine(Directory.GetCurrentDirectory(), fileName);
            string basePath = Path.Combine(AppContext.BaseDirectory, fileName);
            string path = File.Exists(cwdPath) ? cwdPath : basePath;

            var result = LicenseGuard.ValidateFile(path);
            if (result.IsValid)
            {
                LogManager.GetLogger(typeof(Program)).Info($"[License] {result.Message}");
                Console.WriteLine($"[License] {result.Message}");
                return true;
            }

            Console.Error.WriteLine();
            Console.Error.WriteLine("======== 授权校验失败，无法启动 ========");
            Console.Error.WriteLine(result.Message);
            Console.Error.WriteLine($"本机机器码: {result.LocalMachineId}");
            Console.Error.WriteLine();
            Console.Error.WriteLine("请按下列步骤获取授权：");
            Console.Error.WriteLine("  1) 运行: ./EssSimulator --machine-id   （或 scripts/license/get-machine-id.sh）");
            Console.Error.WriteLine("  2) 将机器码发给软件提供方，获取 license.txt");
            Console.Error.WriteLine($"  3) 将 license.txt 放到程序运行目录后重新启动");
            Console.Error.WriteLine("======================================");
            Console.Error.WriteLine();
            Environment.ExitCode = 2;
            return false;
        }

        private static void LogProtocolCreateInfo(SimulatorConfig cfg)
        {
            var log = LogManager.GetLogger(typeof(Program));
            log.Info("==== 协议模拟器创建 ====");
            // 端口计划：appsettings 默认值 + protocol-ports.json 手动覆盖
            var plan = EssSimulator.Protocol.Modbus.ProtocolPortPlan.Load(cfg, out var overridesError);
            if (overridesError != null)
                log.Warn(overridesError);
            foreach (var entry in plan.Entries)
            {
                string overrideMark = entry.IsDefault ? string.Empty : "（手动覆盖）";
                string rackMark = entry.RackCount > 0 ? $"，簇从站号 {entry.SlaveId + 1}-{entry.SlaveId + entry.RackCount}" : string.Empty;
                log.Info($"{entry.Name,-12}: Modbus TCP 端口 {entry.Port}，从站号 {entry.SlaveId}{rackMark}{overrideMark}");
            }

            var bindings = EssSimulator.Protocol.Iec61850.ProtocolBindings.Load();
            var emuEndpoints = EmuProtocolLayout.Enumerate(cfg);
            for (int i = 0; i < emuEndpoints.Count; i++)
            {
                var ep = emuEndpoints[i];
                if (!bindings.Allows(ep.ServerName, EssSimulator.Protocol.Iec61850.Iec61850Protocols.Iec61850))
                    continue;
                int port = bindings.PortOverride(ep.ServerName)
                           ?? EssSimulator.Protocol.Iec61850.ProtocolBindings.DefaultIec61850Port(
                               ep.SimIndex1Based, cfg.Protocol.BaseEmuIec61850Port, cfg.Protocol.EmuIec61850PortStep);
                string ied = EssSimulator.Protocol.Iec61850.Iec61850Mapping.IedNameFor(ep.ServerName);
                log.Info($"{ep.ServerName,-12}: IEC 61850 MMS {ied} 端口 {port}");
            }
            log.Info("=======================");
        }

        static async Task Main(string[] args)
        {
            // 仅打印机器码（供用户申请授权，无需完整启动）
            if (args.Any(a => string.Equals(a, "--machine-id", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(a, "machine-id", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine(MachineIdProvider.GetMachineId());
                return;
            }

            // 配置 log4net
            try
            {
                var logCfgPath = Path.Combine(AppContext.BaseDirectory, "log4net.config");
                if (File.Exists(logCfgPath))
                    XmlConfigurator.Configure(new FileInfo(logCfgPath));
                else if (File.Exists("log4net.config"))
                    XmlConfigurator.Configure(new FileInfo("log4net.config"));
            }
            catch (Exception ex)
            {
                BasicConfigurator.Configure();
                Console.WriteLine($"[log4net] 配置加载失败：{ex.Message}，已启用基础配置");
            }
            LogManager.GetLogger(typeof(Program)).Info("[Program] 应用启动（B/S 架构）");

            // 编程式注册 LogHubAppender：把日志推送到 SignalR（无需修改 log4net.config）
            try
            {
                var repo = LogManager.GetRepository() as Hierarchy;
                if (repo != null)
                {
                    var hubAppender = new LogHubAppender { Name = "LogHubAppender", Threshold = log4net.Core.Level.Info };
                    hubAppender.ActivateOptions();
                    repo.Root.AddAppender(hubAppender);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[log4net] 注册 LogHubAppender 失败：{ex.Message}");
            }

            var builder = WebApplication.CreateBuilder(args);

            // 替换默认配置加载为支持 JSON 注释/尾随逗号的版本
            builder.Configuration.Sources.Clear();
            string baseDir = Directory.GetCurrentDirectory();
            string envName = builder.Environment.EnvironmentName;
            var baseStream = LoadJsonWithCommentsAsStream(Path.Combine(baseDir, "appsettings.json"), optional: false);
            if (baseStream != null) builder.Configuration.AddJsonStream(baseStream);
            var envStream = LoadJsonWithCommentsAsStream(Path.Combine(baseDir, $"appsettings.{envName}.json"), optional: true);
            if (envStream != null) builder.Configuration.AddJsonStream(envStream);
            builder.Configuration.AddEnvironmentVariables();
            if (args is { Length: > 0 }) builder.Configuration.AddCommandLine(args);

            // 授权校验（社区版默认不要求；商业版/定制版要求运行目录或程序目录存在 license.txt）
            if (!EnforceLicenseOrExit(builder.Configuration))
                return;

            // 组态工程模式：若已应用 overlay，则覆盖 EssUnits / Pcc / 变压器等
            var topologyOverlay = TopologyOverlayLoader.TryLoad(builder.Environment.ContentRootPath);
            var editionEarly = builder.Configuration.GetSection(EditionConfig.Section).Get<EditionConfig>()
                ?? new EditionConfig();
            editionEarly.ApplyPresets();
            if (topologyOverlay != null && !editionEarly.AllowTopologyEditor)
            {
                Console.WriteLine("[Topology] 当前档位未开放组态编辑，已忽略工程 overlay，改用 appsettings");
                topologyOverlay = null;
            }
            if (topologyOverlay != null)
            {
                Console.WriteLine(
                    $"[Topology] 工程模式已启用：{topologyOverlay.SourceProjectName}（储能 {topologyOverlay.EssUnits.Count} / 光伏 {topologyOverlay.PvUnits.Count}）");
            }

            // 绑定配置节
            builder.Services.Configure<SimulatorConfig>(builder.Configuration.GetSection(SimulatorConfig.Section));
            builder.Services.Configure<EditionConfig>(builder.Configuration.GetSection(EditionConfig.Section));
            builder.Services.Configure<LicenseConfig>(builder.Configuration.GetSection(LicenseConfig.Section));
            builder.Services.PostConfigure<EditionConfig>(edition => edition.ApplyPresets());
            builder.Services.PostConfigure<LicenseConfig>(lic =>
            {
                var edition = builder.Configuration.GetSection(EditionConfig.Section).Get<EditionConfig>()
                    ?? new EditionConfig();
                edition.ApplyPresets();
                // 未显式配置 Required 时：社区版免授权，其它档位默认需要
                bool hasExplicit = builder.Configuration.GetSection(LicenseConfig.Section)
                    .GetSection(nameof(LicenseConfig.Required)).Exists();
                if (!hasExplicit)
                    lic.Required = !edition.IsCommunity;
            });
            builder.Services.PostConfigure<SimulatorConfig>(opt =>
            {
                var units = builder.Configuration.GetSection(EssUnitsConfig.Section).Get<List<EssUnitConfig>>();
                if (units is { Count: > 0 }) opt.Devices = units;

                if (topologyOverlay != null)
                {
                    if (topologyOverlay.EssUnits is { Count: > 0 })
                        opt.Devices = topologyOverlay.EssUnits;
                    else if (topologyOverlay.PvUnits is { Count: > 0 })
                        opt.Devices = new List<EssUnitConfig>();

                    if (topologyOverlay.PvUnits != null)
                        opt.PvUnits = topologyOverlay.PvUnits;
                }

                var edition = builder.Configuration.GetSection(EditionConfig.Section).Get<EditionConfig>()
                    ?? new EditionConfig();
                edition.ApplyPresets();
                // 仅社区版（LockTopology）裁剪单元；商业版不改拓扑，只靠 API 功能开关区分高级能力
                if (edition.LockTopology && edition.MaxEssUnits > 0 && opt.Devices is { Count: > 0 }
                    && opt.Devices.Count > edition.MaxEssUnits)
                {
                    int before = opt.Devices.Count;
                    opt.Devices = opt.Devices.Take(edition.MaxEssUnits).ToList();
                    Console.WriteLine(
                        $"[Edition] {edition.Name}: EssUnits 已从 {before} 裁剪为 {opt.Devices.Count}（MaxEssUnits={edition.MaxEssUnits}）");
                }
            });
            builder.Services.PostConfigure<WebConfig>(web =>
            {
                var edition = builder.Configuration.GetSection(EditionConfig.Section).Get<EditionConfig>()
                    ?? new EditionConfig();
                edition.ApplyPresets();
                if (!edition.AllowDroopSlices)
                    web.DroopSliceCaptureEnabled = false;
            });
            builder.Services.Configure<PcsPhysicalConfig>(builder.Configuration.GetSection(PcsPhysicalConfig.Section));
            builder.Services.Configure<TransformerConfig>(builder.Configuration.GetSection(TransformerConfig.Section));
            builder.Services.Configure<UnitTransformerConfig>(builder.Configuration.GetSection(UnitTransformerConfig.Section));
            builder.Services.Configure<LoadConfig>(builder.Configuration.GetSection(LoadConfig.Section));
            builder.Services.Configure<PccConfig>(builder.Configuration.GetSection(PccConfig.Section));
            builder.Services.Configure<EssSimulator.EssDeviceSimModel.Model.MeterConfig>(
                builder.Configuration.GetSection(EssSimulator.EssDeviceSimModel.Model.MeterConfig.Section));
            builder.Services.Configure<DataExchangeOptions>(builder.Configuration.GetSection(DataExchangeOptions.Section));
            builder.Services.Configure<WebConfig>(builder.Configuration.GetSection(WebConfig.Section));

            if (topologyOverlay != null)
            {
                if (topologyOverlay.Pcs != null)
                    builder.Services.PostConfigure<PcsPhysicalConfig>(pcs => CopyPcs(topologyOverlay.Pcs, pcs));
                if (topologyOverlay.Transformer != null)
                    builder.Services.PostConfigure<TransformerConfig>(t => CopyTransformer(topologyOverlay.Transformer, t));
                if (topologyOverlay.UnitTransformer != null)
                    builder.Services.PostConfigure<UnitTransformerConfig>(t => CopyUnitTransformer(topologyOverlay.UnitTransformer, t));
                if (topologyOverlay.Pcc != null)
                    builder.Services.PostConfigure<PccConfig>(p => CopyPcc(topologyOverlay.Pcc, p));
                if (topologyOverlay.Meter != null)
                    builder.Services.PostConfigure<EssSimulator.EssDeviceSimModel.Model.MeterConfig>(m =>
                    {
                        m.PccMeter = topologyOverlay.Meter.PccMeter;
                    });
                if (topologyOverlay.Load != null)
                {
                    builder.Services.PostConfigure<LoadConfig>(load =>
                    {
                        // 组态负载：有功仅消耗（≤0）
                        load.ActivePowerPlan = Math.Min(0, topologyOverlay.Load.ActivePowerPlan);
                        load.ReactivePowerPlan = topologyOverlay.Load.ReactivePowerPlan;
                    });
                }
            }

            // 配置 Kestrel 监听端口
            var webCfgEarly = builder.Configuration.GetSection(WebConfig.Section).Get<WebConfig>() ?? new WebConfig();
            string url = $"{webCfgEarly.HttpBaseUrl.Replace("0.0.0.0", "*")}:{webCfgEarly.HttpPort}";
            builder.WebHost.UseUrls(url);

            // CORS：允许前端 dev 代理与配置的来源
            var corsOrigins = webCfgEarly.CorsOrigins ?? new List<string>();
            // dev 模式默认放行 Vite 默认端口
            var devOrigins = new[] { "http://localhost:5173", "http://127.0.0.1:5173", "http://localhost:5050", "http://localhost:5000" };
            var allOrigins = corsOrigins.Concat(devOrigins).Distinct().ToArray();
            builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
            {
                p.WithOrigins(allOrigins)
                 .AllowAnyHeader()
                 .AllowAnyMethod()
                 .AllowCredentials();
            }));

            // SignalR
            builder.Services.AddSignalR(o => o.MaximumReceiveMessageSize = 64 * 1024);

            // JSON 序列化：保留枚举名/数值，camelCase 由 System.Text.Json 默认处理
            builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o =>
            {
                o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                o.SerializerOptions.IncludeFields = true;
            });

            // 核心仿真模型（单例 + 托管服务）
            builder.Services.AddSingleton<EnergyStorageSystem>(sp =>
            {
                var simCfg   = sp.GetRequiredService<IOptions<SimulatorConfig>>().Value;
                var pcsCfg   = sp.GetRequiredService<IOptions<PcsPhysicalConfig>>().Value;
                var transCfg = sp.GetRequiredService<IOptions<TransformerConfig>>().Value;
                var unitTransCfg = sp.GetRequiredService<IOptions<UnitTransformerConfig>>().Value;
                var loadCfg  = sp.GetRequiredService<IOptions<LoadConfig>>().Value;
                var pccCfg   = sp.GetRequiredService<IOptions<PccConfig>>().Value;
                var meterCfg = sp.GetRequiredService<IOptions<EssSimulator.EssDeviceSimModel.Model.MeterConfig>>().Value;
                var ess = new EnergyStorageSystem(simCfg, pcsCfg, transCfg, unitTransCfg, loadCfg, pccCfg, meterCfg);
                SimulatorHost.Instance.Register("ess", ess);
                for (int i = 0; i < ess.PvUnits.Count; i++)
                    SimulatorHost.Instance.Register($"pv{i + 1}", ess.PvUnits[i]);
                return ess;
            });
            builder.Services.AddHostedService(sp => sp.GetRequiredService<EnergyStorageSystem>());

            builder.Services.AddSingleton<BmsDataService>(sp =>
            {
                var cfg = sp.GetRequiredService<IOptions<SimulatorConfig>>().Value;
                return new BmsDataService(cfg);
            });

            builder.Services.AddHostedService<BmsLinkService>();

            builder.Services.AddSingleton<PcsDataServer>();
            builder.Services.AddSingleton<EmDataService>();
            builder.Services.AddSingleton<ProtocolProjectionService>();
            builder.Services.AddSingleton<EssSimulator.EmsStrategy.Adapter.EmsStrategyRuntime>();
            builder.Services.AddSingleton<EssSimulator.EmsStrategy.Adapter.EmsStrategyPlantAdapter>();

            bool enableLocalControl = builder.Configuration.GetSection(SimulatorConfig.Section)
                .GetSection(nameof(SimulatorConfig.Protocol))
                .GetValue<bool>(nameof(ProtocolConfig.EnableLocalControl));
            if (enableLocalControl)
            {
                builder.Services.AddHostedService<LocalControlHostedService>();
            }

            builder.Services.AddHostedService<ModbusHostedService>();
            builder.Services.AddHostedService<EssSimulator.Protocol.Iec61850.Iec61850HostedService>();
            builder.Services.AddHostedService<EssSimulator.Protocol.Ptp.PtpHostedService>();

            // Web 层服务
            builder.Services.AddSingleton<WebCommandExecutor>();
            builder.Services.AddSingleton<EssSimulator.Web.ThirdPartyEms.ThirdPartyEmsSession>();
            builder.Services.AddSingleton<EssSimulator.Web.DroopSlices.DroopSliceStore>();
            builder.Services.AddSingleton<EssSimulator.Web.Topology.TopologyStore>();
            builder.Services.AddHostedService<SnapshotService>();
            builder.Services.AddHostedService<LogHubDispatcher>();

            var app = builder.Build();

            // 确保切片 Store 在控制管道写入前完成静态挂载
            _ = app.Services.GetRequiredService<EssSimulator.Web.DroopSlices.DroopSliceStore>();
            // 协议镜像投影挂到 PlantEngine.Step 末尾（不再各自 100ms 循环）
            var proto = app.Services.GetRequiredService<ProtocolProjectionService>();
            var emsAdapter = app.Services.GetRequiredService<EssSimulator.EmsStrategy.Adapter.EmsStrategyPlantAdapter>();
            AfterPlantStep.Current = new CompositeAfterPlantStep(emsAdapter, proto);

            var simCfg = app.Services.GetRequiredService<IOptions<SimulatorConfig>>().Value;
            BlackStartSafety.Register(app.Services.GetRequiredService<IHostApplicationLifetime>());

            // 订阅 FatalSystemAlert 事件，通过 SignalR 推送前端
            var hub = app.Services.GetRequiredService<IHubContext<RealtimeHub>>();
            FatalSystemAlert.AlertTriggered += async (e) =>
            {
                try
                {
                    await hub.Clients.All.SendAsync(RealtimeMethods.ReceiveAlert, new
                    {
                        isActive = true,
                        message = e.Message,
                        detail = e.Detail,
                        secondsUntilExit = (int)e.ExitDelay.TotalSeconds
                    });
                }
                catch { /* 推送失败忽略 */ }
            };

            EssSimulator.Protocol.Iec61850.Iec61850TrafficLog.MessageAppended += msg =>
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await hub.Clients.Group(RealtimeChannels.Iec61850)
                            .SendAsync(RealtimeMethods.ReceiveIec61850Message, new
                            {
                                msg.Id,
                                utc = msg.Utc.ToString("o"),
                                localTime = msg.Utc.ToLocalTime().ToString("HH:mm:ss.fff"),
                                msg.Direction,
                                msg.Protocol,
                                service = msg.Service,
                                server = msg.ServerName,
                                iedName = msg.IedName,
                                clientPeer = msg.ClientPeer,
                                paramName = msg.ParamName,
                                objectRef = msg.ObjectRef,
                                orCat = msg.OrCat,
                                ctlNum = msg.CtlNum,
                                appId = msg.AppId,
                                goCbRef = msg.GoCbRef,
                                goId = msg.GoId,
                                datSet = msg.DatSet,
                                stNum = msg.StNum,
                                sqNum = msg.SqNum,
                                isTest = msg.IsTest,
                                simulation = msg.IsTest,
                                ndsCom = msg.NeedsCommission,
                                confRev = msg.ConfRev,
                                timeAllowedToLive = msg.TimeAllowedToLive,
                                t = msg.GooseTimestampUtc,
                                numDatSetEntries = msg.NumDatSetEntries,
                                msg.Result,
                                msg.Summary,
                                writes = msg.Writes,
                                values = msg.Values,
                                allData = msg.AllData?.Select(e => new
                                {
                                    e.Index,
                                    paramName = e.ParamName,
                                    type = e.Type,
                                    value = e.Value,
                                    description = e.Description
                                })
                            });
                    }
                    catch { /* ignore */ }
                });
            };

            // 中间件
            app.UseCors();
            app.UseMiddleware<EssSimulator.Web.ApiKeyAuthMiddleware>();
            if (webCfgEarly.StaticFiles)
            {
                app.UseDefaultFiles();
                app.UseStaticFiles();
            }

            // 端点
            app.MapHub<RealtimeHub>("/hub/realtime");
            app.MapSimulatorEndpoints();

            // SPA 回退：非 /api 与 /hub 开头的请求回退到 index.html
            app.MapFallback(context =>
            {
                if (context.Request.Path.StartsWithSegments("/api") ||
                    context.Request.Path.StartsWithSegments("/hub"))
                {
                    context.Response.StatusCode = 404;
                    return Task.CompletedTask;
                }
                var indexPath = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html");
                if (!File.Exists(indexPath))
                {
                    context.Response.StatusCode = 404;
                    return Task.CompletedTask;
                }
                context.Response.ContentType = "text/html; charset=utf-8";
                return context.Response.SendFileAsync(indexPath);
            });

            // 启动
            await app.StartAsync();
            LogManager.GetLogger(typeof(Program)).Info($"[Program] Web 服务已启动：{url}（前端访问根路径）");
            LogProtocolCreateInfo(simCfg);

            _ = WaitForSimulatorReadyAsync(simCfg, app.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);

            await app.WaitForShutdownAsync();
        }

        private static void CopyPcs(PcsPhysicalConfig src, PcsPhysicalConfig dst)
        {
            dst.RatedPower = src.RatedPower;
            dst.MaxPower = src.MaxPower;
            dst.Efficiency = src.Efficiency;
            dst.DcVoltageRangeMin = src.DcVoltageRangeMin;
            dst.DcVoltageRangeMax = src.DcVoltageRangeMax;
            dst.AcVoltageNominal = src.AcVoltageNominal;
        }

        private static void CopyTransformer(TransformerConfig src, TransformerConfig dst)
        {
            dst.Present = src.Present;
            dst.RatedPower = src.RatedPower;
            dst.PrimaryVoltage = src.PrimaryVoltage;
            dst.SecondaryVoltage = src.SecondaryVoltage;
            dst.NoLoadLoss = src.NoLoadLoss;
            dst.LoadLoss = src.LoadLoss;
            dst.ImpedancePercent = src.ImpedancePercent;
        }

        private static void CopyUnitTransformer(UnitTransformerConfig src, UnitTransformerConfig dst)
        {
            dst.RatedPower = src.RatedPower;
            dst.PrimaryVoltage = src.PrimaryVoltage;
            dst.SecondaryVoltage = src.SecondaryVoltage;
        }

        private static void CopyPcc(PccConfig src, PccConfig dst)
        {
            dst.NominalLineVoltage = src.NominalLineVoltage;
            dst.ShortCircuitMva = src.ShortCircuitMva;
            dst.StationBusNominalLineVoltage = src.StationBusNominalLineVoltage;
        }
    }
}
