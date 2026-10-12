using EssSimulator.Protocol.Modbus;
using log4net;

namespace EssSimulator
{
    /// <summary>
    /// ModBus协议解析器
    /// @author : ljw
    /// @date : 2024/04/26
    /// </summary>
    public class ModbusParser
    {
        private List<MapEntry[]> modbusPointMap;
        ILog log = LogManager.GetLogger(typeof(ModbusParser));
        public ModbusParser(List<MapEntry[]> modbusPointMap)
        {
            this.modbusPointMap = modbusPointMap;
        }

        /// <summary>
        /// 数据下发转换
        /// </summary>
        /// <param name="models">下发的参数名与值对应关系的Dictionary<string, object></param>
        /// <returns>参数名与字节数组对应关系的Dictionary</returns>
        public Dictionary<string, byte[]> DataEncryption(Dictionary<string, object> models)
        {
            Dictionary<string, byte[]> reslut = new Dictionary<string, byte[]>();

            foreach (var item in models)
            {
                var point = FindPointByName(item.Key);
                if (item.Value == null) throw new NullReferenceException("value is null");
                byte[] resultBytes = ModbusPointCodec.Encode(item.Value, point, applyScale: true);
                reslut.Add(item.Key, resultBytes);
            }
            return reslut;
        }

        /// <summary>
        /// 数据解析
        /// </summary>
        /// <param name="originalData">参数名与读取上来的字节数组对应关系</param>
        /// <returns>参数名与解析后的实际值对应关系的Dictionary<string, object></returns>
        public Dictionary<string, object> DataParse(Dictionary<string, object> originalData)
        {
            try
            {
                var ResultArray = new Dictionary<string, object>();
                foreach (var item in originalData)
                {
                    MapEntry? point = FindPointByName(item.Key);
                    if (point == null)
                        continue;

                    // 61850 / 点影子可能直接写入工程值；Modbus 读回仍为字节数组。
                    if (item.Value is byte[] data)
                        ResultArray.Add(item.Key, ModbusPointCodec.Decode(data, point));
                    else
                        ResultArray.Add(item.Key, item.Value);
                }
                return ResultArray;
            }
            catch (Exception ex)
            {
                log.Error(ex.Message);
                return new Dictionary<string, object>();
            }
        }

        private MapEntry FindPointByName(string paramName)
        {
            foreach (var map in modbusPointMap)
            {
                var point = map.FirstOrDefault(x => x.ParamName == paramName);
                if (point != null)
                {
                    return point;
                }
            }
            throw new Exception($"Point with name {paramName} not found.");
        }
    }
}
