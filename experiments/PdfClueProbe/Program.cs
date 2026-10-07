using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using BoothZipInspector;
using BoothZipInspector.Models;
using UglyToad.PdfPig;

// 数だけを出す。本文・名前は出力しない。
var roots = args;
var zips = roots.SelectMany(r => Directory.EnumerateFiles(r, "*.zip", SearchOption.AllDirectories)).Distinct().ToList();
Console.WriteLine($"zips={zips.Count}");
var itemRx = new Regex(@"booth\.pm/(?:[a-z]{2}(?:-[a-z]{2})?/)?items/(\d+)", RegexOptions.IgnoreCase);
var subRx = new Regex(@"([a-z0-9][a-z0-9-]*)\.booth\.pm", RegexOptions.IgnoreCase);
int zipsWithPdf=0, pdfs=0, textOk=0, noText=0, errs=0, enc=0, vn3=0, withItemUrl=0, withSub=0, withWork=0, withOwner=0, vn3Item=0, vn3Sub=0;
int zipBaselineResolved=0, zipPdfNew=0, zipPdfConsistent=0, zipPdfConflict=0, zipUnresolvedWithPdf=0, zipPdfSubOnly=0, zipUnresolvedWithPdfSub=0;
int zipNoBaselineAnyUrl=0;
long maxPdf=0; double totalMs=0; int timed=0; int maxPages=0; long totalPdfBytes=0;
int zoneFound=0;
var sw=new Stopwatch();
var dep = new HashSet<string>{"3087170"};
var vn3Rx = new Regex(@"VN3|Virtual\s*Native|vn3\.org", RegexOptions.IgnoreCase);
var workRx = new Regex(@"作品名|Title|Product", RegexOptions.IgnoreCase);
var ownerRx = new Regex(@"権利者|Rights\s*holder|Author|制作者|作者", RegexOptions.IgnoreCase);
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
foreach (var z in zips)
{
    List<string> baseIds;
    try { var r = ZipInspector.Inspect(z); baseIds = r.Clues.Where(c=>c.Kind==BoothClueKind.ItemUrl&&c.ItemId!=null).Select(c=>c.ItemId!).Where(i=>!dep.Contains(i)).Distinct().ToList(); }
    catch { baseIds = new(); }
    var zone = ZoneIdentifierReader.Read(z);
    bool zoneId = zone.Found && zone.BoothItemId != null; if (zoneId) zoneFound++;
    bool baseResolved = zoneId || baseIds.Count==1;
    if (baseResolved) zipBaselineResolved++;
    var pdfIds = new HashSet<string>(); bool anyPdf=false, anyPdfSub=false;
    try
    {
        using var zip = ZipFile.Open(z, ZipArchiveMode.Read, ZipNameEncoding.Instance);
        foreach (var e in zip.Entries.Where(e=>e.FullName.EndsWith(".pdf",StringComparison.OrdinalIgnoreCase)))
        {
            if (e.FullName.Contains("__MACOSX")||Path.GetFileName(e.FullName).StartsWith("._")) { Console.WriteLine("APPLEDOUBLE"); continue; } anyPdf=true; pdfs++; maxPdf=Math.Max(maxPdf,e.Length); totalPdfBytes+=e.Length;
            if (e.Length>64*1024*1024) { errs++; continue; }
            try
            {
                var bytes=new byte[(int)e.Length]; using (var s=e.Open()) s.ReadExactly(bytes);
                sw.Restart();
                using var pdf=PdfDocument.Open(bytes);
                maxPages=Math.Max(maxPages,pdf.NumberOfPages);
                var sb=new StringBuilder();
                foreach (var p in pdf.GetPages().Take(10)) { sb.Append(p.Text).Append('\n'); foreach(var l in p.GetHyperlinks()) sb.Append(l.Uri).Append('\n'); }
                sw.Stop(); totalMs+=sw.Elapsed.TotalMilliseconds; timed++;
                var t=sb.ToString();
                if (t.Trim().Length<20) { noText++; continue; }
                textOk++;
                bool isVn3=vn3Rx.IsMatch(t); if(isVn3) vn3++;
                var im=itemRx.Matches(t); if(im.Count>0){withItemUrl++; if(isVn3)vn3Item++; foreach(Match m in im) pdfIds.Add(m.Groups[1].Value);}
                var sm=subRx.Matches(t).Where(m=>!m.Groups[1].Value.StartsWith("s")||m.Groups[1].Value.Length>2).Where(m=>m.Groups[1].Value!="www").ToList();
                if(sm.Count>0){withSub++; anyPdfSub=true; if(isVn3)vn3Sub++;}
                if(workRx.IsMatch(t)) withWork++;
                if(ownerRx.IsMatch(t)) withOwner++;
            }
            catch (Exception ex) { Console.WriteLine("HEAD "+Convert.ToHexString(new ReadOnlySpan<byte>(new byte[0]))+" len="+e.Length/1000+"k"); Console.WriteLine("ERR "+ex.GetType().Name+" "+Regex.Replace(ex.Message,@"[^ -~]","?").Substring(0,Math.Min(60,ex.Message.Length))); if (ex.Message.Contains("ncrypt")||ex.Message.Contains("password")) enc++; errs++; }
        }
    } catch { }
    if (anyPdf) { zipsWithPdf++;
        if (!baseResolved) { zipUnresolvedWithPdf++; if (anyPdfSub) zipUnresolvedWithPdfSub++; }
        if (pdfIds.Count>0) {
            if (!baseResolved) { if (pdfIds.Count==1) zipPdfNew++; }
            else { var b = zoneId? zone.BoothItemId! : baseIds[0]; if (pdfIds.Contains(b)) zipPdfConsistent++; else zipPdfConflict++; }
        }
    }
}
Console.WriteLine($"zoneIdFound={zoneFound} baselineResolved={zipBaselineResolved}");
Console.WriteLine($"zipsWithPdf={zipsWithPdf} pdfs={pdfs} textOk={textOk} noText={noText} errs={errs} encrypted={enc}");
Console.WriteLine($"vn3={vn3} itemUrl={withItemUrl} (vn3&url={vn3Item}) subdomain={withSub} (vn3&sub={vn3Sub}) workField={withWork} ownerField={withOwner}");
Console.WriteLine($"zipsWithPdf unresolved by baseline={zipUnresolvedWithPdf}; of them PDF gives single item id={zipPdfNew}; PDF gives shop subdomain={zipUnresolvedWithPdfSub}");
Console.WriteLine($"baseline-resolved zips where PDF id agrees={zipPdfConsistent} disagrees={zipPdfConflict}");
Console.WriteLine($"maxPdfBytes={maxPdf} totalPdfBytes={totalPdfBytes} maxPages={maxPages} avgMs={(timed>0?totalMs/timed:0):F1} totalMs={totalMs:F0}");
