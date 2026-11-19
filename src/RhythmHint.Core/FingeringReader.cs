using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RhythmHint.Core;

/// <summary>
/// 读手序文件（rhythmhint-fingering）的 JSON：用 JsonTextReader 从头读一遍，边读边数元素个数，不先把整个文件建成 JObject。
/// hits、notes 记成小结构（各最多 <see cref="MaxHits"/> 行，再多的只数不留，校验时整份拒绝），charts 只留文字；
/// 别的顶层字段（文件头、以后版本加的字段）才建成 JToken，加起来最多 <see cref="MaxOtherElements"/> 个元素，
/// 超出的不认识字段只读过去不留（手序照样能用，见 <see cref="CustomFingering.DroppedFields"/>）。
/// 复现（2026-11-19 评审 C 中2）：原来整个文件先建成 JObject，20 MB 以内的文件内存放大约 56 倍，680 万个 [] 峰值 1.37 GB、主线程卡 2.4 秒。
/// <para>
/// 校验在 <see cref="CustomFingering"/> 里，和 1.3.x 一样（只放宽：值是整数的浮点写法当整数），按原来的顺序报第一处错；
/// 同名字段取最后一个（和 JObject.Load 一样），注释、结尾多余的逗号照旧接受（Newtonsoft 的宽松写法），JSON 后面多出别的内容照旧拒绝。
/// </para>
/// </summary>
internal static class FingeringReader
{
    /// <summary>hits 最多这么多行（1.3.0 起就是这个数）。notes 每个音符至少一块砖，也不会更多。</summary>
    public const int MaxHits = 200_000;

    /// <summary>
    /// hits、notes、charts 以外的字段（文件头和以后版本加的字段）最多建这么多个元素，再多的不认识字段跳过（不拒绝：1.3.x 本来就丢掉所有不认识的字段）。
    /// 一个元素约 120 字节，最坏多占约 30 MB。
    /// </summary>
    public const int MaxOtherElements = 250_000;

    /// <summary>charts 最多认这么多个指纹（每重新保存一版加一个，正常的文件只有几个）。</summary>
    public const int MaxCharts = 10_000;

    /// <summary>键数最多 16（<see cref="CustomFingering.MaxKeys"/>），一个音符超过 17 对轨道时前 17 对里一定有错，后面的不用留。</summary>
    private const int MaxLanePairs = CustomFingering.MaxKeys + 1;

    /// <summary>读出来、还没校验的文件。</summary>
    internal sealed class RawFile
    {
        /// <summary>hits、notes、charts 以外的顶层字段；同名的位置按第一次出现、值取最后一次。</summary>
        public JObject Header { get; } = new();

        /// <summary>hits 的前 <see cref="MaxHits"/> 行；hits 不是数组时为 null。</summary>
        public List<Row>? Hits { get; set; }

        /// <summary>hits 一共多少行。</summary>
        public int HitCount { get; set; }

        public List<NoteRow>? Notes { get; set; }

        public int NoteCount { get; set; }

        /// <summary>charts 里 100 个字以内、不重复的文字；charts 不是数组时为空。</summary>
        public List<string> Charts { get; } = new();

        /// <summary>太大没读的不认识字段。</summary>
        public List<string> Dropped { get; } = new();
    }

    /// <summary>一个值：数字（long、double 或超出 long 的大整数）、文字、null、true/false；数组、对象只记类型。</summary>
    internal readonly struct Scalar
    {
        public Scalar(JsonToken type, object? value)
        {
            Type = type;
            Value = value;
        }

        public JsonToken Type { get; }

        public object? Value { get; }

        public bool IsNumber => Type is JsonToken.Integer or JsonToken.Float;

        public bool IsNull => Type == JsonToken.Null;

        public static Scalar Of(JToken? token) => token is JValue v
            ? new Scalar(v.Type switch
            {
                JTokenType.Integer => JsonToken.Integer,
                JTokenType.Float => JsonToken.Float,
                JTokenType.Null => JsonToken.Null,
                _ => JsonToken.String,
            }, v.Value)
            : new Scalar(token == null ? JsonToken.None : JsonToken.StartObject, null);
    }

    /// <summary>hits 的一行（只留前 4 项，后面的不看）或 notes 里的一对 [轨道, 长按]。</summary>
    internal readonly struct Row
    {
        public Row(bool isArray, int count, Scalar a, Scalar b, Scalar c, Scalar d)
        {
            IsArray = isArray;
            Count = count;
            A = a;
            B = b;
            C = c;
            D = d;
        }

        public bool IsArray { get; }

        /// <summary>数组里一共几项。</summary>
        public int Count { get; }

        public Scalar A { get; }

        public Scalar B { get; }

        public Scalar C { get; }

        public Scalar D { get; }
    }

    /// <summary>notes 的一行：[第一块砖号, 最后一块砖号, [[轨道, 长按], …]]。</summary>
    internal sealed class NoteRow
    {
        public bool IsArray { get; set; }

        public int Count { get; set; }

        public Scalar First { get; set; }

        public Scalar Last { get; set; }

        /// <summary>第 3 项的轨道表（最多留 <see cref="MaxLanePairs"/> 对）；第 3 项不是数组时为 null。</summary>
        public List<Row>? Lanes { get; set; }
    }

    /// <exception cref="FormatException">不是有效的 JSON。</exception>
    public static RawFile Load(string json)
    {
        try
        {
            var raw = new RawFile();
            long budget = MaxOtherElements;
            using var r = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
            StartObject(r);
            while (Next(r) && r.TokenType == JsonToken.PropertyName)
            {
                var name = (string)r.Value!;
                Next(r);
                switch (name)
                {
                    case "hits":
                        (raw.Hits, raw.HitCount) = r.TokenType == JsonToken.StartArray ? ReadRows(r, ReadRow) : (null, 0);
                        break;
                    case "notes":
                        (raw.Notes, raw.NoteCount) = r.TokenType == JsonToken.StartArray ? ReadRows(r, ReadNote) : (null, 0);
                        break;
                    case "charts":
                        raw.Charts.Clear(); // 同名的取最后一个
                        ReadCharts(r, raw.Charts);
                        break;
                    default:
                        budget = ReadOther(r, name, raw, budget);
                        break;
                }

                r.Skip(); // 上面没读的（不是数组的 hits、notes 等）跳过；已经读到结尾时什么也不做
            }

            EndOfDocument(r);
            return raw;
        }
        catch (JsonException ex)
        {
            throw new FormatException("不是有效的 JSON：" + ex.Message, ex);
        }
    }

    /// <summary>
    /// 文件头和不认识的字段：不太大就建成 JToken；太大的（加上前面的超过 <see cref="MaxOtherElements"/>）读过去不留——
    /// 已知字段留一个同类的空壳（照样按类型校验、拒绝），不认识的整个去掉、记进 Dropped。
    /// </summary>
    private static long ReadOther(JsonTextReader r, string name, RawFile raw, long budget)
    {
        var start = r.TokenType;
        var value = ReadBounded(r, ref budget);
        if (value != null)
        {
            raw.Header[name] = value;
        }
        else if (CustomFingering.IsKnownField(name))
        {
            raw.Header[name] = start == JsonToken.StartArray ? new JArray() : new JObject();
        }
        else
        {
            raw.Header.Remove(name); // 前面同名的那个也作废（同名取最后一个）
            raw.Dropped.Add(name);
        }

        return budget;
    }

    /// <summary>读当前这个值建成 JToken，最多用 budget 个元素（数组、对象、每个值各算一个）；超出时读完这个值、不留，返回 null，额度不扣。</summary>
    private static JToken? ReadBounded(JsonTextReader r, ref long budget)
    {
        if (r.TokenType is not (JsonToken.StartArray or JsonToken.StartObject))
        {
            budget--;
            return JToken.ReadFrom(r);
        }

        var depth = r.Depth;
        var writer = new JTokenWriter();
        long used = 0;
        var keep = true;
        do
        {
            var t = r.TokenType;
            if (t == JsonToken.Comment)
            {
                continue; // 同 JObject.Load：注释不进树
            }

            if (keep && t is not (JsonToken.PropertyName or JsonToken.EndArray or JsonToken.EndObject) && ++used > budget)
            {
                keep = false;
            }

            if (keep)
            {
                writer.WriteToken(r, writeChildren: false);
            }

            if (t is JsonToken.EndArray or JsonToken.EndObject && r.Depth == depth)
            {
                break;
            }
        }
        while (r.Read());

        if (!keep)
        {
            return null;
        }

        budget -= used;
        return writer.Token;
    }

    /// <summary>一个数组的每一行：最多留 <see cref="MaxHits"/> 行（再多的只数，校验时整份拒绝），返回留下的行和一共几行。</summary>
    private static (List<T>? Rows, int Count) ReadRows<T>(JsonTextReader r, Func<JsonTextReader, T> read)
    {
        var rows = new List<T>();
        var count = 0;
        while (Next(r) && r.TokenType != JsonToken.EndArray)
        {
            if (++count <= MaxHits)
            {
                rows.Add(read(r));
            }
            else
            {
                r.Skip();
            }
        }

        return (rows, count);
    }

    /// <summary>一行 [a, b, c, d, …]：记下一共几项和前 4 项。</summary>
    private static Row ReadRow(JsonTextReader r)
    {
        if (r.TokenType != JsonToken.StartArray)
        {
            r.Skip();
            return default;
        }

        Scalar a = default, b = default, c = default, d = default;
        var count = 0;
        while (Next(r) && r.TokenType != JsonToken.EndArray)
        {
            var value = Capture(r);
            switch (count++)
            {
                case 0: a = value; break;
                case 1: b = value; break;
                case 2: c = value; break;
                case 3: d = value; break;
            }
        }

        return new Row(true, count, a, b, c, d);
    }

    private static NoteRow ReadNote(JsonTextReader r)
    {
        var row = new NoteRow();
        if (r.TokenType != JsonToken.StartArray)
        {
            r.Skip();
            return row;
        }

        row.IsArray = true;
        while (Next(r) && r.TokenType != JsonToken.EndArray)
        {
            switch (row.Count++)
            {
                case 0: row.First = Capture(r); break;
                case 1: row.Last = Capture(r); break;
                case 2: row.Lanes = ReadLanes(r); break;
                default: r.Skip(); break;
            }
        }

        return row;
    }

    private static List<Row>? ReadLanes(JsonTextReader r)
    {
        if (r.TokenType != JsonToken.StartArray)
        {
            r.Skip();
            return null;
        }

        var lanes = new List<Row>();
        while (Next(r) && r.TokenType != JsonToken.EndArray)
        {
            if (lanes.Count < MaxLanePairs)
            {
                lanes.Add(ReadRow(r));
            }
            else
            {
                r.Skip();
            }
        }

        return lanes;
    }

    private static void ReadCharts(JsonTextReader r, List<string> charts)
    {
        if (r.TokenType != JsonToken.StartArray)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (Next(r) && r.TokenType != JsonToken.EndArray)
        {
            if (r.TokenType == JsonToken.String && charts.Count < MaxCharts && r.Value is string chart && chart.Length <= 100 && seen.Add(chart))
            {
                charts.Add(chart);
            }

            r.Skip();
        }
    }

    /// <summary>当前的值；是数组、对象就只记类型、跳过里面。</summary>
    private static Scalar Capture(JsonTextReader r)
    {
        var value = new Scalar(r.TokenType, r.Value);
        r.Skip();
        return value;
    }

    /// <summary>读下一个记号，跳过注释。</summary>
    private static bool Next(JsonTextReader r)
    {
        while (r.Read())
        {
            if (r.TokenType != JsonToken.Comment)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>根必须是对象（同 JObject.Load）。</summary>
    private static void StartObject(JsonTextReader r)
    {
        if (!Next(r) || r.TokenType != JsonToken.StartObject)
        {
            throw new FormatException("不是有效的 JSON：最外层不是对象");
        }
    }

    /// <summary>根对象读完以后只能有注释。</summary>
    private static void EndOfDocument(JsonTextReader r)
    {
        if (r.TokenType != JsonToken.EndObject)
        {
            throw new FormatException("不是有效的 JSON：内容没有结束");
        }

        if (Next(r))
        {
            throw new FormatException("不是有效的 JSON：结尾还有多余的内容");
        }
    }

    /// <summary>整数。值是整数的浮点写法（1.0、1e0）也算（2026-11-19 评审 A 中7：编辑器收、Mod 拒）。</summary>
    public static int Int(Scalar s, string what)
    {
        if (!s.IsNumber)
        {
            throw new FormatException(what + " 必须是整数");
        }

        switch (s.Value)
        {
            case long l when l >= int.MinValue && l <= int.MaxValue:
                return (int)l;
            case int i:
                return i;
            case double d when Math.Floor(d) == d && !double.IsInfinity(d):
                return d >= int.MinValue && d <= int.MaxValue ? (int)d : throw new FormatException(what + " 超出范围");
            case double:
                throw new FormatException(what + " 必须是整数");
            default:
                throw new FormatException(what + " 超出范围"); // 超出 int 的 long、超出 long 的大整数
        }
    }

    public static double Num(Scalar s, string what)
    {
        if (!s.IsNumber)
        {
            throw new FormatException(what + " 必须是数字");
        }

        var value = s.Value switch
        {
            long l => l,
            int i => i,
            double d => d,

            // 超出 long 的大整数（BigInteger）：和 1.3.x 一样交给 Newtonsoft 换成 double（不同运行时舍入不同，照它的来）
            _ => (double)new JValue(s.Value),
        };
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new FormatException(what + " 必须是有限数");
        }

        return value;
    }
}
