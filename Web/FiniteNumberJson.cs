using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using log4net;

namespace EssSimulator.Web
{
    /// <summary>
    /// HTTP 与 SignalR 写出时把 NaN / ±Infinity 收成 null。
    /// 组态文件的序列化不要调用 <see cref="Apply"/>。
    /// </summary>
    public static class FiniteNumberJson
    {
        internal const string ElementKey = "element";

        private static readonly ILog Log = LogManager.GetLogger(typeof(FiniteNumberJson));
        private static readonly ConcurrentDictionary<string, byte> Logged = new();

        public static void Apply(JsonSerializerOptions options)
        {
            if (options.Converters.Any(c => c is FiniteDoubleJsonConverter))
                return;

            options.Converters.Add(new FiniteDoubleJsonConverter(ElementKey));
            options.Converters.Add(new FiniteFloatJsonConverter(ElementKey));
            IJsonTypeInfoResolver resolver = options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver();
            options.TypeInfoResolver = resolver.WithAddedModifier(AnnotateProperties);
        }

        internal static void ResetWarningsForTests() => Logged.Clear();

        internal static bool WasLogged(string key) => Logged.ContainsKey(key);

        internal static int LoggedCount => Logged.Count;

        private static void AnnotateProperties(JsonTypeInfo typeInfo)
        {
            if (typeInfo.Kind != JsonTypeInfoKind.Object)
                return;

            foreach (JsonPropertyInfo prop in typeInfo.Properties)
            {
                string key = $"{typeInfo.Type.Name}.{prop.Name}";
                if (prop.PropertyType == typeof(double))
                    prop.CustomConverter = new FiniteDoubleJsonConverter(key);
                else if (prop.PropertyType == typeof(float))
                    prop.CustomConverter = new FiniteFloatJsonConverter(key);
            }
        }

        internal static double Coerce(double value) => double.IsFinite(value) ? value : 0;

        internal static float Coerce(float value) => float.IsFinite(value) ? value : 0;

        internal static void WarnOnce(string key)
        {
            if (!Logged.TryAdd(key, 0))
                return;
            Log.Warn($"JSON 写出省略非有限数: {key}");
        }
    }

    internal sealed class FiniteDoubleJsonConverter : JsonConverter<double>
    {
        private readonly string _key;

        public FiniteDoubleJsonConverter(string key) => _key = key;

        public override bool HandleNull => true;

        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return 0;
            return FiniteNumberJson.Coerce(reader.GetDouble());
        }

        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        {
            if (double.IsFinite(value))
            {
                writer.WriteNumberValue(value);
                return;
            }

            FiniteNumberJson.WarnOnce(_key);
            writer.WriteNullValue();
        }
    }

    internal sealed class FiniteFloatJsonConverter : JsonConverter<float>
    {
        private readonly string _key;

        public FiniteFloatJsonConverter(string key) => _key = key;

        public override bool HandleNull => true;

        public override float Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return 0;
            return FiniteNumberJson.Coerce(reader.GetSingle());
        }

        public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options)
        {
            if (float.IsFinite(value))
            {
                writer.WriteNumberValue(value);
                return;
            }

            FiniteNumberJson.WarnOnce(_key);
            writer.WriteNullValue();
        }
    }
}
