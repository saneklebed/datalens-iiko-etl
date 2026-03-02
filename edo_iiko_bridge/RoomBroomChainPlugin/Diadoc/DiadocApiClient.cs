using System;
using System.Collections.Generic;
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
            _http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
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
            using (var req = new HttpRequestMessage(HttpMethod.Post, url))
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

        /// <summary>Токен из ответа: либо сырая строка, либо JSON "token"/"Token", убираем кавычки по краям.</summary>
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
                    return NormalizeToken(t ?? "");
                }
                catch { return NormalizeToken(s); }
            }
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
                s = s.Substring(1, s.Length - 2).Replace("\\\"", "\"");
            return NormalizeToken(s);
        }

        /// <summary>GET /GetMyOrganizations → список организаций и ящиков.</summary>
        public async Task<List<DiadocOrg>> GetMyOrganizationsAsync()
        {
            var url = BaseUrl + "/GetMyOrganizations";
            string json;
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
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
                    boxId = (string)boxes[0]["BoxId"];
                    var title = (string)boxes[0]["Title"];
                    if (!string.IsNullOrEmpty(title)) name = title;
                }
                list.Add(new DiadocOrg
                {
                    OrgId = (string)o["OrgId"],
                    Name = name,
                    BoxId = boxId
                });
            }
            return list;
        }

        /// <summary>GET /V3/GetCounteragents?myBoxId=... → контрагенты (Организация, ИНН, КПП).</summary>
        public async Task<List<CounteragentRow>> GetCounteragentsAsync(string myBoxId)
        {
            if (string.IsNullOrEmpty(myBoxId))
                return new List<CounteragentRow>();
            var url = BaseUrl + "/V3/GetCounteragents?myBoxId=" + Uri.EscapeDataString(myBoxId) + "&counteragentStatus=IsMyCounteragent&pageSize=100";
            string json;
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            {
                SetAuthorization(req, AuthHeader());
                var resp = await _http.SendAsync(req).ConfigureAwait(false);
                await EnsureSuccessOrThrowAsync(resp).ConfigureAwait(false);
                json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
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

        /// <summary>GET /V3/GetDocuments (входящие или черновики).</summary>
        public async Task<List<DiadocDocumentRow>> GetDocumentsAsync(string boxId, bool incoming)
        {
            if (string.IsNullOrEmpty(boxId))
                return new List<DiadocDocumentRow>();
            var filter = incoming ? "Any.InboundNotRevoked" : "Any.Draft";
            var url = BaseUrl + "/V3/GetDocuments?boxId=" + Uri.EscapeDataString(boxId) + "&filterCategory=" + Uri.EscapeDataString(filter) + "&count=100&sortDirection=Descending";
            string json;
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            {
                SetAuthorization(req, AuthHeader());
                var resp = await _http.SendAsync(req).ConfigureAwait(false);
                await EnsureSuccessOrThrowAsync(resp).ConfigureAwait(false);
                json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
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
