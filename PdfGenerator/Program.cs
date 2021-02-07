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
        private static readonly string _baseDir = $"{Environment.CurrentDirectory.Split(new[] { "bin" }, StringSplitOptions.RemoveEmptyEntries)[0]}Rotativa\\";
        private static readonly string _domain = "https://www.nfumutual.co.uk";
        private static readonly IDictionary<int, int> _views = new Dictionary<int, int>
        {
            { 4, 430 },
            { 3, 768 },
            { 2, 1440 },
            { 1, 1200 }
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

            //var pdfBits = GeneratePdf(contentId, workId);

            foreach (var key in _views.Keys)
            {
                var view = await GetPage($"{_domain}/versionview/?id={contentId}&wId={workId}&mode={key}");
                results.Add(new HtmlResult { Width = _views[key], Html = view });
            }

            //var pdfBits = GeneratePdf(results);

            var fetchExecutionTime = DateTime.Now.Subtract(startTime).TotalMilliseconds;
            Console.WriteLine($"Done fetching HTML in {fetchExecutionTime} milliseconds");

            var mergedView = MergePages(results.ToArray());

            var mergeExecutionTime = DateTime.Now.Subtract(startTime).TotalMilliseconds - fetchExecutionTime;
            Console.WriteLine($"Done merging views in {mergeExecutionTime} milliseconds");

            var pdfBits = GeneratePdf(new[] { mergedView });
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
            var widthWrapFormat = "<div class=\"{0}\" style=\"width:{1}px;max-width:{1}px\">{2}</div>";
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

        private byte[] GeneratePdf(int contentId, int workId)
        {
            string switches = "--disable-javascript ";

            foreach (var kv in _views)
            {
                var url = $"{_domain}/versionview/?id={contentId}&wId={workId}&mode={kv.Key}";
                switches += url + " ";
                switches += $"--viewport-size {kv.Value}x800 --zoom 0.65 ";
            }

            return WkhtmltopdfDriver.Convert(_baseDir, switches);
        }

        private byte[] GeneratePdf(IEnumerable<HtmlResult> results)
        {
            string switches = "--disable-javascript ";
            foreach (var result in results)
            {
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(result.Html);
                htmlDoc.LoadHtml(ProcessHtml(htmlDoc));
                
                // Body wrap
                var widthWrapFormat = "<div style=\"width:{0}px;max-width:{0}px\">{1}</div>";
                var body = htmlDoc.DocumentNode.SelectSingleNode("//body");
                if (body != null)
                {
                    body.InnerHtml = string.Format(widthWrapFormat, result.Width, body.InnerHtml);
                    body.Attributes.Add("style", $"width:{result.Width}px;max-width:{result.Width}px;");
                }

                var viewport = htmlDoc.DocumentNode.SelectNodes("//meta").FirstOrDefault(m => m.Attributes["name"]?.Value == "viewport");
                if (viewport != null)
                {
                    viewport.Remove();
                    viewport.Attributes["content"].Value = $"width={result.Width},initial-scale=1";
                }

                File.WriteAllText($"{_baseDir + result.Width}.html", ProcessHtml(htmlDoc));
                switches += $"{_baseDir + result.Width}.html --viewport-size {result.Width}x800";
            }

            var pdf = WkhtmltopdfDriver.Convert(_baseDir, switches);
            foreach (var result in results)
            {
                File.Delete($"{_baseDir + result.Width}.html");
            }
            return pdf;
        }

        private byte[] GeneratePdf(string[] html)
        {

            //const string switches = "--disable-javascript --viewport-size 1000x800 --disable-smart-shrinking --zoom 0.65";
            const string switches = "--disable-javascript";
            //const string switches = "--javascript-delay 2000";
            //const string switches = "--disable-smart-shrinking";

            var pdf = WkhtmltopdfDriver.ConvertHtml(_baseDir,
                $"{switches}",
                html[0]);

            return pdf;
        }

        private string ProcessHtml(HtmlDocument result)
        {
            // Css
            var links = result.DocumentNode.SelectNodes("//link");
            if (links != null)
            {
                foreach (var link in links)
                {
                    if (!link.Attributes["href"].Value.StartsWith("http"))
                    {
                        link.Attributes["href"].Value = _domain + link.Attributes["href"].Value;
                    }
                }
            }
            // Images
            var imgs = result.DocumentNode.SelectNodes("//img");
            if (imgs != null)
            {
                foreach (var link in imgs)
                {
                    if (link.Attributes["data-set"] != null)
                    {
                        var bestImg = link.Attributes["data-set"].Value.Split(';').Last();
                        link.Attributes["src"].Value = bestImg;
                    }
                    if (!link.Attributes["src"].Value.StartsWith("http"))
                    {
                        link.Attributes["src"].Value = _domain + link.Attributes["src"].Value;
                    }
                }
            }

            // Anchors
            var anchors = result.DocumentNode.SelectNodes("//a");
            if (anchors != null)
            {
                foreach (var link in anchors)
                {
                    if (!link.Attributes["href"].Value.StartsWith("http"))
                    {
                        link.Attributes["href"].Value = _domain + link.Attributes["href"].Value;
                    }
                }
            }

            // Javascripts
            var js = result.DocumentNode.SelectNodes("//script");
            if (js != null)
            {
                foreach (var link in js)
                {
                    if (link.Attributes.Contains("src"))
                    {
                        if (!link.Attributes["src"].Value.StartsWith("http") &&
                            !link.Attributes["src"].Value.StartsWith(_domain))
                        {
                            link.Attributes["src"].Value = _domain + link.Attributes["src"].Value;
                        }
                        else
                        {
                            link.Remove();
                        }
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
