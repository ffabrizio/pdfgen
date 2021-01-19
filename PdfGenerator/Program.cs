using HtmlAgilityPack;
using Rotativa;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace PdfGenerator
{
    class Program
    {
        private static readonly HttpClient _client = new HttpClient();
        private static readonly string _domain = "https://www.nfumutual.co.uk";
        private static readonly IDictionary<int, int> _views = new Dictionary<int, int>
        {
            { 4, 430 },
            { 3, 768 },
            { 2, 1440 },
            { 1, 1440 }
        };

        static void Main(string[] args)
        {
            var program = new Program();
            program.Run(3194, 105536)
                .GetAwaiter()
                .GetResult();
        }

        private async Task Run(int contentId, int workId)
        {
            Console.WriteLine("Running...");

            var startTime = DateTime.Now;
            var results = new List<HtmlResult>();

            foreach (var key in _views.Keys)
            { 
                var view = await GetPage($"{_domain}/versionview/?id={contentId}&wId={workId}&mode={key}");
                results.Add(new HtmlResult { Width = _views[key], Html = view });
            }

            var fetchExecutionTime = DateTime.Now.Subtract(startTime).TotalMilliseconds;
            Console.WriteLine($"Done fetching HTML in {fetchExecutionTime} milliseconds");

            var mergedView = MergePages(results.ToArray());

            var mergeExecutionTime = DateTime.Now.Subtract(startTime).TotalMilliseconds - fetchExecutionTime;
            Console.WriteLine($"Done merging views in {mergeExecutionTime} milliseconds");

            var pdfBits = GeneratePdf(mergedView);
            var pdfExecutionTime = DateTime.Now.Subtract(startTime).TotalMilliseconds - mergeExecutionTime;
            Console.WriteLine($"Done generating PDF in {pdfExecutionTime} milliseconds");

            var filePath = $@"C:\Source\PdfGenerator\PdfGenerator\out\{Guid.NewGuid()}.pdf";
            File.WriteAllBytes(filePath, pdfBits);
            var pdfSaveTime = DateTime.Now.Subtract(startTime).TotalMilliseconds - pdfExecutionTime;
            Console.WriteLine($"Done saving PDF in {pdfSaveTime} milliseconds");


            Console.WriteLine($"Finished! PDF saved in {filePath}");
            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadLine();
        }

        private async Task<string> GetPage(string url)
        {
            var response = await _client.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            return content;
        }

        private string MergePages(HtmlResult[] pages)
        {
            if (pages.Length == 0)
            {
                return string.Empty;
            }

            var result = new HtmlDocument();
            result.LoadHtml(pages[0].Html);
            result.DocumentNode.SelectSingleNode("//body").InnerHtml = "";

            var pageBreak = "<div style=\"page-break-after:always;\">";
            var widthWrapFormat = "<div style=\"width:{0}px;\">{1}</div>";
            for (var counter = 0; counter < pages.Length; counter++)
            {
                var page = pages[counter];
                var doc = new HtmlDocument();
                doc.LoadHtml(page.Html);
                var pageBody = doc.DocumentNode.SelectSingleNode("//body").InnerHtml;
                var wrappedBody = string.Format(widthWrapFormat, page.Width, pageBody);


                result.DocumentNode.SelectSingleNode("//body").InnerHtml += wrappedBody;
                if (counter < pages.Length)
                {
                    result.DocumentNode.SelectSingleNode("//body").InnerHtml += pageBreak;
                }
            }

            return ProcessHtml(result);
        }

        private byte[] GeneratePdf(string html)
        {
            const string switchPageOffset = "--page-offset 0";
            const string switchDisableJavascript = "--disable-javascript";

            var pdf = WkhtmltopdfDriver.ConvertHtml(@"C:\Source\PdfGenerator\PdfGenerator\Rotativa\",
                $"{switchPageOffset} {switchDisableJavascript}",
                html);

            return pdf;
        }

        private string ProcessHtml(HtmlDocument result)
        {
            // Css
            foreach (var link in result.DocumentNode.SelectNodes("//link"))
            {
                if (!link.Attributes["href"].Value.StartsWith("http"))
                {
                    link.Attributes["href"].Value = _domain + link.Attributes["href"].Value;
                }
            }

            // Images
            foreach (var link in result.DocumentNode.SelectNodes("//img"))
            {
                if (!link.Attributes["src"].Value.StartsWith("http"))
                {
                    link.Attributes["src"].Value = _domain + link.Attributes["src"].Value;
                }
            }

            // Anchors
            foreach (var link in result.DocumentNode.SelectNodes("//a"))
            {
                if (!link.Attributes["href"].Value.StartsWith("http"))
                {
                    link.Attributes["href"].Value = _domain + link.Attributes["href"].Value;
                }
            }

            // Javascripts
            // Images
            foreach (var link in result.DocumentNode.SelectNodes("//script"))
            {
                if (link.Attributes.Contains("src"))
                {
                    if (!link.Attributes["src"].Value.StartsWith("http"))
                    {
                        link.Attributes["src"].Value = _domain + link.Attributes["src"].Value;
                    }
                    else
                    {
                        link.Remove();
                    }
                }

            }

            return result.DocumentNode.OuterHtml;
        }
    }

    public class HtmlResult
    {
        public int Width { get; set; }
        public string Html { get; set; }
    }
}
