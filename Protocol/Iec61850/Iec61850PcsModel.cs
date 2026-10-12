using IEC61850.Common;
using IEC61850.Server;

namespace EssSimulator.Protocol.Iec61850
{
    /// <summary>
    /// 按 mapping.csv 动态拼 PCS IED：LLN0/LPHD + 各 LN 的 CDC。
    /// libiec61850 的 APC 没有 setMag(SP)，设定值按 ASG 建，ObjectRef 仍是 mapping 里的 setMag.f。
    /// </summary>
    internal sealed class Iec61850PcsModel : IDisposable
    {
        public const string DataSetName = "dsPcs";
        public const string UrcbName = "URCB1";
        public const string GooseDataSetName = "dsGoose";
        public const string GoCbName = "GoCB1";
        /// <summary>入向订阅 AppID 基数（PCS1 = 0x2001）。</summary>
        public const int DefaultSubscribeAppIdBase = 0x2000;
        /// <summary>CDC_CTL_MODEL_DIRECT_NORMAL（iec61850_cdc.h）。</summary>
        public const uint DirectNormal = 1;

        public IedModel Model { get; }
        public Dictionary<string, DataAttribute> LeafByParam { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, DataObject> ControlDoByParam { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, DataAttribute> SetpointByParam { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int GooseEntryCount { get; private set; }

        private Iec61850PcsModel(IedModel model)
        {
            Model = model;
        }

        public static Iec61850PcsModel Build(string iedName, Iec61850Mapping mapping)
        {
            Iec61850Native.EnsureLoaded();
            var model = new IedModel(iedName);
            var built = new Iec61850PcsModel(model);
            var ld = new LogicalDevice(Iec61850Mapping.LogicalDeviceName, model);

            var lln0 = new LogicalNode("LLN0", ld);
            CDC.Create_CDC_ENS(lln0, "Mod", 0);
            CDC.Create_CDC_ENS(lln0, "Health", 0);
            CDC.Create_CDC_LPL(lln0, "NamPlt", (uint)(CDC.CDC_OPTION_AC_LN0_M | CDC.CDC_OPTION_AC_LN0_EX));

            var lphd = new LogicalNode("LPHD1", ld);
            CDC.Create_CDC_DPL(lphd, "PhyNam", 0);
            CDC.Create_CDC_ENS(lphd, "PhyHealth", 0);
            CDC.Create_CDC_SPS(lphd, "Proxy", 0);

            var nodes = new Dictionary<string, LogicalNode>(StringComparer.OrdinalIgnoreCase)
            {
                ["LLN0"] = lln0,
                ["LPHD1"] = lphd
            };
            var dataObjects = new Dictionary<string, DataObject>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in mapping.Entries)
            {
                if (!TryParseRef(entry.ObjectRef, out var lnName, out var doName, out var leafPath))
                    continue;

                if (!nodes.TryGetValue(lnName, out var ln))
                {
                    ln = new LogicalNode(lnName, ld);
                    nodes[lnName] = ln;
                }

                string doKey = lnName + "." + doName;
                if (dataObjects.ContainsKey(doKey))
                    continue;

                var created = CreateCdc(ln, doName, InferCdc(entry.Cdc, leafPath), entry.CtlModel);
                if (created != null)
                    dataObjects[doKey] = created;
            }

            foreach (var entry in mapping.Entries)
            {
                if (!TryParseRef(entry.ObjectRef, out var lnName, out var doName, out var leafPath))
                    continue;

                string doRef = Iec61850Mapping.LogicalDeviceName + "/" + lnName + "." + doName;
                var dobj = model.GetModelNodeByShortObjectReference(doRef) as DataObject
                           ?? (dataObjects.TryGetValue(lnName + "." + doName, out var created) ? created : null);
                if (dobj == null)
                    continue;

                var leaf = model.GetModelNodeByShortObjectReference(entry.ObjectRef) as DataAttribute
                           ?? dobj.GetChild(leafPath) as DataAttribute;
                if (leaf != null)
                    built.LeafByParam[entry.ParamName] = leaf;

                if (string.Equals(entry.Cdc, "SPC", StringComparison.OrdinalIgnoreCase))
                    built.ControlDoByParam[entry.ParamName] = dobj;

                if (!entry.IsSetpoint)
                    continue;

                if (dobj.GetChild("setMag") is DataAttribute setMag)
                    built.SetpointByParam[entry.ParamName] = setMag;
                else if (leaf != null)
                    built.SetpointByParam[entry.ParamName] = leaf;
            }

            var dataSet = new DataSet(DataSetName, lln0);
            foreach (var entry in mapping.Entries.Where(e => e.IsStatusOrMeas && !e.IsGoose))
            {
                string? variable = ToDataSetVariable(entry);
                if (variable != null)
                    _ = new DataSetEntry(dataSet, variable, -1, null);
            }

            byte trgOps = (byte)(TriggerOptions.DATA_CHANGED | TriggerOptions.INTEGRITY | TriggerOptions.GI);
            byte rptOpts = (byte)(ReportOptions.SEQ_NUM | ReportOptions.TIME_STAMP | ReportOptions.REASON_FOR_INCLUSION
                                  | ReportOptions.DATA_SET | ReportOptions.DATA_REFERENCE);
            _ = new ReportControlBlock(
                UrcbName,
                lln0,
                iedName + Iec61850Mapping.LogicalDeviceName + "/LLN0." + UrcbName,
                false,
                DataSetName,
                1,
                trgOps,
                rptOpts,
                50,
                1000);

            built.GooseEntryCount = mapping.GooseEntries.Count;
            return built;
        }

        public void Dispose()
        {
            Model.Dispose();
        }

        internal static string InferCdc(string declared, string leafPath)
        {
            string first = leafPath.Split('.')[0];
            if (first is "phsAB" or "phsBC" or "phsCA")
                return "DEL";
            if (first is "phsA" or "phsB" or "phsC" or "neut" or "net" or "res")
                return "WYE";
            if (string.Equals(declared, "APC", StringComparison.OrdinalIgnoreCase))
                return "ASG";
            return declared;
        }

        internal static bool TryParseRef(string objectRef, out string ln, out string doName, out string leafPath)
        {
            ln = doName = leafPath = string.Empty;
            string norm = Iec61850Mapping.NormalizeRef(objectRef);
            int slash = norm.IndexOf('/');
            if (slash < 0)
                return false;
            string afterLd = norm[(slash + 1)..];
            int lnDot = afterLd.IndexOf('.');
            if (lnDot < 0)
                return false;
            ln = afterLd[..lnDot];
            string rest = afterLd[(lnDot + 1)..];
            int doDot = rest.IndexOf('.');
            if (doDot < 0)
            {
                doName = rest;
                leafPath = string.Empty;
                return true;
            }

            doName = rest[..doDot];
            leafPath = rest[(doDot + 1)..];
            return true;
        }

        internal static string IngressGoCbRefMms(int simIndex) =>
            $"EMS_PCS{Math.Max(1, simIndex):D2}{Iec61850Mapping.LogicalDeviceName}/LLN0$GO${GoCbName}";

        internal static string LocalGoCbRef(string iedName) =>
            iedName + Iec61850Mapping.LogicalDeviceName + "/LLN0." + GoCbName;

        internal static string LocalGoCbRefMms(string iedName) =>
            iedName + Iec61850Mapping.LogicalDeviceName + "/LLN0$GO$" + GoCbName;

        internal static ushort SubscribeAppId(int simIndex, int appIdBase) =>
            (ushort)(appIdBase + Math.Max(1, simIndex));

        internal static int SimIndexFromIedName(string iedName)
        {
            const string prefix = "TRNA_PCS";
            if (iedName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(iedName[prefix.Length..], out var n)
                && n > 0)
            {
                return n;
            }

            return 1;
        }

        internal static string? ToDataSetVariable(Iec61850MapEntry entry)
        {
            if (!TryParseRef(entry.ObjectRef, out var ln, out var doName, out var leafPath))
                return null;
            string rest = string.IsNullOrEmpty(leafPath) ? doName : doName + "$" + leafPath.Replace('.', '$');
            return $"{ln}${entry.Fc}${rest}";
        }

        private static DataObject? CreateCdc(LogicalNode ln, string doName, string cdc, int ctlModel)
        {
            uint ctl = ctlModel > 0 ? DirectNormal : 0;
            return cdc.ToUpperInvariant() switch
            {
                "MV" => CDC.Create_CDC_MV(ln, doName, 0, false),
                "SPC" => CDC.Create_CDC_SPC(ln, doName, 0, ctl),
                "ASG" => CDC.Create_CDC_ASG(ln, doName, 0, false),
                "APC" => CDC.Create_CDC_ASG(ln, doName, 0, false),
                "INS" => CDC.Create_CDC_INS(ln, doName, 0),
                "BCR" => CDC.Create_CDC_BCR(ln, doName, 0),
                "DEL" => CDC.Create_CDC_DEL(ln, doName, 0),
                "WYE" => CDC.Create_CDC_WYE(ln, doName, 0),
                "SPS" => CDC.Create_CDC_SPS(ln, doName, 0),
                "ENS" => CDC.Create_CDC_ENS(ln, doName, 0),
                _ => CDC.Create_CDC_MV(ln, doName, 0, false)
            };
        }
    }
}
