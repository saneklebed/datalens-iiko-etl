using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RoomBroomChainPlugin.Config;

namespace RoomBroomChainPlugin.Diadoc
{
    /// <summary>
    /// HTTP-клиент Диадока, максимально близкий к Diadoc.Api.Http.HttpClient из SDK 2.45.
    /// Использует HttpWebRequest (тот же стек, что и рабочий EDI-Doc плагин).
    /// </summary>
    public class DiadocApiClient
    {
        private const string BaseUrl = "https://diadoc-api.kontur.ru";
        private readonly RoomBroomConfig _config;
        private string _token;

        public DiadocApiClient(RoomBroomConfig config)
        {
            _config = config ?? new RoomBroomConfig();
        }

        #region URL / header helpers

        /// <summary>
        /// Формирует полный URL аналогично PrepareWebRequest из SDK:
        /// baseUrl + "/" + pathAndQuery (без дублирования слэша).
        /// </summary>
        private static string BuildUrl(string pathAndQuery)
        {
            if (pathAndQuery.StartsWith("/"))
                pathAndQuery = pathAndQuery.Substring(1);
            return BaseUrl + "/" + pathAndQuery;
        }

        private static string NormalizeToken(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            var s = value.Trim().Replace(" ", "").Replace("\r", "").Replace("\n", "");
            if (s.StartsWith("/")) s = s.Substring(1);
            return s;
        }

        /// <summary>
        /// Сокращённое название юр. лица: предпочитаем ShortName; если только FullName —
        /// заменяем «Общество с ограниченной ответственностью» → «ООО», «Акционерное общество» → «АО», убираем кавычки.
        /// </summary>
        private static string ShortenOrganizationName(string shortName, string fullName)
        {
            if (!string.IsNullOrWhiteSpace(shortName))
                return shortName.Trim();
            if (string.IsNullOrWhiteSpace(fullName))
                return "";
            var s = fullName.Trim();
            s = s.Replace("Общество с ограниченной ответственностью", "ООО");
            s = s.Replace("Акционерное общество", "АО");
            s = s.Replace("\"", "").Trim();
            return s;
        }

        /// <summary>
        /// BoxId из JSON может приходить как "guid@diadoc.ru".
        /// API ожидает чистый GUID без доменного суффикса.
        /// </summary>
        private static string StripBoxIdDomain(string boxId)
        {
            if (string.IsNullOrEmpty(boxId)) return boxId;
            var idx = boxId.IndexOf('@');
            return idx >= 0 ? boxId.Substring(0, idx) : boxId;
        }

        /// <summary>DiadocAuth ddauth_api_client_id=...,ddauth_token=...</summary>
        private string AuthHeader()
        {
            if (string.IsNullOrEmpty(_token))
                _token = AuthenticateAsync().GetAwaiter().GetResult();
            var clientId = NormalizeToken(_config.DiadocApiToken ?? "");
            var authToken = NormalizeToken(_token ?? "");
            var sb = new StringBuilder("DiadocAuth ");
            sb.AppendFormat("ddauth_api_client_id={0}", clientId);
            if (!string.IsNullOrEmpty(authToken))
                sb.AppendFormat(",ddauth_token={0}", authToken);
            return sb.ToString();
        }

        private static string ParseTokenFromResponse(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            var s = raw.Trim();
            if (s.StartsWith("{"))
            {
                try
                {
                    var jo = JObject.Parse(s);
                    var t = (string)jo["token"] ?? (string)jo["Token"];
                    s = t ?? "";
                }
                catch { }
            }
            if (s.Length >= 1 && s[0] == '"') s = s.Substring(1);
            if (s.Length >= 1 && s[s.Length - 1] == '"') s = s.Substring(0, s.Length - 1);
            s = s.Replace("\\\"", "\"");
            return NormalizeToken(s);
        }

        private static void LogRequest(string method, string url)
        {
            try
            {
                var dir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(dir)) dir = Path.GetTempPath();
                var folder = Path.Combine(dir, "RoomBroomChainPlugin");
                Directory.CreateDirectory(folder);
                var file = Path.Combine(folder, "diadoc_last_request.txt");
                File.WriteAllText(file,
                    DateTime.UtcNow.ToString("o") + "\n" + method + " " + url,
                    Encoding.UTF8);
            }
            catch { }
        }

        #endregion

        #region Low-level HTTP (HttpWebRequest — как в SDK)

        /// <summary>
        /// Создаёт и отправляет HttpWebRequest. Полная аналогия PrepareWebRequest + PerformHttpRequest из SDK.
        /// </summary>
        private async Task<string> PerformRequestAsync(
            string method,
            string pathAndQuery,
            string authHeaderValue,
            byte[] body = null,
            string bodyContentType = null)
        {
            var url = BuildUrl(pathAndQuery);
            LogRequest(method, url);

            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.Accept = "application/json";
            request.AllowAutoRedirect = true;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Timeout = 60000;

            if (!string.IsNullOrEmpty(authHeaderValue))
                request.Headers.Add("Authorization", authHeaderValue);

            if (body != null && body.Length > 0)
            {
                request.ContentType = bodyContentType ?? "application/json; charset=utf-8";
                request.ContentLength = body.Length;
                using (var stream = request.GetRequestStream())
                    stream.Write(body, 0, body.Length);
            }
            else
            {
                request.ContentLength = 0;
            }

            try
            {
                using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
                {
                    return await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }
            catch (WebException we)
            {
                throw WrapWebException(we, pathAndQuery);
            }
        }

        private static InvalidOperationException WrapWebException(WebException we, string pathAndQuery)
        {
            var httpResp = we.Response as HttpWebResponse;
            if (httpResp == null)
                return new InvalidOperationException("Ошибка соединения с Диадоком: " + we.Message, we);

            using (httpResp)
            using (var stream = httpResp.GetResponseStream())
            using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
            {
                var body = reader.ReadToEnd();
                string message = null;
                try { message = (string)JObject.Parse(body)["message"]; } catch { }

                if (string.IsNullOrEmpty(message))
                {
                    var code = httpResp.StatusCode;
                    if (code == HttpStatusCode.Unauthorized)
                        message = "Неверный логин, пароль или API-токен Диадока.";
                    else if ((int)code == 429)
                        message = "Превышен лимит запросов. Попробуйте позже.";
                    else if ((int)code >= 500)
                        message = "Сервис Диадока временно недоступен.";
                    else
                        message = string.IsNullOrEmpty(body)
                            ? httpResp.StatusDescription ?? code.ToString()
                            : body;
                }
                return new InvalidOperationException(
                    $"[{(int)httpResp.StatusCode} {pathAndQuery}] {message}", we);
            }
        }

        #endregion

        #region Diadoc API methods

        /// <summary>POST /V3/Authenticate?type=password → токен.</summary>
        public async Task<string> AuthenticateAsync()
        {
            if (string.IsNullOrWhiteSpace(_config.DiadocApiToken) || string.IsNullOrWhiteSpace(_config.DiadocLogin))
                throw new InvalidOperationException("Укажите в настройках Api Token и Логин Диадока.");

            var authHeader = "DiadocAuth ddauth_api_client_id=" + NormalizeToken(_config.DiadocApiToken);
            var bodyJson = JsonConvert.SerializeObject(new
            {
                login = _config.DiadocLogin,
                password = _config.DiadocPassword ?? ""
            });
            var bodyBytes = Encoding.UTF8.GetBytes(bodyJson);

            var raw = await PerformRequestAsync(
                "POST",
                "V3/Authenticate?type=password",
                authHeader,
                bodyBytes,
                "application/json; charset=utf-8"
            ).ConfigureAwait(false);

            _token = ParseTokenFromResponse(raw);
            if (string.IsNullOrEmpty(_token))
                throw new InvalidOperationException("Диадок вернул пустой токен.");
            return _token;
        }

        /// <summary>GET /GetMyOrganizations</summary>
        public async Task<List<DiadocOrg>> GetMyOrganizationsAsync()
        {
            var json = await PerformRequestAsync("GET", "GetMyOrganizations", AuthHeader())
                .ConfigureAwait(false);

            var root = JObject.Parse(json);
            var orgs = root["Organizations"] as JArray;
            var list = new List<DiadocOrg>();
            if (orgs == null) return list;

            foreach (var o in orgs)
            {
                var boxes = o["Boxes"] as JArray;
                string boxId = null;
                string name = (string)(o["ShortName"] ?? o["FullName"] ?? o["Inn"] ?? "—");
                if (boxes != null && boxes.Count > 0)
                {
                    var firstBox = boxes[0] as JObject;
                    if (firstBox != null)
                    {
                        boxId = (string)(firstBox["BoxId"] ?? firstBox["boxId"]);
                        var title = (string)(firstBox["Title"] ?? firstBox["title"]);
                        if (!string.IsNullOrEmpty(title)) name = title;
                    }
                }
                if (string.IsNullOrWhiteSpace(boxId)) continue;
                list.Add(new DiadocOrg
                {
                    OrgId = (string)o["OrgId"],
                    Name = name,
                    BoxId = StripBoxIdDomain(boxId.Trim())
                });
            }
            return list;
        }

        /// <summary>GET /V3/GetCounteragents?myBoxId=...</summary>
        public async Task<List<CounteragentRow>> GetCounteragentsAsync(string myBoxId)
        {
            if (string.IsNullOrWhiteSpace(myBoxId))
                throw new InvalidOperationException(
                    "Выберите юр. лицо с ящиком Диадока (у выбранной организации нет BoxId).");

            var cleanBoxId = StripBoxIdDomain(myBoxId.Trim());
            var pq = new StringBuilder("V3/GetCounteragents");
            pq.Append("?myBoxId=").Append(Uri.EscapeDataString(cleanBoxId));
            pq.Append("&counteragentStatus=IsMyCounteragent");
            pq.Append("&pageSize=100");

            var json = await PerformRequestAsync("GET", pq.ToString(), AuthHeader())
                .ConfigureAwait(false);

            var root = JObject.Parse(json);
            var arr = root["Counteragents"] as JArray;
            var list = new List<CounteragentRow>();
            if (arr == null) return list;

            foreach (var c in arr)
            {
                var org = c["Organization"];
                if (org == null) continue;
                var shortName = (string)org["ShortName"];
                var fullName = (string)org["FullName"];
                list.Add(new CounteragentRow
                {
                    Organization = ShortenOrganizationName(shortName, fullName),
                    Inn = (string)org["Inn"] ?? "",
                    Kpp = (string)org["Kpp"] ?? ""
                });
            }
            return list;
        }

        /// <summary>GET /V3/GetDocuments (входящие или черновики). fromDate/toDate в формате ДД.ММ.ГГГГ.</summary>
        public async Task<List<DiadocDocumentRow>> GetDocumentsAsync(string boxId, bool incoming, DateTime? fromDate = null, DateTime? toDate = null)
        {
            if (string.IsNullOrWhiteSpace(boxId))
                throw new InvalidOperationException(
                    "Выберите юр. лицо с ящиком Диадока (у выбранной организации нет BoxId).");

            var cleanBoxId = StripBoxIdDomain(boxId.Trim());
            var filter = incoming ? "Any.InboundNotRevoked" : "Any.Draft";
            var pq = new StringBuilder("V3/GetDocuments");
            pq.Append("?boxId=").Append(Uri.EscapeDataString(cleanBoxId));
            pq.Append("&filterCategory=").Append(Uri.EscapeDataString(filter));
            pq.Append("&count=100");
            pq.Append("&sortDirection=Descending");
            if (fromDate.HasValue)
                pq.Append("&fromDocumentDate=").Append(Uri.EscapeDataString(fromDate.Value.ToString("dd.MM.yyyy")));
            if (toDate.HasValue)
                pq.Append("&toDocumentDate=").Append(Uri.EscapeDataString(toDate.Value.ToString("dd.MM.yyyy")));

            var json = await PerformRequestAsync("GET", pq.ToString(), AuthHeader())
                .ConfigureAwait(false);

            var root = JObject.Parse(json);
            var arr = root["Documents"] as JArray;
            var list = new List<DiadocDocumentRow>();
            if (arr == null) return list;

            foreach (var d in arr)
            {
                var meta = d["Metadata"] as JArray;
                string totalSum = null, totalVat = null;
                if (meta != null)
                {
                    foreach (var m in meta)
                    {
                        var k = (string)m["Key"];
                        var v = (string)m["Value"];
                        if (k == "TotalSum") totalSum = v;
                        else if (k == "TotalVat") totalVat = v;
                    }
                }
                var statusText = (string)(d["DocflowStatus"]?["PrimaryStatus"]?["StatusText"]);
                var sender = (string)(d["CounterpartyName"] ?? d["SenderName"]);
                list.Add(new DiadocDocumentRow
                {
                    MessageId = (string)d["MessageId"],
                    EntityId = (string)d["EntityId"],
                    DocumentNumber = (string)d["DocumentNumber"],
                    DocumentDate = (string)d["DocumentDate"],
                    CounterpartyName = sender,
                    Supplier = sender,
                    TotalAmount = totalSum,
                    TotalVat = totalVat,
                    StatusText = statusText
                });
            }
            return list;
        }

        #endregion
    }

    public class DiadocOrg
    {
        public string OrgId { get; set; }
        public string Name { get; set; }
        public string BoxId { get; set; }
    }

    public class CounteragentRow
    {
        public string Organization { get; set; }
        public string Inn { get; set; }
        public string Kpp { get; set; }
    }

    public class DiadocDocumentRow
    {
        public string MessageId { get; set; }
        public string EntityId { get; set; }
        /// <summary>Отправитель (для колонки «Отправитель»).</summary>
        public string CounterpartyName { get; set; }
        public string DocumentNumber { get; set; }
        public string DocumentDate { get; set; }
        public string TotalAmount { get; set; }
        public string TotalVat { get; set; }
        public string StatusText { get; set; }
        /// <summary>ИНН контрагента, если есть в ответе API.</summary>
        public string CounterpartyInn { get; set; }
        /// <summary>Отправлен в ЗДО — дата доставки, при необходимости можно заполнять из API.</summary>
        public string SentToEdo { get; set; }
        /// <summary>Поставщик (для входящих = отправитель).</summary>
        public string Supplier { get; set; }
        /// <summary>Накладная IIKO — при интеграции с iiko.</summary>
        public string IikoInvoice { get; set; }
    }
}
