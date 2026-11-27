using System.Reflection;
using System.Reflection.Emit;

namespace Chmonos.App.Tests.Support;

/// <summary>
/// 型のメソッド（入れ子の型＝ラムダ・async の状態機械も含む）の命令列を読み、<c>newobj</c> で作っている型を集める。
/// 「主画面が画面に入れ得る型」を手書きの一覧に頼らず集めるため（画面を足して名前を付け忘れたら、試験が落ちる）。
/// 命令の長さは <see cref="OpCodes"/> の表から引き、引数の中の値を命令と取り違えない
/// </summary>
internal static class ConstructedTypes
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    public static IReadOnlySet<Type> Of(Type type)
    {
        var found = new HashSet<Type>();
        Collect(type, found);
        return found;
    }

    private static void Collect(Type type, HashSet<Type> found)
    {
        foreach (var method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
        {
            if (method.GetMethodBody()?.GetILAsByteArray() is { } il)
            {
                Read(method, il, found);
            }
        }

        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            Collect(nested, found);
        }
    }

    private static void Read(MethodBase method, byte[] il, HashSet<Type> found)
    {
        var at = 0;
        while (at < il.Length)
        {
            short value = il[at++];
            if (value == 0xFE)
            {
                value = unchecked((short)(0xFE00 | il[at++]));
            }

            var code = Codes[value];
            if (code == OpCodes.Newobj)
            {
                var token = BitConverter.ToInt32(il, at);
                var genericTypes = method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null;
                var genericMethods = method.IsGenericMethod ? method.GetGenericArguments() : null;
                if (method.Module.ResolveMethod(token, genericTypes, genericMethods)?.DeclaringType is { } created)
                {
                    found.Add(created);
                }
            }

            at += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, at)),
                _ => 4,
            };
        }
    }
}
