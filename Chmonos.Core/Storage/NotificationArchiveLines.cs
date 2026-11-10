using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;

namespace Chmonos.Core.Storage;

/// <summary>
/// 保管庫の月のファイルを1回読んだ様子。速い読みのあと、呼び手は <see cref="Intact"/> を見て、
/// 壊れていたら結果を捨てて正確な読み（<see cref="NotificationArchiveLines.PreciseAsync"/>）で頭から読み直す。
/// </summary>
internal sealed class ArchiveLineScan
{
    /// <summary>速い読みで、gzip の誤り（CRC の不一致・deflate の誤り）が出た、最後の行が改行で終わらなかった、またはファイルの終わりまで引かなかった。</summary>
    public bool Broken { get; set; }

    /// <summary>速い読みで通ったバイト列の中の、このアプリが書くメンバーの頭（<c>1F 8B 08 00</c> と時刻 0）の数。</summary>
    public int Headers { get; set; }

    /// <summary>出てきた版の行の数（呼び手が数える）。</summary>
    public int VersionLines { get; set; }

    /// <summary>正確な読みで、読めずに飛ばした所の数（続けて読めなかった候補はまとめて1か所）。</summary>
    public int DamagedBlocks { get; set; }

    /// <summary>
    /// 速い読みが全部を読み切ったか。GZipStream は、メンバーの後ろが gzip の頭でないと（頭の1バイトが壊れた等）
    /// 黙って読み終える（.NET 10 で 2026-11-10 に確かめた）。例外が無いだけでは後ろを落としたかが分からないので、
    /// ファイルの終わりまで引いたこと（引いたバッファの外の後ろ）と、版の行（メンバーごとに1行目）の数とメンバーの頭の数が合うこと
    /// （引いたバッファの中の後ろ）も見る。
    /// 手で作った gzip（名前や時刻の入った頭）・版の行がメンバーの途中にある物は合わず、正確な読みに回る（遅いが正しい）。
    /// </summary>
    public bool Intact => !Broken && VersionLines == Headers;
}

/// <summary>
/// 保管庫の月のファイルを行に分けて流す。ファイルは知らせ1件ずつの gzip のメンバー（かたまり）と、
/// 半分に書き直したときの大きなメンバーが続いた物（ユーザ判断 2026-11-10「壊れたかたまりだけ飛ばす」）。
///
/// - **速い読み**：GZipStream 1本で全部を流す。壊れていない月はこれだけで読み終え、前と同じ速さ。
/// - **正確な読み**：止まったときだけ。ファイルからメンバーの頭の候補を拾い、候補から次の候補までを1メンバーとして解く。
///   GZipStream は先読みするので止まった位置は分からず、メンバーの終わりも教えない。そのため、解いた中身の CRC32 と長さが
///   ファイルの中の8バイト（gzip の尾）と合う所をメンバーの終わりとする。
///   知らせ1件のメンバーは確かめ終わるまで行を出さず、壊れていれば丸ごと捨てる（その1件だけ欠ける）。
///   2件以上のメンバー（書き直した大きいメンバーなど）は流す（全部を載せない）。途中で壊れていれば、
///   それまでに出した最後まで読めた行は生かし、途中で切れた最後の行は捨てる（後ろは gzip の仕組み上救えない）。
///   どちらも、その後ろに別のメンバーがあれば、そこから読み続ける。
/// </summary>
internal static class NotificationArchiveLines
{
    /// <summary>
    /// 確かめ終わるまで溜める中身の上限（解いた後の大きさ）。知らせ1件のメンバーは数百バイト〜数 KB で、ふつうは届かない。
    /// 1行がとても大きい作り物に近い物でも、これを超えたら流す（50MB の月を全部載せると作業セットが 500MB を超えた）。
    /// </summary>
    private const int BufferedLimit = 1 << 20;

    /// <summary>
    /// 候補で区切ったメンバーが途中で切れていたとき、次の候補まで伸ばす回数。偽の頭（中身にたまたま現れた <c>1F 8B 08</c>）は
    /// 50MB に 1 つあるか無いかなので、数回で足りる。壊れたメンバーで伸ばし続けて遅くならないよう抑える。
    /// </summary>
    private const int MaxExtensions = 4;

    private const int ReadBuffer = 64 * 1024;

    /// <summary>メンバーの尾を区切りの中で探すときに、読む大きさの上限（これを超えるメンバーは区切りの終わりだけを見る）。作業セットを増やしすぎない値。</summary>
    private const int SearchLimit = 4 << 20;

    /// <summary>速い読み。最後に <see cref="ArchiveLineScan.Broken"/> と <see cref="ArchiveLineScan.Headers"/> を入れる。</summary>
    public static async IAsyncEnumerable<string> FastAsync(string path, ArchiveLineScan scan, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var file = JsonStore.OpenShared(path);
        await using var counting = new HeaderCountingStream(file);
        await using var gzip = new GZipStream(counting, CompressionMode.Decompress);
        var splitter = new LineSplitter();
        var lines = new List<string>();
        var buffer = ArrayPool<byte>.Shared.Rent(ReadBuffer);
        try
        {
            while (true)
            {
                int read;
                try
                {
                    read = await gzip.ReadAsync(buffer.AsMemory(0, ReadBuffer), cancellationToken);
                }
                catch (InvalidDataException)
                {
                    scan.Broken = true;
                    yield break;
                }

                if (read == 0)
                {
                    break;
                }

                splitter.Feed(buffer.AsSpan(0, read), lines);
                foreach (var line in lines)
                {
                    yield return line;
                }

                lines.Clear();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        // 改行で終わらない最後の行は、切れたメンバーの書きかけ（このアプリは行を必ず改行で終える）。
        // GZipStream は、メンバーの後ろが gzip の頭でないと、その先を元のストリームから引かずに読み終える。
        // 引いたバッファ（8KB）の中に次の頭が無いと頭の数では気づけないので、ファイルの終わりまで引いたかも見る
        scan.Broken = splitter.HasPartial || counting.Pulled < file.Length;
        scan.Headers = counting.Headers;
    }

    /// <summary>正確な読み（速い読みが壊れていたときだけ）。読めなかった所の数を <see cref="ArchiveLineScan.DamagedBlocks"/> に入れる。</summary>
    public static async IAsyncEnumerable<string> PreciseAsync(string path, ArchiveLineScan scan, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var file = JsonStore.OpenShared(path);
        var length = file.Length;
        var candidates = await CandidatesAsync(file, cancellationToken);
        var inDamage = false;

        void Damaged()
        {
            if (!inDamage)
            {
                scan.DamagedBlocks++;
                inDamage = true;
            }
        }

        // 最初の候補より前に何かあれば、頭の壊れたメンバー（gzip でない月もここ）
        if (length > 0 && (candidates.Count == 0 || candidates[0] > 0))
        {
            Damaged();
        }

        var buffer = ArrayPool<byte>.Shared.Rent(ReadBuffer);
        try
        {
            var index = 0;
            while (index < candidates.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var start = candidates[index];

                // 流した行の数。伸ばして解き直すときは、同じ入力から同じ中身が出るので、この数だけ読み飛ばす
                var flushed = 0;
                var streaming = false;
                long? memberEnd = null;
                List<string>? keep = null;

                for (var extension = 0; ; extension++)
                {
                    var boundIndex = index + 1 + extension;
                    var bound = boundIndex < candidates.Count ? candidates[boundIndex] : length;
                    var pending = new List<string>();
                    var pendingChars = 0L;
                    var produced = 0;
                    var output = 0L;
                    var crc = 0u;
                    var corrupt = false;
                    var splitter = new LineSplitter();
                    var lines = new List<string>();

                    await using (var slice = new SliceStream(file, start, bound))
                    await using (var gzip = new GZipStream(slice, CompressionMode.Decompress))
                    {
                        while (true)
                        {
                            int read;
                            try
                            {
                                read = await gzip.ReadAsync(buffer.AsMemory(0, ReadBuffer), cancellationToken);
                            }
                            catch (InvalidDataException)
                            {
                                corrupt = true;
                                break;
                            }

                            if (read == 0)
                            {
                                break;
                            }

                            output += read;
                            crc = Crc32.Append(crc, buffer.AsSpan(0, read));
                            splitter.Feed(buffer.AsSpan(0, read), lines);
                            foreach (var line in lines)
                            {
                                if (++produced <= flushed)
                                {
                                    continue;
                                }

                                pending.Add(line);
                                pendingChars += line.Length;
                            }

                            lines.Clear();
                            if (streaming || pending.Count > 2 || pendingChars > BufferedLimit)
                            {
                                // 知らせ2件以上のメンバー（版の行と2行）：確かめ終わるのを待たずに流す。
                                // 壊れていても最後まで読めた行は生かすので、待つ理由が無い（大きいメンバーを全部載せない）
                                streaming = true;
                                foreach (var line in pending)
                                {
                                    yield return line;
                                }

                                flushed += pending.Count;
                                pending.Clear();
                                pendingChars = 0;
                            }
                        }
                    }

                    if (!corrupt)
                    {
                        memberEnd = await FindTrailerAsync(file, start, bound, crc, output, cancellationToken);
                        if (memberEnd is not null)
                        {
                            if (splitter.HasPartial)
                            {
                                // 確かめの通ったメンバーの最後の行（改行が無いのは手で作った物だけ）
                                pending.Add(splitter.TakePartial());
                            }

                            keep = pending;
                            break;
                        }

                        // 区切りの中で終わらなかった：偽の頭で切ったか、メンバーが途中で切れている。次の候補まで伸ばして解き直す
                        if (boundIndex < candidates.Count && extension < MaxExtensions)
                        {
                            continue;
                        }
                    }

                    // 壊れたメンバー。流していれば最後まで読めた行は生かし（途中で切れた最後の行は捨てる）、
                    // 溜めていただけ（知らせ1件のメンバー）なら丸ごと捨てる
                    keep = streaming ? pending : null;
                    break;
                }

                foreach (var line in keep ?? [])
                {
                    yield return line;
                }

                if (memberEnd is { } end)
                {
                    inDamage = false;
                    var found = candidates.BinarySearch(end);
                    index = found >= 0 ? found : ~found;

                    // メンバーの終わりと次の頭の間に何かある（頭の壊れたメンバー・後ろに付いたごみ）
                    if (end < (index < candidates.Count ? candidates[index] : length))
                    {
                        Damaged();
                    }
                }
                else
                {
                    Damaged();
                    index++;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>gzip のメンバーの頭の候補（<c>1F 8B 08</c> と、予約のビットが 0 のフラグ）の位置を、前から順に。</summary>
    private static async Task<List<long>> CandidatesAsync(FileStream file, CancellationToken cancellationToken)
    {
        var candidates = new List<long>();
        var buffer = ArrayPool<byte>.Shared.Rent(ReadBuffer + 3);
        try
        {
            file.Position = 0;
            var carry = 0;
            long offset = 0;
            while (true)
            {
                var read = await file.ReadAsync(buffer.AsMemory(carry, ReadBuffer), cancellationToken);
                var available = carry + read;
                var span = buffer.AsSpan(0, available);

                // 頭は4バイト見るので、終わりの3バイトは次の読みと続けて見る（ファイルの終わりなら見なくてよい：4バイトの頭だけのメンバーは無い）
                var limit = read == 0 ? available : Math.Max(0, available - 3);
                for (var position = 0; position < limit;)
                {
                    var found = span[position..limit].IndexOf((byte)0x1F);
                    if (found < 0)
                    {
                        break;
                    }

                    position += found;
                    if (position + 3 < available && span[position + 1] == 0x8B && span[position + 2] == 0x08 && (span[position + 3] & 0xE0) == 0)
                    {
                        candidates.Add(offset + position);
                    }

                    position++;
                }

                if (read == 0)
                {
                    return candidates;
                }

                carry = available - limit;
                span[limit..available].CopyTo(buffer);
                offset += limit;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// メンバーの終わり：解いた中身の CRC32 と長さ（gzip の尾の8バイト）が並ぶ所を、区切りの中で探す。
    /// 区切りの終わりだけを見ると、後ろのメンバーの頭が壊れて区切りが伸びたとき、良いメンバーまで壊れたと見てしまう。
    /// 圧縮した大きさは中身＋格納の見出し（64KB ごとに5バイト）＋頭と尾を超えないので、そこまでを読む。
    /// それが <see cref="SearchLimit"/> を超える大きいメンバーは、全部を載せないよう区切りの終わりの8バイトだけを見る。
    /// </summary>
    private static async Task<long?> FindTrailerAsync(FileStream file, long start, long bound, uint crc, long output, CancellationToken cancellationToken)
    {
        // 頭と尾（18バイト）に、手で作った gzip の名前などの分を少し足す
        var most = 18 + output + 5 * (output / 65535 + 1) + 256;
        if (bound - start < 18)
        {
            return null;
        }

        if (most > SearchLimit)
        {
            var end = new byte[8];
            file.Position = bound - 8;
            await file.ReadExactlyAsync(end, cancellationToken);
            return IndexOfTrailer(end, crc, output, from: 0) == 0 ? bound : null;
        }

        var size = (int)Math.Min(bound - start, most);

        var bytes = ArrayPool<byte>.Shared.Rent(size);
        try
        {
            file.Position = start;
            await file.ReadExactlyAsync(bytes.AsMemory(0, size), cancellationToken);
            // 頭（10バイト）と空の deflate（2バイト）より前に尾は来ない
            var found = IndexOfTrailer(bytes.AsSpan(0, size), crc, output, from: 12);
            return found < 0 ? null : start + found + 8;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    private static int IndexOfTrailer(ReadOnlySpan<byte> bytes, uint crc, long output, int from)
    {
        Span<byte> trailer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(trailer, crc);
        BinaryPrimitives.WriteUInt32LittleEndian(trailer[4..], unchecked((uint)output));
        var found = bytes[from..].IndexOf(trailer);
        return found < 0 ? -1 : from + found;
    }

    /// <summary>解いたバイト列を改行で行に分ける。改行で終わった行だけを出し、終わっていない残りは持っておく。</summary>
    private sealed class LineSplitter
    {
        private static readonly UTF8Encoding Utf8 = new(false);

        private byte[] _partial = new byte[256];
        private int _partialLength;
        private bool _first = true;

        public bool HasPartial => _partialLength > 0;

        public void Feed(ReadOnlySpan<byte> data, List<string> lines)
        {
            while (!data.IsEmpty)
            {
                var newline = data.IndexOf((byte)'\n');
                if (newline < 0)
                {
                    Append(data);
                    return;
                }

                if (_partialLength == 0)
                {
                    lines.Add(Decode(data[..newline]));
                }
                else
                {
                    Append(data[..newline]);
                    lines.Add(Decode(_partial.AsSpan(0, _partialLength)));
                    _partialLength = 0;
                }

                data = data[(newline + 1)..];
            }
        }

        public string TakePartial()
        {
            var line = Decode(_partial.AsSpan(0, _partialLength));
            _partialLength = 0;
            return line;
        }

        private void Append(ReadOnlySpan<byte> data)
        {
            if (_partialLength + data.Length > _partial.Length)
            {
                Array.Resize(ref _partial, Math.Max(_partial.Length * 2, _partialLength + data.Length));
            }

            data.CopyTo(_partial.AsSpan(_partialLength));
            _partialLength += data.Length;
        }

        /// <summary>StreamReader と同じく、行の終わりの CR を外し、先頭の BOM を読み飛ばす（手で作った物のため）。</summary>
        private string Decode(ReadOnlySpan<byte> line)
        {
            if (_first)
            {
                _first = false;
                if (line.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
                {
                    line = line[3..];
                }
            }

            if (!line.IsEmpty && line[^1] == (byte)'\r')
            {
                line = line[..^1];
            }

            return Utf8.GetString(line);
        }
    }

    /// <summary>
    /// 読んだバイト列の中の、このアプリが書くメンバーの頭（<c>1F 8B 08 00</c> と時刻 0 の8バイト）を数える。
    /// 速い読みが全部を読み切ったかを確かめるため。8バイトで見ると、圧縮した中身に偶然現れることはまず無い。
    /// </summary>
    private sealed class HeaderCountingStream(Stream inner) : Stream
    {
        private static readonly byte[] Header = [0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00];

        private readonly byte[] _seam = new byte[(Header.Length - 1) * 2];
        private int _tail;

        public int Headers { get; private set; }

        /// <summary>元のストリームから引いたバイト数。</summary>
        public long Pulled { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Count(buffer.AsSpan(offset, inner.Read(buffer, offset, count)));

        public override int Read(Span<byte> buffer) => Count(buffer[..inner.Read(buffer)]);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            return Count(buffer.Span[..read]);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        private int Count(ReadOnlySpan<byte> read)
        {
            if (read.IsEmpty)
            {
                return 0;
            }

            Pulled += read.Length;

            // 前の読みとの継ぎ目にまたがる頭（前の終わりの7バイトまで＋今の頭の7バイトまで）
            var head = Math.Min(read.Length, Header.Length - 1);
            read[..head].CopyTo(_seam.AsSpan(_tail));
            if (_seam.AsSpan(0, _tail + head).IndexOf(Header) is >= 0)
            {
                Headers++;
            }

            for (var span = read; ;)
            {
                var found = span.IndexOf(Header);
                if (found < 0)
                {
                    break;
                }

                Headers++;
                span = span[(found + 1)..];
            }

            // 次の継ぎ目に使う終わりの7バイト（今の読みが短ければ、前の残りと続けて持つ）
            var joined = _seam.AsSpan(0, _tail + head);
            if (read.Length >= Header.Length - 1)
            {
                read[^(Header.Length - 1)..].CopyTo(_seam);
                _tail = Header.Length - 1;
            }
            else
            {
                var keep = Math.Min(joined.Length, Header.Length - 1);
                joined[^keep..].CopyTo(_seam);
                _tail = keep;
            }

            return read.Length;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            // 元のファイルは呼び手が閉じる
        }
    }

    /// <summary>ファイルの [start, end) だけを読ませる（GZipStream が区切りの先まで解かないように）。読むたびに位置を合わせる。</summary>
    private sealed class SliceStream(FileStream file, long start, long end) : Stream
    {
        private long _position = start;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var size = (int)Math.Min(buffer.Length, end - _position);
            if (size <= 0)
            {
                return 0;
            }

            file.Position = _position;
            var read = file.Read(buffer[..size]);
            _position += read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var size = (int)Math.Min(buffer.Length, end - _position);
            if (size <= 0)
            {
                return 0;
            }

            file.Position = _position;
            var read = await file.ReadAsync(buffer[..size], cancellationToken);
            _position += read;
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            // 元のファイルは呼び手が閉じる
        }
    }

    /// <summary>gzip の尾と同じ CRC32（多項式 0xEDB88320）。正確な読みで、溜めた小さいメンバーにだけ使う。</summary>
    private static class Crc32
    {
        private static readonly uint[] Table = Build();

        public static uint Append(uint crc, ReadOnlySpan<byte> data)
        {
            crc = ~crc;
            foreach (var value in data)
            {
                crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
            }

            return ~crc;
        }

        private static uint[] Build()
        {
            var table = new uint[256];
            for (var n = 0u; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                }

                table[n] = c;
            }

            return table;
        }
    }
}
