using HtmlAgilityPack;
using Rotativa;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace PdfGenerator
{
    class Program
    {
        private static readonly HttpClient _client = new HttpClient();
        private static readonly string _baseDir = @"C:\Source\PdfGenerator\PdfGenerator\Rotativa\";
        private static readonly string _domain = "https://www.nfumutual.co.uk";
        private static readonly IDictionary<int, int> _views = new Dictionary<int, int>
        {
            { 4, 432 },
            { 3, 770 },
            { 2, 1442 },
            { 1, 1442 }
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
            var savePath = $@"{_baseDir}out\{contentId}_{workId}";
            Directory.CreateDirectory(savePath);

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

            var filePath = $@"{savePath}\{contentId}_{workId}";
            File.WriteAllBytes($"{filePath}.pdf", pdfBits);
            var pdfSaveTime = DateTime.Now.Subtract(startTime).TotalMilliseconds - pdfExecutionTime;
            Console.WriteLine($"Done saving PDF in {pdfSaveTime} milliseconds");


            File.WriteAllText($"{filePath}.html", mergedView);


            Console.WriteLine($"Finished! PDF saved in {filePath}.pdf from {filePath}.html");
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

            var resultBody = result.DocumentNode.SelectSingleNode("//body");
            resultBody.InnerHtml = "";
            resultBody.Attributes.RemoveAll();

            var pageBreak = "<div style=\"page-break-after:always;\">";
            var widthWrapFormat = "<div class=\"{0}\" style=\"width:{1}px;\">{2}</div>";
            for (var counter = 0; counter < pages.Length; counter++)
            {
                var page = pages[counter];
                var doc = new HtmlDocument();
                doc.LoadHtml(page.Html);
                var pageBody = doc.DocumentNode.SelectSingleNode("//body");
                var bodyCssClasses = pageBody.Attributes["class"].Value;
                var pageHtml = pageBody.InnerHtml;
                var wrappedBody = string.Format(widthWrapFormat, bodyCssClasses, page.Width, pageHtml);


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
            const string switchDisableJavascript = "--disable-javascript";
            //const string enableJavascriptWithDelay = "--javascript-delay 2000";
            //const string disableSmartShrinking = "--disable-smart-shrinking";

            var pdf = WkhtmltopdfDriver.ConvertHtml(_baseDir,
                $"{switchDisableJavascript}",
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
                if (link.Attributes["data-set"] != null) {
                    var bestImg = link.Attributes["data-set"].Value.Split(';').Last();
                    link.Attributes["src"].Value = bestImg;
                }
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
