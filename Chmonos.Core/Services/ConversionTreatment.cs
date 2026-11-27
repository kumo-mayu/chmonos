using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 商品を変換ツールのプロファイルとして扱うかの、実質の答え（<c>docs/spec/conversion.md</c>「データの形」）。
/// 人が決めた扱いと、自動の判定（中にプロファイルのファイルがあるか）を合わせた物。どちらで決まったかも残す
/// （商品ページの「今の扱い」は「手動で指定」か「自動の判定」かを言うので、扱うかだけに潰さない）。
/// </summary>
public enum ConversionTreatment
{
    /// <summary>自動に任せていて、中にプロファイルのファイルがある。</summary>
    AutoProfile,

    /// <summary>自動に任せていて、中にプロファイルのファイルが無い。</summary>
    AutoNotProfile,

    /// <summary>人がプロファイルとして扱うと決めた。</summary>
    ManualProfile,

    /// <summary>人がプロファイルとして扱わないと決めた（中にプロファイルのファイルがあっても）。</summary>
    ManualNotProfile,
}

/// <summary>
/// 実質の扱いを組む所。前は商品ページ（App の <c>ItemConversionEditor</c>）の文の中で組んでいた。外から使う API も同じ答えを返すため、
/// 画面と API が同じ関数を呼ぶよう Core に置く（ユーザ判断 2026-11-27「Core へ移しましょう」。外で組むとずれる）。
/// </summary>
public static class ConversionTreatments
{
    /// <summary>
    /// 人が決めた扱いが自動の判定に勝つ。<paramref name="hasProfileFiles"/> は手元のファイルの中にプロファイルがあるか
    /// （呼び手が <see cref="Of(ItemRecord, ConversionScan)"/> と同じ物を渡す。商品ページは同じ一覧を他にも使うので、読み直さずに渡す）。
    /// </summary>
    public static ConversionTreatment Of(ConversionSetting? setting, bool hasProfileFiles)
        => (setting?.Mode ?? ConversionMode.Auto) switch
        {
            ConversionMode.Profile => ConversionTreatment.ManualProfile,
            ConversionMode.NotProfile => ConversionTreatment.ManualNotProfile,
            _ => hasProfileFiles ? ConversionTreatment.AutoProfile : ConversionTreatment.AutoNotProfile,
        };

    /// <summary>
    /// 商品から直に組む。自動の判定は、入る先の一番上で先にふるってから（ふるいに落ちた商品は中身の控えを読まない）<see cref="ConversionScan.ProfilesOf"/> で見る。
    /// </summary>
    public static ConversionTreatment Of(ItemRecord item, ConversionScan scan)
        => Of(item.Local.Conversion, HasProfileFiles(item, scan));

    /// <summary>手元のファイルの中にプロファイルのファイルがあるか（自動の判定）。</summary>
    public static bool HasProfileFiles(ItemRecord item, ConversionScan scan)
        => ConversionScan.MayHoldProfiles(item) && scan.ProfilesOf(item).Count > 0;

    /// <summary>プロファイルとして扱うか（どちらで決まったかを問わない）。</summary>
    public static bool IsProfile(this ConversionTreatment treatment)
        => treatment is ConversionTreatment.AutoProfile or ConversionTreatment.ManualProfile;
}
