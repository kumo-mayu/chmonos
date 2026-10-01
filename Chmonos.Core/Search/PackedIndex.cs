namespace Chmonos.Core.Search;

/// <summary>
/// 「鍵 → 文字列の並び」の索引を、文字を1本の文字列に詰めて持つ（表記をまたぐ辞書の索引）。
///
/// **文字列を1つずつ持たないため。**控えを <c>Dictionary&lt;string, string[]&gt;</c> へ読むと、
/// 3つの索引で48.5万の鍵・約163万個の文字列になり、管理ヒープに約105MBが残った（読み込み225ms・割り当て188MB。2026-09-24 実測）。
/// 同じ表記（「指輪」など）が何百もの鍵に繰り返し出てくるので、異なる文字列は1回だけ置き、
/// 残りは番号（int）で指す。オブジェクトの頭（1つ20〜30バイト）と配列の数が消える。
///
/// 引いた結果は引くたびに文字列へ戻す（数個ずつなので安い。引くのは打った語1つにつき数回だけ）。
/// </summary>
internal sealed class PackedIndex
{
    private readonly string _text;
    private readonly int[] _starts;
    private readonly int[] _buckets;
    private readonly int[] _keyOfString;
    private readonly int[] _keys;
    private readonly int[] _formStarts;
    private readonly int[] _forms;

    private PackedIndex(string text, int[] starts, int[] buckets, int[] keyOfString, int[] keys, int[] formStarts, int[] forms)
    {
        _text = text;
        _starts = starts;
        _buckets = buckets;
        _keyOfString = keyOfString;
        _keys = keys;
        _formStarts = formStarts;
        _forms = forms;
    }

    /// <summary>鍵の数（試験・計測用）。</summary>
    public int KeyCount => _keys.Length;

    /// <summary>鍵の並び。無ければ空。</summary>
    public IReadOnlyList<string> Lookup(string key)
    {
        var id = Find(_text, _starts, _buckets, key);
        if (id < 0)
        {
            return [];
        }

        var k = _keyOfString[id];
        if (k < 0)
        {
            return [];
        }

        var start = _formStarts[k];
        var result = new string[_formStarts[k + 1] - start];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = StringAt(_forms[start + i]);
        }

        return result;
    }

    /// <summary>控えに書く順（読んだ・足した順）に、鍵と並びを返す。同じ鍵が2度あった物は後の方だけ。</summary>
    public IEnumerable<(string Key, IReadOnlyList<string> Forms)> Entries()
    {
        for (var k = 0; k < _keys.Length; k++)
        {
            if (_keyOfString[_keys[k]] != k)
            {
                continue;
            }

            var start = _formStarts[k];
            var forms = new string[_formStarts[k + 1] - start];
            for (var i = 0; i < forms.Length; i++)
            {
                forms[i] = StringAt(_forms[start + i]);
            }

            yield return (StringAt(_keys[k]), forms);
        }
    }

    private string StringAt(int id) => _text.Substring(_starts[id], _starts[id + 1] - _starts[id]);

    private static int Find(string text, int[] starts, int[] buckets, ReadOnlySpan<char> value)
    {
        var mask = buckets.Length - 1;
        for (var slot = string.GetHashCode(value) & mask; ; slot = (slot + 1) & mask)
        {
            var entry = buckets[slot];
            if (entry == 0)
            {
                return -1;
            }

            var id = entry - 1;
            if (text.AsSpan(starts[id], starts[id + 1] - starts[id]).SequenceEqual(value))
            {
                return id;
            }
        }
    }

    /// <summary>
    /// 組み立て。鍵を <see cref="BeginKey"/> で始め、並びを <see cref="AddForm"/> で足す。
    /// 同じ文字列はここで1つにまとめる（文字列を作らずに、文字の並びのまま比べる）。
    /// </summary>
    public sealed class Builder
    {
        private char[] _chars;
        private int _length;
        private readonly List<int> _starts;
        private int[] _buckets;
        private readonly List<int> _keyOfString;
        private readonly List<int> _keys;
        private readonly List<int> _formStarts;
        private readonly List<int> _forms;

        /// <param name="expectedKeys">
        /// 鍵のおよその数。置き場を倍々で広げると、捨てる途中の置き場で割り当てが残す物の3倍近くになった
        /// （英語の節で残り17MB・割り当て57MB）。控えの3つの節は1節が16〜17万鍵で、1鍵あたり異なる文字列が1.9〜2.9個・
        /// 並びが1.6〜3.0個・文字が12〜22字（2026-09-24 の控えで数えた）。多い側に合わせて取る。外れても広げるだけで、結果は変わらない
        /// </param>
        public Builder(int expectedKeys = 1024)
        {
            expectedKeys = Math.Max(expectedKeys, 16);
            _chars = new char[expectedKeys * 22];
            _starts = new List<int>(expectedKeys * 3) { 0 };
            _buckets = new int[BucketSize(expectedKeys * 3)];
            _keyOfString = new List<int>(expectedKeys * 3);
            _keys = new List<int>(expectedKeys);
            _formStarts = new List<int>(expectedKeys);
            _forms = new List<int>(expectedKeys * 3);
        }

        public void BeginKey(ReadOnlySpan<char> key)
        {
            var id = Intern(key);
            _keyOfString[id] = _keys.Count;
            _keys.Add(id);
            _formStarts.Add(_forms.Count);
        }

        public void AddForm(ReadOnlySpan<char> form) => _forms.Add(Intern(form));

        public PackedIndex Build()
        {
            var formStarts = new int[_formStarts.Count + 1];
            _formStarts.CopyTo(formStarts);
            formStarts[^1] = _forms.Count;

            // 詰める表は、文字列の数の2倍以上の2の冪（空きを半分残すと、探す手数が平均2回前後で済む）
            var buckets = new int[BucketSize(_starts.Count - 1)];
            var starts = _starts.ToArray();
            var text = new string(_chars, 0, _length);
            for (var id = 0; id < starts.Length - 1; id++)
            {
                var mask = buckets.Length - 1;
                var slot = string.GetHashCode(text.AsSpan(starts[id], starts[id + 1] - starts[id])) & mask;
                while (buckets[slot] != 0)
                {
                    slot = (slot + 1) & mask;
                }

                buckets[slot] = id + 1;
            }

            return new PackedIndex(text, starts, buckets, _keyOfString.ToArray(), _keys.ToArray(), formStarts, _forms.ToArray());
        }

        private static int BucketSize(int count)
        {
            var size = 16;
            while (size < count * 2)
            {
                size <<= 1;
            }

            return size;
        }

        private int Intern(ReadOnlySpan<char> value)
        {
            var count = _starts.Count - 1;
            if ((count + 1) * 2 > _buckets.Length)
            {
                Grow();
            }

            var mask = _buckets.Length - 1;
            var slot = string.GetHashCode(value) & mask;
            while (_buckets[slot] != 0)
            {
                var id = _buckets[slot] - 1;
                if (_chars.AsSpan(_starts[id], _starts[id + 1] - _starts[id]).SequenceEqual(value))
                {
                    return id;
                }

                slot = (slot + 1) & mask;
            }

            if (_length + value.Length > _chars.Length)
            {
                Array.Resize(ref _chars, Math.Max(_chars.Length * 2, _length + value.Length));
            }

            value.CopyTo(_chars.AsSpan(_length));
            _length += value.Length;
            _starts.Add(_length);
            _keyOfString.Add(-1);
            _buckets[slot] = count + 1;
            return count;
        }

        private void Grow()
        {
            var buckets = new int[_buckets.Length * 2];
            var mask = buckets.Length - 1;
            for (var id = 0; id < _starts.Count - 1; id++)
            {
                var slot = string.GetHashCode(_chars.AsSpan(_starts[id], _starts[id + 1] - _starts[id])) & mask;
                while (buckets[slot] != 0)
                {
                    slot = (slot + 1) & mask;
                }

                buckets[slot] = id + 1;
            }

            _buckets = buckets;
        }
    }
}
