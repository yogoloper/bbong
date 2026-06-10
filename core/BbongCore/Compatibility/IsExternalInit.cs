// netstandard2.1에는 init 접근자/record가 요구하는 IsExternalInit이 없어 폴리필합니다.
// Unity(.NET Standard 2.1) 호환을 위해 코어를 netstandard2.1로 타깃하기 때문입니다.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit
{
}
