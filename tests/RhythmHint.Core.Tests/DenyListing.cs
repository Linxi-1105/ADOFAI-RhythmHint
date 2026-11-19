using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace RhythmHint.Core.Tests;

/// <summary>
/// 测试用：暂时不让当前用户列出 <c>path</c> 里的文件（Windows 文件夹权限），模拟没有权限、网络盘断开时列目录失败；
/// 文件夹本身还看得到（Directory.Exists 为 true）。Dispose 时恢复，测试结束能删掉临时文件夹。
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DenyListing : IDisposable
{
    private readonly DirectoryInfo _dir;
    private readonly DirectorySecurity _acl;
    private readonly FileSystemAccessRule _rule;

    public DenyListing(string path)
    {
        _dir = new DirectoryInfo(path);
        _rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
        _acl = _dir.GetAccessControl();
        _acl.AddAccessRule(_rule);
        _dir.SetAccessControl(_acl);
    }

    public void Dispose()
    {
        _acl.RemoveAccessRule(_rule);
        _dir.SetAccessControl(_acl);
    }
}
