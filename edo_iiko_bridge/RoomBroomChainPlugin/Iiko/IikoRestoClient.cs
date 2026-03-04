using System;
using System.Collections.Generic;
using System.Globalization;
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
        public string Code { get; set; }  // табельный/код для api suppliers/{code}/pricelist
        public string Name { get; set; }
        public string Inn { get; set; }
    }

    public class IikoStore
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    /// <summary>Строка прайс-листа поставщика: сопоставление «товар у поставщика» ↔ «наш товар» в iiko.</summary>
    public class SupplierPricelistItem
    {
        public string NativeProduct { get; set; }       // guid нашего товара
        public string NativeProductNum { get; set; }   // артикул у нас
        public string NativeProductName { get; set; }   // наименование товара в iiko (если есть в ответе API)
        public string SupplierProductNum { get; set; }  // артикул у поставщика
        public string SupplierProductCode { get; set; } // код у поставщика
        public string ContainerName { get; set; }       // наименование фасовки (container/name)
    }

    public class DocumentValidationResult
    {
        public string DocumentNumber { get; set; }
        public bool? Valid { get; set; }
        public bool? Warning { get; set; }
        public string RawXml { get; set; }
    }

    /// <summary>Краткая информация о приходной накладной iiko (incomingInvoice) для статусов.</summary>
    public class IncomingInvoiceInfo
    {
        /// <summary>Входящий номер внешнего документа (incomingDocumentNumber).</summary>
        public string IncomingDocumentNumber { get; set; }
        /// <summary>Номер накладной в iiko (documentNumber).</summary>
        public string DocumentNumber { get; set; }
        /// <summary>Статус документа: NEW / PROCESSED / DELETED.</summary>
        public string Status { get; set; }
        /// <summary>Поставщик (guid), если заполнен.</summary>
        public string SupplierId { get; set; }
    }

    /// <summary>Исключение при импорте накладной в iiko (4xx/5xx) — чтобы показать пользователю ответ сервера.</summary>
    public class IikoImportException : Exception
    {
        public int StatusCode { get; }
        public string ResponseBody { get; }

        public IikoImportException(string message, int statusCode, string responseBody)
            : base(message)
        {
            StatusCode = statusCode;
            ResponseBody = responseBody ?? "";
        }
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
        // Временный лог для диагностики импорта накладных в iiko на dev-машине.
        private const string ImportDebugLogPath =
            @"C:\Users\Orange\Documents\GitHub\datalens-iiko-etl\edo_iiko_bridge\dist\iiko_import_debug.log";

        private static string GetEnv(string name)
        {
            return (Environment.GetEnvironmentVariable(name) ?? "").Trim();
        }

        private static void SafeDebugLog(string message)
        {
            WriteImportDebugLog(message);
        }

        /// <summary>Пишет строку в лог импорта (dist/iiko_import_debug.log). Вызывать из UI при выгрузке, чтобы видеть storeId и т.д.</summary>
        public static void WriteImportDebugLog(string message)
        {
            try
            {
                var line = DateTime.Now.ToString("s") + " " + (message ?? "");
                var dir = Path.GetDirectoryName(ImportDebugLogPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.AppendAllText(ImportDebugLogPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Лог не должен ломать работу плагина.
            }
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

        private static async Task<string> PerformSimplePostAsync(string url, string body, string contentType)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.Timeout = 60000;

            var bytes = Encoding.UTF8.GetBytes(body ?? "");
            request.ContentType = contentType ?? "application/xml; charset=utf-8";
            request.ContentLength = bytes.Length;

            using (var stream = await request.GetRequestStreamAsync().ConfigureAwait(false))
            {
                await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            }

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
                string code = resp != null ? ((int)resp.StatusCode).ToString() : "no-status";
                string bodyText = null;
                int status = 0;
                try
                {
                    if (resp != null)
                    {
                        status = (int)resp.StatusCode;
                        using (var stream = resp.GetResponseStream())
                        {
                            if (stream != null)
                            {
                                using (var reader = new StreamReader(stream, Encoding.UTF8))
                                    bodyText = reader.ReadToEnd();
                            }
                        }
                    }
                }
                catch
                {
                    // ignore
                }

                IikoLog.Write("HTTP POST error " + code + " at " + url + " msg=" + we.Message);
                SafeDebugLog("HTTP POST error code=" + code + " url=" + url + " msg=" + we.Message +
                             " responseBody=" + (bodyText ?? "<null>"));
                throw new IikoImportException(we.Message ?? "Ошибка iiko", status, bodyText ?? "");
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

        private async Task<string> PostXmlAsync(string path, string xmlBody, bool debug = false)
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
            IikoLog.Write("POST XML " + url);
            if (debug)
                SafeDebugLog("POST " + url + " bodyLength=" + (xmlBody == null ? 0 : xmlBody.Length));
            return await PerformSimplePostAsync(url, xmlBody ?? "", "application/xml; charset=utf-8").ConfigureAwait(false);
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

                var code = (string)emp.Element("code") ?? (string)emp.Element("Code");
                result.Add(new IikoSupplier
                {
                    Id = id ?? "",
                    Code = code ?? "",
                    Name = name ?? "",
                    Inn = inn.Trim()
                });
            }

            IikoLog.Write("GetSuppliersAsync: loaded suppliers count = " + result.Count);
            return result;
        }

        /// <summary>
        /// Список складов /resto/api/corporation/stores.
        /// Пытается разобрать как JSON (id/name) или XML (store/id/name).
        /// </summary>
        public async Task<List<IikoStore>> GetStoresAsync()
        {
            IikoLog.Write("GetStoresAsync start");

            string raw = null;
            // Официальные эндпоинты (документация iiko): corporation/stores и corporation/departments возвращают corporateItemDto.
            foreach (var path in new[] { "api/corporation/stores?revisionFrom=-1", "api/corporation/departments?revisionFrom=-1", "api/corporation/stores", "api/corporation/departments", "api/stores", "api/nomenclature/stores", "api/departments" })
            {
                try
                {
                    raw = await GetAsync(path).ConfigureAwait(false);
                    IikoLog.Write("GetStoresAsync: path=" + path + " rawLength=" + (raw == null ? 0 : raw.Length));
                    if (!string.IsNullOrWhiteSpace(raw))
                        break;
                }
                catch (Exception ex)
                {
                    IikoLog.Write("GetStoresAsync: error at " + path + " msg=" + ex.Message);
                    raw = null;
                }
            }
            if (string.IsNullOrWhiteSpace(raw))
            {
                IikoLog.Write("GetStoresAsync: empty response");
                return new List<IikoStore>();
            }

            // Попытка JSON
            try
            {
                var token = JToken.Parse(raw);
                var items = new List<IikoStore>();
                if (token is JArray arr)
                {
                    foreach (var s in arr)
                    {
                        items.Add(new IikoStore
                        {
                            Id = (string)s["id"] ?? (string)s["Id"] ?? "",
                            Name = (string)s["name"] ?? (string)s["Name"] ?? ""
                        });
                    }
                }
                else if (token is JObject obj)
                {
                    var listToken = obj["stores"] ?? obj["items"] ?? obj["data"];
                    if (listToken is JArray arr2)
                    {
                        foreach (var s in arr2)
                        {
                            items.Add(new IikoStore
                            {
                                Id = (string)s["id"] ?? (string)s["Id"] ?? "",
                                Name = (string)s["name"] ?? (string)s["Name"] ?? ""
                            });
                        }
                    }
                }

                if (items.Count > 0)
                {
                    IikoLog.Write("GetStoresAsync: parsed JSON stores count=" + items.Count);
                    return items;
                }
            }
            catch
            {
                // не JSON — пробуем XML
            }

            // Попытка XML: corporateItemDto (api/corporation/stores и api/corporation/departments), store, department
            try
            {
                var doc = XDocument.Parse(raw);
                var result = new List<IikoStore>();
                foreach (var s in doc.Descendants())
                {
                    var local = s.Name.LocalName;
                    var id = (string)s.Element("id") ?? (string)s.Element("Id");
                    var code = (string)s.Element("code") ?? (string)s.Element("Code");
                    var name = (string)s.Element("name") ?? (string)s.Element("Name");
                    var typeEl = (string)s.Element("type") ?? (string)s.Element("Type");

                    // corporateItemDto (документация iiko: иерархия подразделений, список складов)
                    if (string.Equals(local, "corporateItemDto", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(code))
                            continue;
                        // В приходную накладную подходят только склады: STORE, CENTRALSTORE. Пустой type — ок (corporation/stores возвращает только склады).
                        if (!string.IsNullOrWhiteSpace(typeEl) &&
                            !string.Equals(typeEl, "STORE", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(typeEl, "CENTRALSTORE", StringComparison.OrdinalIgnoreCase))
                            continue;
                        result.Add(new IikoStore
                        {
                            Id = id ?? code ?? "",
                            Name = !string.IsNullOrWhiteSpace(name) ? name : (code ?? "")
                        });
                        continue;
                    }

                    var isStore = string.Equals(local, "store", StringComparison.OrdinalIgnoreCase) || string.Equals(local, "Store", StringComparison.OrdinalIgnoreCase);
                    var isDept = string.Equals(local, "department", StringComparison.OrdinalIgnoreCase) || string.Equals(local, "Department", StringComparison.OrdinalIgnoreCase);
                    if (!isStore && !isDept)
                        continue;
                    if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(name))
                        continue;
                    result.Add(new IikoStore
                    {
                        Id = !string.IsNullOrWhiteSpace(id) ? id : (code ?? ""),
                        Name = name ?? ""
                    });
                }

                IikoLog.Write("GetStoresAsync: parsed XML stores count=" + result.Count);
                return result;
            }
            catch
            {
                IikoLog.Write("GetStoresAsync: failed to parse response");
                return new List<IikoStore>();
            }
        }

        /// <summary>
        /// Прайс-лист поставщика: GET /resto/api/suppliers/{supplierIdOrCode}/pricelist.
        /// Сопоставление «код/артикул у поставщика» → «наш товар» (nativeProduct guid) для привязки строк накладной к номенклатуре iiko.
        /// </summary>
        public async Task<List<SupplierPricelistItem>> GetSupplierPricelistAsync(string supplierIdOrCode)
        {
            if (string.IsNullOrWhiteSpace(supplierIdOrCode))
                return new List<SupplierPricelistItem>();

            var path = "api/suppliers/" + Uri.EscapeDataString(supplierIdOrCode.Trim()) + "/pricelist";
            var raw = await GetAsync(path).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(raw))
                return new List<SupplierPricelistItem>();

            var result = new List<SupplierPricelistItem>();
            try
            {
                var doc = XDocument.Parse(raw);
                foreach (var el in doc.Descendants())
                {
                    var local = el.Name.LocalName;
                    if (!string.Equals(local, "supplierPriceListItemDto", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(local, "item", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var nativeProduct = (string)el.Element("nativeProduct") ?? (string)el.Element("NativeProduct");
                    var nativeProductNum = (string)el.Element("nativeProductNum") ?? (string)el.Element("NativeProductNum");
                    var nativeProductName = (string)el.Element("nativeProductName") ?? (string)el.Element("NativeProductName")
                        ?? (string)el.Element("productName") ?? (string)el.Element("name");
                    var supplierProductNum = (string)el.Element("supplierProductNum") ?? (string)el.Element("SupplierProductNum");
                    var supplierProductCode = (string)el.Element("supplierProductCode") ?? (string)el.Element("SupplierProductCode");
                    var containerEl = el.Element("container") ?? el.Element("Container");
                    var containerName = containerEl != null
                        ? ((string)containerEl.Element("name") ?? (string)containerEl.Element("Name"))
                        : null;
                    if (string.IsNullOrWhiteSpace(nativeProduct) && string.IsNullOrWhiteSpace(nativeProductNum))
                        continue;

                    result.Add(new SupplierPricelistItem
                    {
                        NativeProduct = nativeProduct ?? "",
                        NativeProductNum = nativeProductNum ?? "",
                        NativeProductName = nativeProductName ?? "",
                        SupplierProductNum = supplierProductNum ?? "",
                        SupplierProductCode = supplierProductCode ?? "",
                        ContainerName = containerName ?? ""
                    });
                }
                IikoLog.Write("GetSupplierPricelistAsync: loaded " + result.Count + " items for supplier " + supplierIdOrCode);
            }
            catch (Exception ex)
            {
                IikoLog.Write("GetSupplierPricelistAsync: parse error " + ex.Message);
            }
            return result;
        }

        /// <summary>
        /// Импорт приходной накладной: POST /resto/api/documents/import/incomingInvoice.
        /// </summary>
        public async Task<DocumentValidationResult> ImportIncomingInvoiceAsync(string xmlBody)
        {
            if (string.IsNullOrWhiteSpace(xmlBody))
                throw new ArgumentException("xmlBody");

            var raw = await PostXmlAsync("api/documents/import/incomingInvoice", xmlBody, debug: true).ConfigureAwait(false);
            var result = ParseDocumentValidationResult(raw);
            if (result != null)
                result.RawXml = raw ?? "";
            return result ?? new DocumentValidationResult { RawXml = raw ?? "" };
        }

        /// <summary>
        /// Экспорт приходных накладных за период: GET /resto/api/documents/export/incomingInvoice.
        /// Используется для сопоставления УПД с уже внесёнными приходами (incomingDocumentNumber → documentNumber/status).
        /// </summary>
        public async Task<List<IncomingInvoiceInfo>> GetIncomingInvoicesAsync(DateTime from, DateTime to)
        {
            // В REST API from/to включительно, время не учитывается.
            var fromStr = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var toStr = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var path = $"api/documents/export/incomingInvoice?from={fromStr}&to={toStr}";

            string raw;
            try
            {
                raw = await GetAsync(path).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                IikoLog.Write("GetIncomingInvoicesAsync: error " + ex.Message);
                return new List<IncomingInvoiceInfo>();
            }

            if (string.IsNullOrWhiteSpace(raw))
                return new List<IncomingInvoiceInfo>();

            var result = new List<IncomingInvoiceInfo>();
            try
            {
                var doc = XDocument.Parse(raw);
                foreach (var el in doc.Descendants())
                {
                    if (!string.Equals(el.Name.LocalName, "document", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var incomingNumber = (string)el.Element("incomingDocumentNumber") ?? (string)el.Element("IncomingDocumentNumber");
                    var docNumber = (string)el.Element("documentNumber") ?? (string)el.Element("DocumentNumber");
                    var status = (string)el.Element("status") ?? (string)el.Element("Status");
                    var supplier = (string)el.Element("supplier") ?? (string)el.Element("Supplier");

                    if (string.IsNullOrWhiteSpace(incomingNumber) && string.IsNullOrWhiteSpace(docNumber))
                        continue;

                    result.Add(new IncomingInvoiceInfo
                    {
                        IncomingDocumentNumber = incomingNumber ?? "",
                        DocumentNumber = docNumber ?? "",
                        Status = status ?? "",
                        SupplierId = supplier ?? ""
                    });
                }
                IikoLog.Write("GetIncomingInvoicesAsync: loaded " + result.Count + " documents");
            }
            catch (Exception ex)
            {
                IikoLog.Write("GetIncomingInvoicesAsync: parse error " + ex.Message);
            }

            return result;
        }

        private static DocumentValidationResult ParseDocumentValidationResult(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return new DocumentValidationResult { RawXml = raw ?? "" };

            try
            {
                var doc = XDocument.Parse(raw);
                var root = doc.Root;
                if (root == null || root.Name.LocalName != "documentValidationResult")
                    return new DocumentValidationResult { RawXml = raw };

                var docNumber = (string)root.Element("documentNumber") ?? (string)root.Element("DocumentNumber");
                bool? valid = null, warning = null;
                var validEl = root.Element("valid") ?? root.Element("Valid");
                if (validEl != null && bool.TryParse(validEl.Value, out var b))
                    valid = b;
                var warnEl = root.Element("warning") ?? root.Element("Warning");
                if (warnEl != null && bool.TryParse(warnEl.Value, out var w))
                    warning = w;

                return new DocumentValidationResult
                {
                    RawXml = raw,
                    DocumentNumber = docNumber,
                    Valid = valid,
                    Warning = warning
                };
            }
            catch
            {
                return new DocumentValidationResult { RawXml = raw };
            }
        }
    }
}
