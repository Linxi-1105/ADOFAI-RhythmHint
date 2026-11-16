// netstandard2.0 / .NET Framework 没有这个类型，record 和 init 需要它。
// 只是编译器标记，没有运行时行为。
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit
{
}
