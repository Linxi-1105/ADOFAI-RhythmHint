using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RhythmHint.Core;

/// <summary>
/// 写 Windows 快捷方式（.lnk）：桌面上打开手序编辑器的那个，带编辑器自己的图标（2026-11-18 用户：能不能加个图标）。
/// 按微软公开的 Shell Link 格式（MS-SHLLINK）直接写文件，不调 COM、不开 PowerShell（游戏进程开 PowerShell 容易被杀毒软件拦）。
/// 目标路径写成 Unicode，路径里有中文也行（之前的 .url 实测在中文路径下打不开）；目标是 .html，双击用默认浏览器打开。
/// 只写最少的几块：文件头、LinkInfo（本地路径）、说明、工作目录、图标、结束块。
/// </summary>
public static class ShellLink
{
    private const uint HasLinkTargetIdList = 0x01;
    private const uint HasLinkInfo = 0x02;
    private const uint HasName = 0x04;
    private const uint HasWorkingDir = 0x10;
    private const uint HasIconLocation = 0x40;
    private const uint IsUnicode = 0x80;
    private const uint FileAttributeArchive = 0x20;
    private const uint ShowNormal = 1;
    private const uint DriveFixed = 3;

    /// <summary>快捷方式的类标识 00021401-0000-0000-C000-000000000046（文件头里固定的值）。</summary>
    private static readonly byte[] LinkClsid = { 0x01, 0x14, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46 };

    /// <summary>快捷方式文件的内容。</summary>
    /// <param name="target">目标的完整路径（带盘符）。</param>
    /// <param name="workingDirectory">起始位置。</param>
    /// <param name="icon">图标文件的完整路径；null 用目标文件类型的默认图标。</param>
    /// <param name="description">鼠标悬停时的说明。</param>
    public static byte[] Build(string target, string workingDirectory, string? icon, string description)
    {
        if (target.Length < 3 || target[1] != ':' || (target[2] != '\\' && target[2] != '/') || !char.IsLetter(target[0]))
        {
            throw new ArgumentException("不是带盘符的完整路径：" + target, nameof(target));
        }

        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true))
        {
            var flags = HasLinkTargetIdList | HasLinkInfo | HasName | HasWorkingDir | IsUnicode | (icon != null ? HasIconLocation : 0);
            var path = target.Replace('/', '\\');

            // 文件头（76 字节）
            w.Write(0x4Cu);
            w.Write(LinkClsid);
            w.Write(flags);
            w.Write(FileAttributeArchive);
            w.Write(0L); // 创建时间
            w.Write(0L); // 访问时间
            w.Write(0L); // 修改时间
            w.Write(0u); // 文件大小
            w.Write(0); // 图标序号
            w.Write(ShowNormal);
            w.Write((ushort)0); // 快捷键
            w.Write((ushort)0);
            w.Write(0u);
            w.Write(0u);

            WriteIdList(w, path);
            WriteLinkInfo(w, path);
            WriteString(w, description);
            WriteString(w, workingDirectory);
            if (icon != null)
            {
                WriteString(w, icon);
            }

            w.Write(0u); // 结束块
        }

        return stream.ToArray();
    }

    /// <summary>
    /// 目标的外壳项目列表（双击时系统靠它找到目标；只有 LinkInfo 实测打不开）：「此电脑」→ 盘符 → 每一级文件夹 → 文件。
    /// 每一级按系统自己写的样子：名字用 Unicode，后面跟一个 0xBEEF0004 扩展块再存一遍长名字。时间、文件编号这些写 0。
    /// </summary>
    private static void WriteIdList(BinaryWriter w, string path)
    {
        var items = new List<byte[]> { MyComputer, DriveItem(path.Substring(0, 3)) };
        var parts = path.Substring(3).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            items.Add(FileEntry(parts[i], directory: i < parts.Length - 1));
        }

        w.Write((ushort)(items.Sum(item => item.Length) + 2));
        foreach (var item in items)
        {
            w.Write(item);
        }

        w.Write((ushort)0);
    }

    /// <summary>「此电脑」：根项目，类标识 20D04FE0-3AEA-1069-A2D8-08002B30309D。</summary>
    private static readonly byte[] MyComputer =
    {
        0x14, 0x00, 0x1F, 0x50, 0xE0, 0x4F, 0xD0, 0x20, 0xEA, 0x3A, 0x69, 0x10, 0xA2, 0xD8, 0x08, 0x00, 0x2B, 0x30, 0x30, 0x9D,
    };

    /// <summary>盘符项目：0x2F 加「C:\」，补 0 到 25 字节。</summary>
    private static byte[] DriveItem(string root)
    {
        var item = new byte[25];
        item[0] = 25;
        item[2] = 0x2F;
        Encoding.ASCII.GetBytes(char.ToUpperInvariant(root[0]) + ":\\", 0, 3, item, 3);
        return item;
    }

    /// <summary>文件夹或文件：0x35 / 0x36（带 Unicode 名字的文件项），名字后面是 0xBEEF0004 扩展块（第 9 版，和 Windows 10/11 写的一样）。</summary>
    private static byte[] FileEntry(string name, bool directory)
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true))
        {
            w.Write((ushort)0); // 大小，最后补
            w.Write((byte)(directory ? 0x35 : 0x36));
            w.Write((byte)0);
            w.Write(0u); // 文件大小
            w.Write(0u); // 修改时间
            w.Write((ushort)(directory ? 0x10 : 0x20)); // 属性：文件夹 / 存档
            w.Write(Encoding.Unicode.GetBytes(name + "\0"));

            var extension = (ushort)stream.Position;
            var longName = Encoding.Unicode.GetBytes(name + "\0");
            w.Write((ushort)(46 + longName.Length + 2)); // 扩展块大小
            w.Write((ushort)9); // 版本
            w.Write(0xBEEF0004u);
            w.Write(0u); // 创建时间
            w.Write(0u); // 访问时间
            w.Write((ushort)0x2E); // 第 9 版的标识
            w.Write((ushort)0);
            w.Write(0L); // NTFS 文件编号
            w.Write(0L);
            w.Write((ushort)0); // 本地化名字的大小
            w.Write(0u);
            w.Write(0u);
            w.Write(longName);
            w.Write(extension); // 扩展块在这一项里的位置
        }

        var item = stream.ToArray();
        item[0] = (byte)(item.Length & 0xFF);
        item[1] = (byte)(item.Length >> 8);
        return item;
    }

    /// <summary>LinkInfo：卷信息 + 本地路径（ANSI 一份、Unicode 一份；ANSI 里认不出的字换成问号，系统用 Unicode 那份）。</summary>
    private static void WriteLinkInfo(BinaryWriter w, string path)
    {
        const int headerSize = 0x24; // 带 Unicode 路径偏移的头
        const int volumeIdSize = 17; // 4 个 uint + 空卷标的一个 0
        var ansi = AsciiOrQuestionMark(path);
        var volumeIdOffset = headerSize;
        var localBasePathOffset = volumeIdOffset + volumeIdSize;
        var commonPathSuffixOffset = localBasePathOffset + ansi.Length + 1;
        var localBasePathUnicodeOffset = commonPathSuffixOffset + 1;
        var commonPathSuffixUnicodeOffset = localBasePathUnicodeOffset + ((path.Length + 1) * 2);
        var size = commonPathSuffixUnicodeOffset + 2;

        w.Write(size);
        w.Write(headerSize);
        w.Write(1u); // VolumeIDAndLocalBasePath
        w.Write(volumeIdOffset);
        w.Write(localBasePathOffset);
        w.Write(0); // 没有网络路径
        w.Write(commonPathSuffixOffset);
        w.Write(localBasePathUnicodeOffset);
        w.Write(commonPathSuffixUnicodeOffset);

        w.Write((uint)volumeIdSize);
        w.Write(DriveFixed);
        w.Write(0u); // 卷序列号
        w.Write(0x10u); // 卷标在卷信息里的偏移
        w.Write((byte)0); // 空卷标

        w.Write(ansi);
        w.Write((byte)0);
        w.Write((byte)0); // 空的公共后缀
        w.Write(Encoding.Unicode.GetBytes(path + "\0"));
        w.Write((ushort)0);
    }

    /// <summary>StringData：字数（2 字节）+ UTF-16 文字，不带结尾的 0。</summary>
    private static void WriteString(BinaryWriter w, string text)
    {
        w.Write((ushort)text.Length);
        w.Write(Encoding.Unicode.GetBytes(text));
    }

    private static byte[] AsciiOrQuestionMark(string text)
    {
        var bytes = new byte[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            bytes[i] = text[i] < 128 ? (byte)text[i] : (byte)'?';
        }

        return bytes;
    }
}
