using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace RoomBroomChainPlugin.Iiko
{
    public class IikoSupplier
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Inn { get; set; }
    }

    /// <summary>
    /// Минимальный клиент iiko Server REST по образцу Python IikoRestoClient и старого метода Suppliers_all().
    /// Авторизация: GET /api/auth или /resto/api/auth → key, далее /resto/api/suppliers?key=...
    /// </summary>
    internal class IikoConfig
    {
        public string BaseUrl { get; set; }
        public string Login { get; set; }
        public string PasswordSha1 { get; set; }
    }

    public class IikoRestoClient
    {
        private string _key;
        private static IikoConfig _config;

        private static string GetEnv(string name)
        {
            return (Environment.GetEnvironmentVariable(name) ?? "").Trim();
        }

        private static IikoConfig LoadConfig()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var assemblyDir = Path.GetDirectoryName(typeof(IikoRestoClient).Assembly.Location) ?? baseDir;

                var candidates = new[]
                {
                    Path.Combine(assemblyDir, "RoomBroom.iiko.config.json"),
                    Path.Combine(baseDir, "RoomBroom.iiko.config.json"),
                    Path.Combine(baseDir, "Plugins", "RoomBroom.iiko.config.json"),
                    Path.Combine(baseDir, "Plugins", "RoomBroom", "RoomBroom.iiko.config.json"),
                    Path.Combine(baseDir, "RoomBroom", "RoomBroom.iiko.config.json")
                };

                string json = null;
                string usedPath = null;
                foreach (var p in candidates)
                {
                    if (File.Exists(p))
                    {
                        json = File.ReadAllText(p, Encoding.UTF8);
                        usedPath = p;
                        break;
                    }
                }

                if (string.IsNullOrWhiteSpace(json))
                    return null;

                var jo = JObject.Parse(json);
                var cfg = new IikoConfig
                {
                    BaseUrl = (string)jo["baseUrl"],
                    Login = (string)jo["login"],
                    PasswordSha1 = (string)jo["passwordSha1"]
                };

                IikoLog.Write("Loaded config from " + usedPath + " baseUrl=" + (cfg.BaseUrl ?? "") + " login=" + (cfg.Login ?? ""));
                return cfg;
            }
            catch
            {
                IikoLog.Write("Failed to load iiko config");
                return null;
            }
        }

        private static IikoConfig Config
        {
            get
            {
                if (_config != null) return _config;
                _config = LoadConfig();
                return _config;
            }
        }

        private async Task<string> GetKeyAsync()
        {
            if (!string.IsNullOrEmpty(_key))
                return _key;

            var cfg = Config;
            var baseUrl = (cfg?.BaseUrl ?? GetEnv("IIKO_BASE_URL"))?.TrimEnd('/') ?? "";
            var login = cfg?.Login ?? GetEnv("IIKO_LOGIN");
            var sha1 = (cfg?.PasswordSha1 ?? GetEnv("IIKO_PASS_SHA1") ?? "").ToLowerInvariant();

            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(login) || string.IsNullOrEmpty(sha1))
            {
                IikoLog.Write("Missing iiko config/baseUrl/login/pass");
                throw new InvalidOperationException("Не указаны настройки подключения к iiko (IIKO_BASE_URL, IIKO_LOGIN, IIKO_PASS_SHA1).");
            }

            foreach (var path in new[] { "/api/auth", "/resto/api/auth" })
            {
                var url = baseUrl + path + "?login=" + Uri.EscapeDataString(login) + "&pass=" +
                          Uri.EscapeDataString(sha1);
                IikoLog.Write("Auth GET " + url);
                try
                {
                    var text = await PerformSimpleGetAsync(url).ConfigureAwait(false);
                    IikoLog.Write("Auth response: " + (text ?? "<null>"));
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        _key = text.Trim();
                        IikoLog.Write("Auth key acquired");
                        return _key;
                    }
                }
                catch (WebException we)
                {
                    var resp = we.Response as HttpWebResponse;
                    var code = resp != null ? ((int)resp.StatusCode).ToString() : "no-status";
                    IikoLog.Write("Auth WebException at " + path + " status=" + code + " msg=" + we.Message);
                    // Если эндпоинт не найден (404) — пробуем следующий путь, как в Python-клиенте
                    if (resp != null && resp.StatusCode == HttpStatusCode.NotFound)
                        continue;
                    throw;
                }
            }

            IikoLog.Write("Auth failed: empty key");
            throw new InvalidOperationException("Не удалось выполнить авторизацию в iiko (auth key пустой).");
        }

        private static async Task<string> PerformSimpleGetAsync(string url)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Timeout = 60000;

            try
            {
                using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
                {
                    var s = await reader.ReadToEndAsync().ConfigureAwait(false);
                    return s;
                }
            }
            catch (WebException we)
            {
                var resp = we.Response as HttpWebResponse;
                var code = resp != null ? ((int)resp.StatusCode).ToString() : "no-status";
                IikoLog.Write("HTTP error " + code + " at " + url + " msg=" + we.Message);
                throw;
            }
        }

        private async Task<string> GetAsync(string path)
        {
            var key = await GetKeyAsync().ConfigureAwait(false);
            var cfg = Config;
            var baseUrl = (cfg?.BaseUrl ?? GetEnv("IIKO_BASE_URL"))?.TrimEnd('/') ?? "";
            if (path.StartsWith("/"))
                path = path.Substring(1);

            var sb = new StringBuilder();
            sb.Append(baseUrl).Append("/resto/").Append(path);
            var sep = path.Contains("?") ? "&" : "?";
            sb.Append(sep).Append("key=").Append(Uri.EscapeDataString(key));
            var url = sb.ToString();
            return await PerformSimpleGetAsync(url).ConfigureAwait(false);
        }

        /// <summary>
        /// Список поставщиков из /resto/api/suppliers (XML EmployeesList/Employee/taxpayerIdNumber).
        /// </summary>
        public async Task<List<IikoSupplier>> GetSuppliersAsync()
        {
            IikoLog.Write("GetSuppliersAsync start");
            var xml = await GetAsync("api/suppliers").ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(xml))
            {
                IikoLog.Write("GetSuppliersAsync: empty response");
                return new List<IikoSupplier>();
            }

            var preview = xml.Length > 1000 ? xml.Substring(0, 1000) + "..." : xml;
            IikoLog.Write("GetSuppliersAsync: raw xml preview = " + preview.Replace(Environment.NewLine, " "));

            XDocument doc;
            try
            {
                doc = XDocument.Parse(xml);
            }
            catch
            {
                IikoLog.Write("GetSuppliersAsync: failed to parse XML");
                return new List<IikoSupplier>();
            }

            var result = new List<IikoSupplier>();
            foreach (var emp in doc.Descendants("employee"))
            {
                var inn = (string)emp.Element("taxpayerIdNumber");
                if (string.IsNullOrWhiteSpace(inn))
                    continue;

                var id = (string)emp.Element("id") ?? (string)emp.Element("Id");
                var name = (string)emp.Element("name") ?? (string)emp.Element("Name");

                result.Add(new IikoSupplier
                {
                    Id = id ?? "",
                    Name = name ?? "",
                    Inn = inn.Trim()
                });
            }

            IikoLog.Write("GetSuppliersAsync: loaded suppliers count = " + result.Count);
            return result;
        }
    }
}

