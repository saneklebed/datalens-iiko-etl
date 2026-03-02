using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RoomBroomChainPlugin.Config;

namespace RoomBroomChainPlugin.Diadoc
{
    /// <summary>
    /// Вызовы API Диадока. Креды берутся из настроек плагина (ConfigStore).
    /// </summary>
    public class DiadocApiClient
    {
        private const string BaseUrl = "https://diadoc-api.kontur.ru";
        private readonly HttpClient _http;
        private readonly RoomBroomConfig _config;
        private string _token;

        public DiadocApiClient(RoomBroomConfig config)
        {
            _config = config ?? new RoomBroomConfig();
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add("Accept", "application/json; charset=utf-8");
        }

        /// <summary>Убирает пробелы/переносы и лишний слэш, чтобы заголовок Authorization не ломал парсер Диадока.</summary>
        private static string NormalizeToken(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            var s = value.Trim().Replace(" ", "").Replace("\r", "").Replace("\n", "");
            if (s.StartsWith("/")) s = s.Substring(1);
            return s;
        }

        /// <summary>Формат как в DiadocHttpApi.GetAuthorizationHeaderValue: DiadocAuth ddauth_api_client_id=...,ddauth_token=...</summary>
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

        /// <summary>Добавляет Authorization без разбора заголовка (иначе .NET может перепарсить и исказить значение).</summary>
        private static void SetAuthorization(HttpRequestMessage req, string value)
        {
            req.Headers.TryAddWithoutValidation("Authorization", value);
        }

        /// <summary>POST /V3/Authenticate?type=password → токен.</summary>
        public async Task<string> AuthenticateAsync()
        {
            if (string.IsNullOrWhiteSpace(_config.DiadocApiToken) || string.IsNullOrWhiteSpace(_config.DiadocLogin))
                throw new InvalidOperationException("Укажите в настройках Api Token и Логин Диадока.");
            var url = BaseUrl + "/V3/Authenticate?type=password";
            using (var req = new HttpRequestMessage(HttpMethod.Post, new Uri(url)))
            {
                SetAuthorization(req, "DiadocAuth ddauth_api_client_id=" + NormalizeToken(_config.DiadocApiToken ?? ""));
                req.Content = new StringContent(
                    JsonConvert.SerializeObject(new { login = _config.DiadocLogin, password = _config.DiadocPassword ?? "" }),
                    Encoding.UTF8,
                    "application/json");
                var resp = await _http.SendAsync(req).ConfigureAwait(false);
                await EnsureSuccessOrThrowAsync(resp).ConfigureAwait(false);
                var raw = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                _token = ParseTokenFromResponse(raw);
            }
            if (string.IsNullOrEmpty(_token))
                throw new InvalidOperationException("Диадок вернул пустой токен.");
            return _token;
        }

        /// <summary>Токен из ответа: либо сырая строка, либо JSON "token"/"Token". Убираем обрамляющие кавычки (иначе в заголовке будет ...token" и ошибка «формат недопустим»).</summary>
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
            // Снять одну ведущую и одну завершающую кавычку (ответ может быть JSON-строка "token" или token" без первой кавычки)
            if (s.Length >= 1 && s[0] == '"') s = s.Substring(1);
            if (s.Length >= 1 && s[s.Length - 1] == '"') s = s.Substring(0, s.Length - 1);
            s = s.Replace("\\\"", "\"");
            return NormalizeToken(s);
        }

        /// <summary>GET /GetMyOrganizations → список организаций и ящиков.</summary>
        public async Task<List<DiadocOrg>> GetMyOrganizationsAsync()
        {
            var ub = new UriBuilder(BaseUrl) { Path = "/GetMyOrganizations" };
            string json;
            using (var req = new HttpRequestMessage(HttpMethod.Get, ub.Uri))
            {
                SetAuthorization(req, AuthHeader());
                var resp = await _http.SendAsync(req).ConfigureAwait(false);
                await EnsureSuccessOrThrowAsync(resp).ConfigureAwait(false);
                json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
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
                    BoxId = boxId.Trim()
                });
            }
            return list;
        }

        /// <summary>GET /V3/GetCounteragents?myBoxId=... → контрагенты (Организация, ИНН, КПП). Используем HttpWebRequest, чтобы query-параметры гарантированно доходили до сервера.</summary>
        public async Task<List<CounteragentRow>> GetCounteragentsAsync(string myBoxId)
        {
            if (string.IsNullOrWhiteSpace(myBoxId))
                throw new InvalidOperationException("Выберите юр. лицо с ящиком Диадока (у выбранной организации нет BoxId).");
            myBoxId = myBoxId.Trim();
            var ub = new UriBuilder(BaseUrl) { Path = "/V3/GetCounteragents" };
            ub.Query = "myBoxId=" + Uri.EscapeDataString(myBoxId) + "&counteragentStatus=IsMyCounteragent&pageSize=100";
            string json = await GetStringWithWebRequestAsync(ub.Uri).ConfigureAwait(false);
            var root = JObject.Parse(json);
            var arr = root["Counteragents"] as JArray;
            var list = new List<CounteragentRow>();
            if (arr == null) return list;
            foreach (var c in arr)
            {
                var org = c["Organization"];
                if (org == null) continue;
                list.Add(new CounteragentRow
                {
                    Organization = (string)(org["FullName"] ?? org["ShortName"] ?? ""),
                    Inn = (string)org["Inn"] ?? "",
                    Kpp = (string)org["Kpp"] ?? ""
                });
            }
            return list;
        }

        /// <summary>GET /V3/GetDocuments (входящие или черновики). HttpWebRequest для надёжной передачи query.</summary>
        public async Task<List<DiadocDocumentRow>> GetDocumentsAsync(string boxId, bool incoming)
        {
            if (string.IsNullOrWhiteSpace(boxId))
                throw new InvalidOperationException("Выберите юр. лицо с ящиком Диадока (у выбранной организации нет BoxId).");
            boxId = boxId.Trim();
            var filter = incoming ? "Any.InboundNotRevoked" : "Any.Draft";
            var ub = new UriBuilder(BaseUrl) { Path = "/V3/GetDocuments" };
            ub.Query = "boxId=" + Uri.EscapeDataString(boxId) + "&filterCategory=" + Uri.EscapeDataString(filter) + "&count=100&sortDirection=Descending";
            string json = await GetStringWithWebRequestAsync(ub.Uri).ConfigureAwait(false);
            var root = JObject.Parse(json);
            var arr = root["Documents"] as JArray;
            var list = new List<DiadocDocumentRow>();
            if (arr == null) return list;
            foreach (var d in arr)
            {
                list.Add(new DiadocDocumentRow
                {
                    MessageId = (string)d["MessageId"],
                    EntityId = (string)d["EntityId"],
                    DocumentNumber = (string)d["DocumentNumber"],
                    DocumentDate = (string)d["DocumentDate"],
                    CounterpartyName = (string)d["CounterpartyName"]
                });
            }
            return list;
        }

        /// <summary>GET по URI через HttpWebRequest (query-параметры не теряются в отличие от части окружений с HttpClient).</summary>
        private async Task<string> GetStringWithWebRequestAsync(Uri uri)
        {
            var auth = AuthHeader();
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = "GET";
            request.Accept = "application/json; charset=utf-8";
            request.Headers["Authorization"] = auth;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            try
            {
                using (var response = (HttpWebResponse)await Task.Factory.FromAsync(request.BeginGetResponse, request.EndGetResponse, null).ConfigureAwait(false))
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }
            catch (WebException we)
            {
                var response = we.Response as HttpWebResponse;
                if (response != null)
                {
                    using (var stream = response.GetResponseStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                        string message = null;
                        try
                        {
                            var jo = JObject.Parse(body);
                            message = (string)jo["message"];
                        }
                        catch { }
                        if (string.IsNullOrEmpty(message))
                        {
                            if (response.StatusCode == HttpStatusCode.Unauthorized) message = "Неверный логин, пароль или API-токен Диадока.";
                            else if (response.StatusCode == (HttpStatusCode)429) message = "Превышен лимит запросов.";
                            else if ((int)response.StatusCode >= 500) message = "Сервис Диадока временно недоступен.";
                            else message = body ?? response.StatusDescription ?? response.StatusCode.ToString();
                        }
                        throw new InvalidOperationException(message);
                    }
                }
                throw new InvalidOperationException(we.Message, we);
            }
        }

        private static async Task EnsureSuccessOrThrowAsync(HttpResponseMessage resp)
        {
            if (resp.IsSuccessStatusCode) return;
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            string message = null;
            try
            {
                var jo = JObject.Parse(body);
                message = (string)jo["message"];
            }
            catch { }
            if (string.IsNullOrEmpty(message))
            {
                if (resp.StatusCode == HttpStatusCode.Unauthorized) message = "Неверный логин, пароль или API-токен Диадока.";
                else if (resp.StatusCode == (HttpStatusCode)429) message = "Превышен лимит запросов. Попробуйте позже.";
                else if ((int)resp.StatusCode >= 500) message = "Сервис Диадока временно недоступен.";
                else message = body ?? resp.ReasonPhrase ?? resp.StatusCode.ToString();
            }
            throw new InvalidOperationException(message);
        }
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
        public string DocumentNumber { get; set; }
        public string DocumentDate { get; set; }
        public string CounterpartyName { get; set; }
    }
}
