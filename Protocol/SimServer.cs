using System;
using EssSimulator.Core;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EssSimulator.EssSimModelApi;
using System.Diagnostics;
using log4net;


namespace EssSimulator
{
    public class SimServer
    {
        public static Dictionary<string, bool> clientConnectState { get; set; } = new();
        public static Dictionary<string,string> serverListenInfo = new Dictionary<string, string>();
        
        /// <summary>
        /// 从外部接口或者实时仿真模型产出数据
        /// </summary>
        /// <param name="arg1">变量的域名</param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public static object? GetExtIfVariableVal(string arg1)
        {
            string[] parts = arg1.Split('.',2);
            var objectsCollect = SimulatorHost.Instance;
            var targetObj = objectsCollect.Get<object>(parts[0]);
            if (targetObj == null)
            {
                return null;
            }
            return ObjectPathResolver.GetValue(targetObj, parts[1]);
        }


        public static bool SetExtIfVariableVal(string arg1, object ctlVal)
        {
            string[] parts = arg1.Split('.', 2);

            var objectsCollect = SimulatorHost.Instance;
            var targetObj = objectsCollect.Get<object>(parts[0]);
            if (targetObj == null)
            {
                return false;
            }
            return ObjectPathResolver.SetValue(targetObj, parts[1], ctlVal);
        }
    }
}
