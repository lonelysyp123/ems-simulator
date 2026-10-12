using EssSimulator.Display;
using Microsoft.AspNetCore.SignalR;

namespace EssSimulator.Web
{
    /// <summary>
    /// 实时推送 Hub：前端连接后通过 Receive* 方法接收主接线/BMS/单体/连接/日志/告警/命令进度推送。
    /// 后端通过 IHubContext&lt;RealtimeHub&gt; 主动推送。
    /// </summary>
    public sealed class RealtimeHub : Hub
    {
        // 按频道分组订阅（前端可选择性加入，减少无关流量）
        public Task JoinChannel(string channel) => Groups.AddToGroupAsync(Context.ConnectionId, channel);
        public Task LeaveChannel(string channel) => Groups.RemoveFromGroupAsync(Context.ConnectionId, channel);

        public override async Task OnConnectedAsync()
        {
            // 默认推一次全量告警状态，便于前端初始化
            await Clients.Caller.SendAsync("ReceiveAlert", FatalSystemAlert.GetSnapshot());
            await base.OnConnectedAsync();
        }
    }

    /// <summary>实时推送频道名常量。</summary>
    public static class RealtimeChannels
    {
        public const string MainLine = "mainline";
        public const string Battery = "battery";
        public const string Cells = "cells";
        public const string Connections = "connections";
        public const string Logs = "logs";
        public const string Alert = "alert";
        public const string CommandProgress = "cmdprogress";
        public const string Iec61850 = "iec61850";
    }

    /// <summary>SignalR 推送方法名常量。</summary>
    public static class RealtimeMethods
    {
        public const string ReceiveMainLine = "ReceiveMainLine";
        public const string ReceiveBattery = "ReceiveBattery";
        public const string ReceiveCells = "ReceiveCells";
        public const string ReceiveConnections = "ReceiveConnections";
        public const string ReceiveLog = "ReceiveLog";
        public const string ReceiveAlert = "ReceiveAlert";
        public const string ReceiveCommandProgress = "ReceiveCommandProgress";
        public const string ReceiveIec61850Message = "ReceiveIec61850Message";
    }

    /// <summary>日志推送 DTO。</summary>
    public sealed class LogEntryDto
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = "";
        public string Logger { get; set; } = "";
        public string Message { get; set; } = "";
        public string? Thread { get; set; }
        public string? Exception { get; set; }
    }
}
