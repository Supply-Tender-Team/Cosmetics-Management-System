using TenderSystem.Areas.Shared.Models.Reports;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace TenderSystem.Services.Reports;

// A visual narrative from the authorized snapshot. No AI HTML or provider/database calls.
public class AiReportPdfWriter
{
    public async Task<byte[]> WriteAsync(AiReportViewModel report, CancellationToken ct)
    {
        using var document = new PdfDocumentBuilder();
        var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var path = Path.Combine(fonts, "arial.ttf");
        var boldPath = Path.Combine(fonts, "arialbd.ttf");
        if (!File.Exists(path)) path = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
        if (!File.Exists(boldPath)) boldPath = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf";
        var font = File.Exists(path) ? document.AddTrueTypeFont(await File.ReadAllBytesAsync(path, ct)) : document.AddStandard14Font(Standard14Font.Helvetica);
        var bold = File.Exists(boldPath) ? document.AddTrueTypeFont(await File.ReadAllBytesAsync(boldPath, ct)) : document.AddStandard14Font(Standard14Font.HelveticaBold);
        var burgundy = (R:(byte)114,G:(byte)47,B:(byte)55);
        var ink = (R:(byte)62,G:(byte)47,B:(byte)49);
        var muted = (R:(byte)116,G:(byte)96,B:(byte)94);
        var cream = (R:(byte)250,G:(byte)245,B:(byte)240);
        var rose = (R:(byte)248,G:(byte)232,B:(byte)232);
        var mint = (R:(byte)232,G:(byte)242,B:(byte)233);
        var lavender = (R:(byte)239,G:(byte)234,B:(byte)248);
        var peach = (R:(byte)253,G:(byte)237,B:(byte)218);
        var green = (R:(byte)48,G:(byte)100,B:(byte)72);
        var purple = (R:(byte)100,G:(byte)74,B:(byte)133);
        var amber = (R:(byte)141,G:(byte)85,B:(byte)35);
        PdfPageBuilder page = null!;
        double y = 0;
        int number = 0;
        const double left = 36, width = 523, bottom = 75;

        void Box(double x, double top, double w, double h, (byte R, byte G, byte B) color)
        {
            page.SetTextAndFillColor(color.R,color.G,color.B);
            page.DrawRectangle(new PdfPoint(x,top-h),w,h,0,true);
        }
        void Label(string text, double x, double baseline, double size, (byte R, byte G, byte B) color, bool heavy = false)
        {
            page.SetTextAndFillColor(color.R,color.G,color.B);
            page.AddText(text,size,new PdfPoint(x,baseline),heavy?bold:font);
        }
        double Measure(string text, double size, bool heavy)
        {
            var letters=page.MeasureText(text,size,new PdfPoint(0,0),heavy?bold:font);
            return letters.Count==0?0:letters.Max(l=>l.BoundingBox.Right)-letters.Min(l=>l.BoundingBox.Left);
        }
        List<string> Wrap(string text,double size,double available,bool heavy=false)
        {
            var lines=new List<string>();
            foreach(var paragraph in text.Replace("\r","").Replace('\t',' ').Split('\n'))
            {
                var line="";
                foreach(var word in paragraph.Split(' ',StringSplitOptions.RemoveEmptyEntries))
                {
                    if(line.Length>0&&Measure(line+" "+word,size,heavy)>available){lines.Add(line);line="";}
                    foreach(var c in (line.Length==0?"":" ")+word)
                    {
                        if(line.Length>0&&Measure(line+c,size,heavy)>available){lines.Add(line);line="";}
                        line+=c;
                    }
                }
                lines.Add(line);
            }
            return lines;
        }
        void NewPage(string section)
        {
            ct.ThrowIfCancellationRequested();
            page=document.AddPage(595,842);number++;
            Box(0,842,595,90,burgundy);
            Box(0,752,595,4,(201,143,145));
            Label("MS. WAND",left,802,22,cream,true);
            Label("PROCUREMENT INTELLIGENCE",left,781,8,(230,182,178),true);
            Label("AI REPORT",446,807,11,cream,true);
            Label(report.GeneratedAt.ToString("dd MMM yyyy"),446,788,9,cream);
            Label(section.ToUpperInvariant(),left,726,8,muted,true);
            Label("MS.WAND",396,54,25,(237,226,224),true);
            page.SetStrokeColor(226,213,207);
            page.DrawLine(new PdfPoint(left,43),new PdfPoint(559,43),.6);
            Label("Generated "+report.GeneratedAt.ToString("dd MMM yyyy, HH:mm")+" UTC",left,28,7.5,muted);
            Label("Page "+number,518,28,8,burgundy,true);
            y=704;
        }
        void Heading(string title)
        {
            foreach(var line in Wrap(title,23,width,true))
            {
                if(y<bottom+50)NewPage("Report continued");
                Label(line,left,y,23,burgundy,true);y-=29;
            }
            y-=5;
        }
        void Paragraph(string text,double size=9)
        {
            foreach(var line in Wrap(text,size,width))
            {
                if(y<bottom+14)NewPage("Report continued");
                Label(line,left,y,size,muted);y-=size+5;
            }
            y-=8;
        }
        void Panel(string title,string text,(byte R,byte G,byte B) fill,(byte R,byte G,byte B) accent)
        {
            var lines=Wrap(text,10.5,width-40);var offset=0;var continued=false;
            do
            {
                var headings=Wrap(title+(continued?" / continued":""),12,width-40,true);
                var headerHeight=headings.Count*16+22;
                if(y-bottom<headerHeight+48)NewPage("Report continued");
                var take=Math.Min(lines.Count-offset,Math.Max(1,(int)((y-bottom-headerHeight-16)/15)));
                if(!continued&&lines.Count*15+headerHeight+13<=625&&take<lines.Count)
                {
                    NewPage("Report continued");
                    take=Math.Min(lines.Count,Math.Max(1,(int)((y-bottom-headerHeight-16)/15)));
                }
                var height=headerHeight+take*15+13;
                Box(left,y,width,height,fill);Box(left,y,4,height,accent);
                var baseline=y-24;
                foreach(var line in headings){Label(line,left+20,baseline,12,accent,true);baseline-=16;}
                baseline-=4;
                for(var i=0;i<take;i++){Label(lines[offset+i],left+20,baseline,10.5,ink);baseline-=15;}
                offset+=take;y-=height+14;
                if(offset<lines.Count){NewPage("Report continued");continued=true;}
            }while(offset<lines.Count);
        }
        void Figures()
        {
            var fills=new[]{rose,peach,mint,lavender};var accents=new[]{burgundy,amber,green,purple};
            for(var start=0;start<report.KeyFigures.Count;start+=4)
            {
                var group=report.KeyFigures.Skip(start).Take(4).ToList();var cardWidth=(width-30)/4;
                var labels=group.Select(f=>Wrap(f.Label,8,cardWidth-20,true)).ToList();
                var values=group.Select(f=>Wrap(f.Value,23,cardWidth-20,true)).ToList();
                var height=group.Select((_,i)=>26+labels[i].Count*12+values[i].Count*27).Max();
                if(height>625){foreach(var f in group)Panel(f.Label,f.Value,rose,burgundy);continue;}
                if(y-height<bottom)NewPage("Key figures continued");
                for(var i=0;i<group.Count;i++)
                {
                    var x=left+i*(cardWidth+10);
                    Box(x,y,cardWidth,height,fills[i]);Box(x,y,cardWidth,3,accents[i]);
                    var baseline=y-21;
                    foreach(var line in labels[i]){Label(line,x+10,baseline,8,accents[i],true);baseline-=12;}
                    baseline-=13;
                    foreach(var line in values[i]){Label(line,x+10,baseline,23,accents[i],true);baseline-=27;}
                }
                y-=height+18;
            }
        }
        NewPage("Executive brief");
        Heading(report.Title);
        Paragraph(report.Scope+" | "+report.RecordCount+" source records");
        Paragraph("Prepared for "+report.GeneratedBy+" | AI-generated analysis",8.5);
        if(report.KeyFigures.Count>0)Figures();
        Panel("01  /  The short version",report.Summary,rose,burgundy);
        if(report.Findings.Count>0)Panel("02  /  What stands out",string.Join("\n\n",report.Findings.Select((f,i)=>(i+1)+". "+f)),mint,green);
        if(report.NeedsAttention.Count>0)Panel("03  /  Worth a closer look",string.Join("\n\n",report.NeedsAttention.Select((f,i)=>(i+1)+". "+f)),peach,amber);
        if(report.Details.Count>0)
        {
            NewPage("Expanded report");
            Heading("The detail behind the brief");
            Paragraph("Full context for "+report.Title+". Included even when the on-screen details are collapsed.");
            for(var i=0;i<report.Details.Count;i++)Panel((i+1).ToString("00")+"  /  "+report.Details[i].Label,report.Details[i].Value,i%2==0?lavender:cream,i%2==0?purple:burgundy);
        }
        Paragraph("AI assists with interpretation. Review supporting records before making decisions.",8);
        return document.Build();
    }
}