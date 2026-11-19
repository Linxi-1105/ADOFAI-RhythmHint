using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace RhythmHint.Core;

/// <summary>读设置文件的结果。</summary>
public enum SettingsReadResult
{
    /// <summary>没有设置文件（新装）：全是默认值。</summary>
    Missing,

    /// <summary>整份读进来了。</summary>
    Read,

    /// <summary>整份读失败，逐项读回了能读的值，读不了的用默认值。</summary>
    Recovered,

    /// <summary>文件打不开（被占用、没权限）：全是默认值，原文件没动过。</summary>
    Unreadable,
}

/// <summary>读设置文件的结果和经过（日志、给玩家的提示用）。</summary>
/// <typeparam name="T">设置的类型。</typeparam>
public sealed class SettingsLoad<T>
    where T : class, new()
{
    internal SettingsLoad(T value, SettingsReadResult result, string error = "", string badCopy = "", string backupError = "",
        IReadOnlyList<string>? unread = null, bool truncated = false)
    {
        Value = value;
        Result = result;
        Error = error;
        BadCopy = badCopy;
        BackupError = backupError;
        Unread = unread ?? Array.Empty<string>();
        Truncated = truncated;
    }

    public T Value { get; }

    public SettingsReadResult Result { get; }

    /// <summary>整份读失败、或者打不开文件的原因（XmlSerializer / 文件系统的报错，写日志）。</summary>
    public string Error { get; }

    /// <summary>读失败时另存的原文件（完整路径）；没另存（不需要，或另存失败）为空。</summary>
    public string BadCopy { get; }

    /// <summary>另存原文件失败的原因；没失败为空。</summary>
    public string BackupError { get; }

    /// <summary>文件里有、但读不进来、改用默认值的字段（元素名，按文件里的顺序）。</summary>
    public IReadOnlyList<string> Unread { get; }

    /// <summary>文件中途坏了（不是完整的 XML，比如写到一半断电）：坏掉的地方以后的字段读不到，用默认值。</summary>
    public bool Truncated { get; }

    /// <summary>
    /// 能不能保存设置：整份读失败又没能另存原文件、或者文件打不开时为 false，免得把玩家原来的设置覆盖掉
    /// （打不开可能只是一时被占用，下次启动还能读）。
    /// </summary>
    public bool CanSave => Result is SettingsReadResult.Missing or SettingsReadResult.Read
        || (Result == SettingsReadResult.Recovered && BadCopy.Length > 0);
}

/// <summary>
/// 读 UMM 的设置文件（Settings.xml）。UMM 自带的 ModSettings.Load 读失败时（字段值写法不对、不认识的枚举值、文件写到一半）
/// 只记一句「Can't read …」就返回全默认的设置，下一次保存就把玩家原来的文件覆盖掉，连同各谱选过的手序一起丢。这里改成：
/// <list type="number">
/// <item>先和 UMM 一样整份读（XmlSerializer）；读得进来就完事，结果和 UMM 一模一样</item>
/// <item>读失败时先把原文件另存成「Settings.xml.bad-时间」（不覆盖已有的），再逐项读：每个字段单独交给同一个 XmlSerializer，
/// 读不了的那项用默认值，其余照样读回；文件写到一半坏了时，坏掉之前的照样读回</item>
/// </list>
/// 写文件仍由 UMM 做（ModSettings.Save），写出来的格式不变，老版本照样能读。
/// </summary>
public static class SettingsFile
{
    /// <param name="path">设置文件的完整路径。</param>
    /// <param name="now">另存坏文件时文件名里的时间。</param>
    public static SettingsLoad<T> Load<T>(string path, DateTime now)
        where T : class, new()
    {
        if (!File.Exists(path))
        {
            return new SettingsLoad<T>(new T(), SettingsReadResult.Missing);
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SettingsLoad<T>(new T(), SettingsReadResult.Unreadable, ex.Message);
        }

        var serializer = new XmlSerializer(typeof(T));
        string error;
        try
        {
            using var stream = new MemoryStream(bytes);
            if (serializer.Deserialize(stream) is T whole)
            {
                return new SettingsLoad<T>(whole, SettingsReadResult.Read);
            }

            error = "根元素是 xsi:nil，读出来是空的";
        }
        catch (Exception ex) when (IsReadError(ex))
        {
            error = Describe(ex);
        }

        var (badCopy, backupError) = Backup(path, bytes, now);
        var (value, unread, truncated) = Salvage<T>(bytes, serializer);
        return new SettingsLoad<T>(value, SettingsReadResult.Recovered, error, badCopy, backupError, unread, truncated);
    }

    /// <summary>另存坏文件：「Settings.xml.bad-20261119-153012」，同一秒已经有了就加「-2」「-3」……，不覆盖已有的。</summary>
    /// <returns>另存的路径和失败原因（成功时原因为空，失败时路径为空）。</returns>
    private static (string Path, string Error) Backup(string path, byte[] bytes, DateTime now)
    {
        var stem = path + ".bad-" + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        for (var i = 1; i <= 100; i++)
        {
            var target = i == 1 ? stem : stem + "-" + i.ToString(CultureInfo.InvariantCulture);
            if (File.Exists(target))
            {
                continue;
            }

            try
            {
                using (var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
                {
                    file.Write(bytes, 0, bytes.Length);
                }

                return (target, string.Empty);
            }
            catch (IOException) when (File.Exists(target))
            {
                // 刚好被别人抢先建了同名文件：换下一个名字
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return (string.Empty, ex.Message);
            }
        }

        return (string.Empty, "同名的坏文件太多（" + stem + "-100）");
    }

    /// <summary>逐项读：每个元素单独包成一份只有它的设置文件交给 XmlSerializer，读得进来就抄到结果里。</summary>
    private static (T Value, List<string> Unread, bool Truncated) Salvage<T>(byte[] bytes, XmlSerializer serializer)
        where T : class, new()
    {
        var (elements, truncated) = Elements(bytes);
        var root = RootName(typeof(T));
        var members = Members(typeof(T));
        var value = new T();
        var unread = new List<string>();
        foreach (var (name, xml) in elements)
        {
            if (!members.TryGetValue(name, out var member))
            {
                continue; // 不认识的字段（老版本留下的、新版本加的）：XmlSerializer 本来就跳过
            }

            var doc = "<" + root + " xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">"
                + xml + "</" + root + ">";
            try
            {
                using var reader = new StringReader(doc);
                if (serializer.Deserialize(reader) is T one)
                {
                    member.Copy(one, value);
                    continue;
                }
            }
            catch (Exception ex) when (IsReadError(ex))
            {
                // 这一项的值读不了（写法不对、不认识的枚举值）：用默认值，记进 unread
            }

            unread.Add(name);
        }

        return (value, unread, truncated);
    }

    /// <summary>
    /// 根元素下面的每个子元素（元素名、原文），同名的只取第一个（和 XmlSerializer 一样）。
    /// XML 中途坏了就停在那里，前面读到的照样返回。
    /// </summary>
    private static (List<(string Name, string Xml)> Elements, bool Truncated) Elements(byte[] bytes)
    {
        var elements = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        };
        try
        {
            using var stream = new MemoryStream(bytes);
            using var reader = XmlReader.Create(stream, settings);
            if (reader.MoveToContent() != XmlNodeType.Element || reader.IsEmptyElement)
            {
                return (elements, false);
            }

            reader.Read();
            while (!reader.EOF)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    var name = reader.LocalName;
                    var xml = reader.ReadOuterXml(); // 读完整个元素，停在它后面
                    if (seen.Add(name))
                    {
                        elements.Add((name, xml));
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == 0)
                {
                    break;
                }
                else
                {
                    reader.Read();
                }
            }

            return (elements, false);
        }
        catch (XmlException)
        {
            // 文件中途坏了：坏掉之前读到的照样用
            return (elements, true);
        }
    }

    private static string RootName(Type type) =>
        type.GetCustomAttribute<XmlRootAttribute>() is { ElementName.Length: > 0 } root ? root.ElementName : type.Name;

    /// <summary>XmlSerializer 会读写的成员：公开实例字段和可读写的公开属性（没标 XmlIgnore），按元素名查。</summary>
    private static Dictionary<string, Member> Members(Type type)
    {
        var members = new Dictionary<string, Member>(StringComparer.Ordinal);
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!field.IsInitOnly && !field.IsDefined(typeof(XmlIgnoreAttribute)))
            {
                members[ElementName(field)] = new Member(o => field.GetValue(o), (o, v) => field.SetValue(o, v));
            }
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetGetMethod() != null && property.GetSetMethod() != null && property.GetIndexParameters().Length == 0
                && !property.IsDefined(typeof(XmlIgnoreAttribute)))
            {
                members[ElementName(property)] = new Member(o => property.GetValue(o), (o, v) => property.SetValue(o, v));
            }
        }

        return members;
    }

    private static string ElementName(MemberInfo member) =>
        member.GetCustomAttribute<XmlElementAttribute>() is { ElementName.Length: > 0 } element ? element.ElementName : member.Name;

    /// <summary>
    /// XmlSerializer 读不了时抛的：.NET 和 Mono 都把读取错误（XML 本身坏了、值的写法不对、不认识的枚举值）包成 InvalidOperationException；
    /// 万一哪个运行时没包，原样的 XmlException、FormatException、OverflowException 也算。
    /// </summary>
    private static bool IsReadError(Exception ex) => ex is InvalidOperationException or XmlException or FormatException or OverflowException;

    private static string Describe(Exception ex)
    {
        var text = new StringBuilder(ex.Message);
        for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
        {
            text.Append(" → ").Append(inner.Message);
        }

        return text.ToString();
    }

    private sealed class Member
    {
        private readonly Func<object, object?> _get;
        private readonly Action<object, object?> _set;

        public Member(Func<object, object?> get, Action<object, object?> set)
        {
            _get = get;
            _set = set;
        }

        public void Copy(object from, object to) => _set(to, _get(from));
    }
}
