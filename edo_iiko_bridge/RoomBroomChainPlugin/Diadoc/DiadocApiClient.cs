using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Linq;
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
        // Временный лог для диагностики парсинга УПД на dev-машине.
        private static readonly string DebugLogPath = InitDebugLogPath();

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

        private static string InitDebugLogPath()
        {
            try
            {
                // Пытаемся писать лог рядом с DLL плагина (в папку Plugins на боевом сервере).
                var asmLocation = typeof(DiadocApiClient).Assembly.Location;
                var dir = Path.GetDirectoryName(asmLocation);
                if (string.IsNullOrEmpty(dir))
                    dir = AppDomain.CurrentDomain.BaseDirectory;
                if (string.IsNullOrEmpty(dir))
                    dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "RoomBroomChainPlugin");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "diadoc_debug.log");
            }
            catch
            {
                // Фолбэк — временная папка.
                var fallbackDir = Path.Combine(Path.GetTempPath(), "RoomBroomChainPlugin");
                try { Directory.CreateDirectory(fallbackDir); } catch { }
                return Path.Combine(fallbackDir, "diadoc_debug.log");
            }
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

        /// <summary>
        /// Вариант PerformRequestAsync, возвращающий сырые байты (без перекодировки в строку).
        /// Нужен для GenerateTitleXml, который отдаёт XML в windows-1251.
        /// </summary>
        private async Task<byte[]> PerformRequestBytesAsync(
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
                using (var ms = new MemoryStream())
                {
                    await stream.CopyToAsync(ms).ConfigureAwait(false);
                    return ms.ToArray();
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

                // Пишем в диагностический лог подробности HTTP-ошибки Диадока.
                SafeDebugLog(
                    $"HttpError: status={(int)httpResp.StatusCode} path={pathAndQuery} message={message} body={body}");

                return new InvalidOperationException(
                    $"[{(int)httpResp.StatusCode} {pathAndQuery}] {message}", we);
            }
        }

        #endregion

        #region Helpers / logging

        private static void SafeDebugLog(string message)
        {
            try
            {
                var line = DateTime.Now.ToString("s") + " " + (message ?? "");
                var dir = Path.GetDirectoryName(DebugLogPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.AppendAllText(DebugLogPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Лог не должен ломать работу плагина.
            }
        }

        #endregion

        #region Signing helpers

        private List<DiadocOrg> _cachedOrgs;

        /// <summary>
        /// Для SignerContent нужен GUID организации (OrgId), а с UI в подпись приходит boxId.
        /// Разрешаем OrgId по BoxId через GetMyOrganizations (с простым кэшем).
        /// </summary>
        private async Task<string> ResolveOrgGuidForSignerAsync(string boxId)
        {
            var cleanBoxId = StripBoxIdDomain(boxId?.Trim() ?? "");
            if (string.IsNullOrEmpty(cleanBoxId))
                return boxId;

            try
            {
                if (_cachedOrgs == null || _cachedOrgs.Count == 0)
                {
                    var orgs = await GetMyOrganizationsAsync().ConfigureAwait(false);
                    _cachedOrgs = orgs ?? new List<DiadocOrg>();
                }

                var match = _cachedOrgs.FirstOrDefault(o =>
                    string.Equals(StripBoxIdDomain(o.BoxId ?? ""), cleanBoxId, StringComparison.OrdinalIgnoreCase));

                if (match != null && !string.IsNullOrWhiteSpace(match.OrgId) &&
                    Guid.TryParse(match.OrgId, out var g))
                {
                    return g.ToString("D");
                }
            }
            catch
            {
                // Не удалось получить OrgId — вернём исходное значение, чтобы не ломать подпись полностью.
            }

            return cleanBoxId;
        }

        private static X509Certificate2 GetBestCertificateForSigning()
        {
            var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            try
            {
                var now = DateTime.Now;
                var candidates = store.Certificates
                    .Find(X509FindType.FindByTimeValid, now, false)
                    .Find(X509FindType.FindByKeyUsage, X509KeyUsageFlags.DigitalSignature, false)
                    .Cast<X509Certificate2>()
                    .Where(c => c.HasPrivateKey)
                    .ToList();

                if (candidates.Count == 0)
                    throw new InvalidOperationException("В хранилище пользователя нет подходящих сертификатов для подписи (действующий, с правом DigitalSignature и закрытым ключом).");

                // Сначала пробуем выбрать сертификат, выданный ФНС (как в интерфейсе Диадока),
                // чтобы использовать его для подписания УПД/УКД.
                var fnsCandidates = candidates
                    .Where(c => (c.Issuer ?? "").IndexOf("Федеральная налоговая служба", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

                List<X509Certificate2> pool = fnsCandidates.Count > 0 ? fnsCandidates : candidates;

                // В выбранном пуле берём самый новый сертификат по дате окончания действия (NotAfter).
                var cert = pool
                    .OrderByDescending(c => c.NotAfter)
                    .First();

                if (!cert.HasPrivateKey)
                    throw new InvalidOperationException("У выбранного сертификата отсутствует закрытый ключ.");

                return cert;
            }
            finally
            {
                store.Close();
            }
        }

        /// <summary>
        /// Генерация титула покупателя для входящего УПД через актуальный метод GenerateTitleXml.
        /// Возвращает готовый XML титула покупателя (байты UTF-8).
        /// </summary>
        private async Task<byte[]> GenerateBuyerTitleXmlAsync(string boxId, string messageId, string entityId)
        {
            if (string.IsNullOrWhiteSpace(boxId))
                throw new ArgumentException(nameof(boxId));
            if (string.IsNullOrWhiteSpace(messageId))
                throw new ArgumentException(nameof(messageId));
            if (string.IsNullOrWhiteSpace(entityId))
                throw new ArgumentException(nameof(entityId));

            var cleanBoxId = StripBoxIdDomain(boxId.Trim());

            // Для SignerContent нам нужен GUID организации (OrgId) и thumbprint сертификата.
            var orgGuid = await ResolveOrgGuidForSignerAsync(boxId).ConfigureAwait(false);
            var cert = GetBestCertificateForSigning();
            var thumb = (cert.Thumbprint ?? "").Replace(" ", "").ToUpperInvariant();

            // Минимальный упрощённый XML UserDataXsd для титула покупателя.
            // Формат строго по реальному титулу покупателя: ContentOperCode → Signers.
            var acceptanceDate = DateTime.Now.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.Append("<UniversalTransferDocumentBuyerTitle ");
            sb.Append("DocumentCreator=\"ИП СМИРНОВ АНТОН ВЛАДИМИРОВИЧ\" ");
            sb.Append("OperationContent=\"Товары приняты\" ");
            sb.Append("AcceptanceDate=\"").Append(SecurityElement.Escape(acceptanceDate)).Append("\">");
            sb.Append("<ContentOperCode TotalCode=\"1\" />");
            sb.Append("<Signers BoxId=\"").Append(SecurityElement.Escape(orgGuid)).Append("\">");
            sb.Append("<Signer SignerPowersConfirmationMethod=\"1\">");
            sb.Append("<Certificate CertificateThumbprint=\"").Append(thumb).Append("\" />");
            sb.Append("</Signer>");
            sb.Append("</Signers>");
            sb.Append("</UniversalTransferDocumentBuyerTitle>");
            var bodyXml = sb.ToString();
            var bodyBytes = Encoding.UTF8.GetBytes(bodyXml);

            // titleIndex=1 — титул покупателя; letterId/documentId — исходный УПД (титул продавца).
            var pq = new StringBuilder("GenerateTitleXml");
            pq.Append("?boxId=").Append(Uri.EscapeDataString(cleanBoxId));
            pq.Append("&documentTypeNamedId=").Append(Uri.EscapeDataString("UniversalTransferDocument"));
            pq.Append("&documentFunction=").Append(Uri.EscapeDataString("СЧФДОП"));
            pq.Append("&documentVersion=").Append(Uri.EscapeDataString("utd970_05_03_01"));
            pq.Append("&titleIndex=1");
            pq.Append("&letterId=").Append(Uri.EscapeDataString(messageId));
            pq.Append("&documentId=").Append(Uri.EscapeDataString(entityId));

            SafeDebugLog("GenerateBuyerTitleXmlAsync: requestXml=" + bodyXml);

            var resultBytes = await PerformRequestBytesAsync(
                "POST",
                pq.ToString(),
                AuthHeader(),
                bodyBytes,
                "application/xml; charset=utf-8"
            ).ConfigureAwait(false);

            SafeDebugLog("GenerateBuyerTitleXmlAsync: response bytes length=" + (resultBytes?.Length ?? 0));

            // Сохраним XML ответа для диагностики (Диадок отдаёт windows-1251).
            try
            {
                var enc1251 = Encoding.GetEncoding(1251);
                SafeDebugLog("GenerateBuyerTitleXmlAsync: responseXml=" + enc1251.GetString(resultBytes ?? new byte[0]));
            }
            catch { }

            return resultBytes ?? new byte[0];
        }

        private static string BuildSignerContentXml(string orgGuidOrBoxId, X509Certificate2 cert)
        {
            if (string.IsNullOrWhiteSpace(orgGuidOrBoxId))
                throw new ArgumentException("orgGuidOrBoxId");
            if (cert == null)
                throw new ArgumentNullException(nameof(cert));

            var thumb = (cert.Thumbprint ?? "").Replace(" ", "").ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(thumb))
                throw new InvalidOperationException("Не удалось получить отпечаток сертификата для SignerContent.");

            // BoxId в XSD описан как guid, поэтому используем GUID организации (OrgId).
            // Если приходит «сплошной» идентификатор без дефисов, пробуем привести его к формату GUID (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx).
            var boxForXml = orgGuidOrBoxId;
            if (!Guid.TryParse(boxForXml, out _))
            {
                if (boxForXml.Length == 32 && Guid.TryParseExact(boxForXml, "N", out var g))
                    boxForXml = g.ToString("D");
            }

            // Упрощённый XML-подписант по универсальному формату (пример из доки Диадока).
            // BoxId — GUID организации (OrgId), CertificateThumbprint — отпечаток используемого сертификата.
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.Append("<Signers BoxId=\"").Append(SecurityElement.Escape(boxForXml)).Append("\">");
            // Для формата УПД требуется указать способ подтверждения полномочий.
            // Используем значение "1" — «в соответствии с данными, содержащимися в электронной подписи».
            sb.Append("<Signer SignerPowersConfirmationMethod=\"1\">");
            sb.Append("<Certificate CertificateThumbprint=\"").Append(thumb).Append("\" />");
            sb.Append("</Signer></Signers>");
            return sb.ToString();
        }

        /// <summary>
        /// Упрощённый XML подписанта для служебных документов (receipt, rejection, correction request).
        /// Формат: TechnologicalSigner133UserContract1505.xsd — корневой элемент <Signer>, не <Signers>.
        /// </summary>
        private static string BuildServiceSignerContentXml(X509Certificate2 cert)
        {
            if (cert == null)
                throw new ArgumentNullException(nameof(cert));

            var certBase64 = Convert.ToBase64String(cert.RawData);

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.Append("<Signer SignerStatus=\"1\" SignatureType=\"1\">");
            sb.Append("<Certificate CertificateBytes=\"").Append(certBase64).Append("\" />");
            sb.Append("</Signer>");
            return sb.ToString();
        }

        private static byte[] SignContentWithUserCertificate(byte[] content, string purposeForUser)
        {
            if (content == null || content.Length == 0)
                throw new ArgumentException("Нет данных для подписи.", nameof(content));

            var cert = GetBestCertificateForSigning();

            var contentInfo = new ContentInfo(content);
            var cms = new SignedCms(contentInfo, detached: true);
            var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, cert)
            {
                IncludeOption = X509IncludeOption.EndCertOnly
            };
            cms.ComputeSignature(signer, false);
            return cms.Encode();
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

        /// <summary>
        /// POST /PrepareDocumentsToSign — подготовка входящего документа к подписи.
        /// Возвращает PatchedContentId и байты контента, которые нужно подписать CMS-подписью.
        /// </summary>
        public async Task<(string patchedContentId, byte[] contentToSign)> PrepareIncomingDocumentToSignAsync(
            string boxId, string messageId, string entityId)
        {
            if (string.IsNullOrWhiteSpace(boxId))
                throw new ArgumentException(nameof(boxId));
            if (string.IsNullOrWhiteSpace(messageId))
                throw new ArgumentException(nameof(messageId));
            if (string.IsNullOrWhiteSpace(entityId))
                throw new ArgumentException(nameof(entityId));

            SafeDebugLog($"PrepareIncomingDocumentToSignAsync: boxId={boxId} messageId={messageId} entityId={entityId}");

            // Для SignerContent переводим boxId → OrgId (GUID организации), как того требует XSD подписанта.
            var orgGuid = await ResolveOrgGuidForSignerAsync(boxId).ConfigureAwait(false);

            var cert = GetBestCertificateForSigning();
            SafeDebugLog(
                $"PrepareIncomingDocumentToSignAsync: using cert Subject={cert.Subject}, Issuer={cert.Issuer}, NotAfter={cert.NotAfter:yyyy-MM-dd}, orgGuid={orgGuid}");

            var signerXml = BuildSignerContentXml(orgGuid, cert);
            var signerBytes = Encoding.UTF8.GetBytes(signerXml);

            var body = new
            {
                BoxId = boxId,
                Documents = new[]
                {
                    new
                    {
                        DocumentId = new
                        {
                            MessageId = messageId,
                            EntityId = entityId
                        },
                        SignerContent = signerBytes
                    }
                }
            };

            var json = JsonConvert.SerializeObject(body);
            var bytes = Encoding.UTF8.GetBytes(json);

            // В официальной доке метод без версии; используем "PrepareDocumentsToSign".
            var raw = await PerformRequestAsync(
                "POST",
                "PrepareDocumentsToSign",
                AuthHeader(),
                bytes,
                "application/json; charset=utf-8"
            ).ConfigureAwait(false);

            var root = JObject.Parse(raw);
            var arr = root["DocumentPatchedContents"] as JArray;
            if (arr == null || arr.Count == 0)
                throw new InvalidOperationException("Диадок не вернул DocumentPatchedContents для подготовки к подписи.");

            var first = (JObject)arr[0];
            var patchedId = (string)first["PatchedContentId"];
            var contentBase64 = (string)first["Content"];
            if (string.IsNullOrEmpty(patchedId))
                throw new InvalidOperationException("Диадок вернул пустой PatchedContentId.");
            byte[] contentBytes = Array.Empty<byte>();
            if (!string.IsNullOrEmpty(contentBase64))
                contentBytes = Convert.FromBase64String(contentBase64);

            SafeDebugLog(
                $"PrepareIncomingDocumentToSignAsync: patchedContentId={patchedId}, contentLength={contentBytes.Length}");

            return (patchedId, contentBytes);
        }

        /// <summary>
        /// Подписать входящий документ:
        /// 1) GenerateTitleXml → XML титула покупателя (UniversalTransferDocumentBuyerTitle)
        /// 2) CMS-подпись этого XML
        /// 3) PostMessagePatch с RecipientTitles.
        /// </summary>
        public async Task SignIncomingDocumentAsync(string boxId, string messageId, string entityId)
        {
            SafeDebugLog($"SignIncomingDocumentAsync: start boxId={boxId} messageId={messageId} entityId={entityId}");

            // Генерируем корректный титул покупателя под конкретный входящий УПД.
            var buyerTitleXml = await GenerateBuyerTitleXmlAsync(boxId, messageId, entityId).ConfigureAwait(false);
            if (buyerTitleXml == null || buyerTitleXml.Length == 0)
                throw new InvalidOperationException("Диадок не вернул XML титула покупателя для подписи.");

            var signature = SignContentWithUserCertificate(
                buyerTitleXml,
                "Выберите сертификат для подписи входящего УПД в Диадоке.");

            SafeDebugLog($"SignIncomingDocumentAsync: contentLength={buyerTitleXml.Length}, signatureLength={signature.Length}");

            // Отправляем титул покупателя как RecipientTitle.
            var patch = new
            {
                BoxId = boxId,
                MessageId = messageId,
                RecipientTitles = new[]
                {
                    new
                    {
                        ParentEntityId = entityId,
                        SignedContent = new
                        {
                            Content = buyerTitleXml,
                            Signature = signature
                        },
                        NeedReceipt = false
                    }
                }
            };

            var json = JsonConvert.SerializeObject(patch);
            var bytes = Encoding.UTF8.GetBytes(json);

            var resp = await PerformRequestAsync(
                "POST",
                "V4/PostMessagePatch",
                AuthHeader(),
                bytes,
                "application/json; charset=utf-8"
            ).ConfigureAwait(false);

            SafeDebugLog("SignIncomingDocumentAsync: PostMessagePatch response=" + resp);
        }

        /// <summary>
        /// Отказ от подписи входящего документа:
        /// 1) GenerateSignatureRejectionXml (V2) — генерация XML отказа на стороне Диадока.
        /// 2) Подписываем полученный XML сертификатом.
        /// 3) PostMessagePatch с XmlSignatureRejections.
        /// </summary>
        public async Task RejectIncomingDocumentAsync(string boxId, string messageId, string entityId, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                reason = "Отказ в подписании документа.";

            SafeDebugLog($"RejectIncomingDocumentAsync: start boxId={boxId} messageId={messageId} entityId={entityId} reason={reason}");

            var cleanBoxId = StripBoxIdDomain(boxId.Trim());
            var cert = GetBestCertificateForSigning();
            var signerXml = BuildServiceSignerContentXml(cert);
            var signerBytes = Encoding.UTF8.GetBytes(signerXml);

            SafeDebugLog("RejectIncomingDocumentAsync: signerXml length=" + signerXml.Length);

            // 1) Генерируем XML отказа через Diadoc API.
            var genRequest = new
            {
                ErrorMessage = reason,
                MessageId = messageId,
                AttachmentId = entityId,
                SignerContent = signerBytes
            };
            var genJson = JsonConvert.SerializeObject(genRequest);
            var genBody = Encoding.UTF8.GetBytes(genJson);

            SafeDebugLog("RejectIncomingDocumentAsync: calling V2/GenerateSignatureRejectionXml");

            var rejectionXml = await PerformRequestBytesAsync(
                "POST",
                "V2/GenerateSignatureRejectionXml?boxId=" + Uri.EscapeDataString(cleanBoxId),
                AuthHeader(),
                genBody,
                "application/json; charset=utf-8"
            ).ConfigureAwait(false);

            SafeDebugLog("RejectIncomingDocumentAsync: rejection XML bytes=" + (rejectionXml?.Length ?? 0));

            if (rejectionXml == null || rejectionXml.Length == 0)
                throw new InvalidOperationException("Диадок не вернул XML отказа от подписи.");

            // 2) Подписываем XML отказа сертификатом.
            var signature = SignContentWithUserCertificate(
                rejectionXml,
                "Выберите сертификат для отказа в подписи документа в Диадоке.");

            SafeDebugLog($"RejectIncomingDocumentAsync: contentLength={rejectionXml.Length}, signatureLength={signature.Length}");

            // 3) Отправляем через PostMessagePatch.
            var patch = new
            {
                BoxId = boxId,
                MessageId = messageId,
                XmlSignatureRejections = new[]
                {
                    new
                    {
                        ParentEntityId = entityId,
                        SignedContent = new
                        {
                            Content = rejectionXml,
                            Signature = signature
                        }
                    }
                }
            };

            var json = JsonConvert.SerializeObject(patch);
            var bytes = Encoding.UTF8.GetBytes(json);

            var resp = await PerformRequestAsync(
                "POST",
                "V4/PostMessagePatch",
                AuthHeader(),
                bytes,
                "application/json; charset=utf-8"
            ).ConfigureAwait(false);

            SafeDebugLog("RejectIncomingDocumentAsync: PostMessagePatch response=" + resp);
        }

        /// <summary>
        /// Нормализует дату/время из Diadoc (CreationTimestamp и подобные) в формат "dd.MM.yyyy".
        /// Если распарсить не удалось — возвращает исходную строку.
        /// </summary>
        private static string NormalizeDisplayDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";

            DateTime dt;

            // Попытка 1: универсальный парсинг (ISO 8601, Roundtrip и т.п.).
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dt) ||
                DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dt) ||
                DateTime.TryParse(raw, null, DateTimeStyles.RoundtripKind, out dt))
            {
                return dt.ToLocalTime().ToString("dd.MM.yyyy");
            }

            // Попытка 2: явные шаблоны, которые мы видели в UI (MM/dd/yyyy HH:mm:ss и т.п.).
            var formats = new[]
            {
                "MM/dd/yyyy HH:mm:ss",
                "MM/dd/yyyy",
                "dd.MM.yyyy HH:mm:ss",
                "dd.MM.yyyy"
            };
            foreach (var fmt in formats)
            {
                if (DateTime.TryParseExact(raw, fmt, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out dt))
                {
                    return dt.ToString("dd.MM.yyyy");
                }
            }

            // Не смогли распарсить — отдаём как есть.
            return raw;
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

        /// <summary>GET /V3/GetCounteragents?myBoxId=... (с пагинацией — загружает всех).</summary>
        public async Task<List<CounteragentRow>> GetCounteragentsAsync(string myBoxId)
        {
            if (string.IsNullOrWhiteSpace(myBoxId))
                throw new InvalidOperationException(
                    "Выберите юр. лицо с ящиком Диадока (у выбранной организации нет BoxId).");

            var cleanBoxId = StripBoxIdDomain(myBoxId.Trim());
            var list = new List<CounteragentRow>();
            string afterIndexKey = null;

            for (int page = 0; page < 50; page++)
            {
                var pq = new StringBuilder("V3/GetCounteragents");
                pq.Append("?myBoxId=").Append(Uri.EscapeDataString(cleanBoxId));
                pq.Append("&counteragentStatus=IsMyCounteragent");
                pq.Append("&pageSize=100");
                if (!string.IsNullOrEmpty(afterIndexKey))
                    pq.Append("&afterIndexKey=").Append(Uri.EscapeDataString(afterIndexKey));

                var json = await PerformRequestAsync("GET", pq.ToString(), AuthHeader())
                    .ConfigureAwait(false);

                var root = JObject.Parse(json);
                var arr = root["Counteragents"] as JArray;
                if (arr == null || arr.Count == 0) break;

                string lastKey = null;
                foreach (var c in arr)
                {
                    var org = c["Organization"];
                    if (org == null) continue;
                    var shortName = (string)org["ShortName"];
                    var fullName = (string)org["FullName"];
                    var boxes = org["Boxes"] as JArray;
                    string cBoxId = null;
                    if (boxes != null && boxes.Count > 0)
                        cBoxId = (string)(boxes[0]["BoxId"] ?? boxes[0]["boxId"]);
                    list.Add(new CounteragentRow
                    {
                        Organization = ShortenOrganizationName(shortName, fullName),
                        Inn = (string)org["Inn"] ?? "",
                        Kpp = (string)org["Kpp"] ?? "",
                        BoxId = cBoxId ?? ""
                    });
                    lastKey = (string)c["IndexKey"];
                }

                if (arr.Count < 100 || string.IsNullOrEmpty(lastKey))
                    break;
                afterIndexKey = lastKey;
            }
            return list;
        }

        /// <summary>GET /V3/GetDocuments (входящие или черновики). fromDate/toDate в формате ДД.ММ.ГГГГ.</summary>
        public async Task<List<DiadocDocumentRow>> GetDocumentsAsync(
            string boxId, bool incoming,
            DateTime? fromDate = null, DateTime? toDate = null,
            List<CounteragentRow> counteragents = null)
        {
            if (string.IsNullOrWhiteSpace(boxId))
                throw new InvalidOperationException(
                    "Выберите юр. лицо с ящиком Диадока (у выбранной организации нет BoxId).");

            var cleanBoxId = StripBoxIdDomain(boxId.Trim());
            var filter = incoming ? "Any.InboundNotRevoked" : "Any.Draft";

            var boxToName = new Dictionary<string, CounteragentRow>(StringComparer.OrdinalIgnoreCase);
            var innToName = new Dictionary<string, CounteragentRow>(StringComparer.OrdinalIgnoreCase);
            if (counteragents != null)
            {
                foreach (var ca in counteragents)
                {
                    var key = StripBoxIdDomain(ca.BoxId ?? "");
                    if (!string.IsNullOrEmpty(key) && !boxToName.ContainsKey(key))
                        boxToName[key] = ca;
                    if (!string.IsNullOrEmpty(ca.Inn) && !innToName.ContainsKey(ca.Inn))
                        innToName[ca.Inn] = ca;
                }
            }

            var list = new List<DiadocDocumentRow>();
            string afterIndexKey = null;

            for (int page = 0; page < 50; page++)
            {
                var pq = new StringBuilder("V3/GetDocuments");
                pq.Append("?boxId=").Append(Uri.EscapeDataString(cleanBoxId));
                pq.Append("&filterCategory=").Append(Uri.EscapeDataString(filter));
                pq.Append("&count=100");
                pq.Append("&sortDirection=Descending");
                if (fromDate.HasValue)
                    pq.Append("&fromDocumentDate=").Append(Uri.EscapeDataString(fromDate.Value.ToString("dd.MM.yyyy")));
                if (toDate.HasValue)
                    pq.Append("&toDocumentDate=").Append(Uri.EscapeDataString(toDate.Value.ToString("dd.MM.yyyy")));
                if (!string.IsNullOrEmpty(afterIndexKey))
                    pq.Append("&afterIndexKey=").Append(Uri.EscapeDataString(afterIndexKey));

                var json = await PerformRequestAsync("GET", pq.ToString(), AuthHeader())
                    .ConfigureAwait(false);

                var root = JObject.Parse(json);
                var arr = root["Documents"] as JArray;
                if (arr == null || arr.Count == 0)
                    break;

                SafeDebugLog("GetDocumentsAsync: page=" + page + " docsFromDiadoc=" + arr.Count);

                string lastIndexKey = null;

                foreach (var d in arr)
                {
                // Логируем тип документа и метаданные, чтобы разбираться с нестандартными УПД (например, по конкретному ИНН).
                string typeNamedId = null;
                try
                {
                    var docTypeToken = d["DocumentType"];
                    if (docTypeToken != null)
                    {
                        if (docTypeToken.Type == JTokenType.Object)
                            typeNamedId = (string)docTypeToken["TypeNamedId"];
                        else if (docTypeToken.Type == JTokenType.String)
                            typeNamedId = (string)docTypeToken;
                    }
                }
                catch (Exception ex)
                {
                    SafeDebugLog("GetDocumentsAsync: error reading DocumentType for doc " +
                                 ((string)d["DocumentNumber"] ?? "<no-number>") + " msg=" + ex.Message +
                                 " json=" + d.ToString(Newtonsoft.Json.Formatting.None));
                }

                string sellerInnMeta = null;
                var metaForInn = d["Metadata"] as JArray;
                if (metaForInn != null)
                {
                    foreach (var m in metaForInn)
                    {
                        var k = (string)m["Key"];
                        var v = (string)m["Value"];
                        if (k == "SellerInn")
                        {
                            sellerInnMeta = v;
                            break;
                        }
                    }
                }

                SafeDebugLog("GetDocumentsAsync: doc=" + ((string)d["DocumentNumber"] ?? "<no-number>")
                             + " date=" + ((string)d["DocumentDate"] ?? "")
                             + " typeNamedId=" + (typeNamedId ?? "<null>")
                             + " SellerInn=" + (sellerInnMeta ?? "<null>"));

                // Фильтр "только УПД": оставляем документы семейства UniversalTransferDocument/UniversalCorrectionDocument
                // и, на всякий случай, XmlTorg12. Остальные (Nonformalized, ReconciliationAct, ProformaInvoice, Invoice и т.д.)
                // в грид "Входящие" не попадают.
                bool isUpd = false;
                var tId = typeNamedId ?? "";
                if (tId.IndexOf("UniversalTransferDocument", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    tId.IndexOf("UniversalCorrectionDocument", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    string.Equals(tId, "XmlTorg12", StringComparison.OrdinalIgnoreCase))
                {
                    isUpd = true;
                }
                else
                {
                    var title = ((string)d["Title"] ?? "").ToUpperInvariant();
                    if (title.Contains("УПД"))
                        isUpd = true;
                }

                if (!isUpd)
                    continue;

                // Отдельный маркер для диагностики поставщика Арсенал (ИНН 5048029448).
                if (string.Equals(sellerInnMeta, "5048029448", StringComparison.OrdinalIgnoreCase))
                {
                    SafeDebugLog("GetDocumentsAsync: *** Arsenal doc detected (SellerInn=5048029448) "
                                 + "number=" + ((string)d["DocumentNumber"] ?? "<no-number>")
                                 + " typeNamedId=" + (typeNamedId ?? "<null>"));
                }

                var meta = d["Metadata"] as JArray;
                string totalSum = null, totalVat = null, sellerInn = null;
                if (meta != null)
                {
                    foreach (var m in meta)
                    {
                        var k = (string)m["Key"];
                        var v = (string)m["Value"];
                        if (k == "TotalSum") totalSum = v;
                        else if (k == "TotalVat") totalVat = v;
                        else if (k == "SellerInn") sellerInn = v;
                    }
                }

                var statusText = (string)(d["DocflowStatus"]?["PrimaryStatus"]?["StatusText"]);

                var counteragentBoxId = StripBoxIdDomain((string)d["CounteragentBoxId"] ?? "");
                string senderName = null;
                string counterpartyInn = sellerInn;

                CounteragentRow matched = null;
                if (!string.IsNullOrEmpty(counteragentBoxId))
                    boxToName.TryGetValue(counteragentBoxId, out matched);
                if (matched == null && !string.IsNullOrEmpty(sellerInn))
                    innToName.TryGetValue(sellerInn, out matched);

                if (matched != null)
                {
                    senderName = matched.Organization;
                    if (string.IsNullOrEmpty(counterpartyInn))
                        counterpartyInn = matched.Inn;
                }
                if (string.IsNullOrEmpty(senderName))
                    senderName = (string)d["Title"] ?? "";

                var creationTs = (string)d["CreationTimestamp"];
                var sentToEdo = NormalizeDisplayDate(creationTs);

                list.Add(new DiadocDocumentRow
                {
                    MessageId = (string)d["MessageId"],
                    EntityId = (string)d["EntityId"],
                    DocumentNumber = (string)d["DocumentNumber"],
                    DocumentDate = (string)d["DocumentDate"],
                    CounterpartyName = senderName,
                    CounterpartyInn = counterpartyInn ?? "",
                    SentToEdo = sentToEdo,
                    Supplier = senderName,
                    TotalAmount = totalSum,
                    TotalVat = totalVat,
                    StatusText = statusText
                });
                    // Для следующей страницы пагинации.
                    lastIndexKey = (string)d["IndexKey"] ?? lastIndexKey;
                }

                if (string.IsNullOrEmpty(lastIndexKey))
                    break;
                afterIndexKey = lastIndexKey;
            }
            return list;
        }

        /// <summary>
        /// GET /V4/GetEntityContent → XML UniversalTransferDocument; парсит табличную часть (InvoiceTable/Item).
        /// </summary>
        public async Task<UtdItemRow[]> GetUtdItemsAsync(string boxId, string messageId, string entityId)
        {
            if (string.IsNullOrWhiteSpace(boxId))
                throw new ArgumentException("boxId");
            if (string.IsNullOrWhiteSpace(messageId))
                throw new ArgumentException("messageId");
            if (string.IsNullOrWhiteSpace(entityId))
                throw new ArgumentException("entityId");

            var cleanBoxId = StripBoxIdDomain(boxId.Trim());
            var pq = new StringBuilder("V4/GetEntityContent");
            pq.Append("?boxId=").Append(Uri.EscapeDataString(cleanBoxId));
            pq.Append("&messageId=").Append(Uri.EscapeDataString(messageId));
            pq.Append("&entityId=").Append(Uri.EscapeDataString(entityId));

            var url = BuildUrl(pq.ToString());
            LogRequest("GET", url);

            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.AllowAutoRedirect = true;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Timeout = 60000;
            var authHeaderValue = AuthHeader();
            if (!string.IsNullOrEmpty(authHeaderValue))
                request.Headers.Add("Authorization", authHeaderValue);

            string xml;
            try
            {
                using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                using (var stream = response.GetResponseStream())
                {
                    // Читаем как байты, чтобы корректно обработать windows-1251 и прочие кодировки.
                    using (var ms = new MemoryStream())
                    {
                        if (stream != null)
                            await stream.CopyToAsync(ms).ConfigureAwait(false);
                        var bytes = ms.ToArray();
                        // По умолчанию пробуем UTF-8.
                        xml = Encoding.UTF8.GetString(bytes);
                        // Если в заголовке XML указана windows-1251 — перекодируем.
                        var marker1 = "encoding=\"windows-1251\"";
                        var marker2 = "encoding='windows-1251'";
                        if ((xml != null && xml.IndexOf(marker1, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (xml != null && xml.IndexOf(marker2, StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            var enc1251 = Encoding.GetEncoding(1251);
                            xml = enc1251.GetString(bytes);
                        }
                    }
                }
            }
            catch (WebException we)
            {
                throw WrapWebException(we, pq.ToString());
            }

            if (string.IsNullOrWhiteSpace(xml))
                return Array.Empty<UtdItemRow>();

            SafeDebugLog("GetUtdItemsAsync: start");

            try
            {
                // Сохраняем последнюю полученную XML-накладную для диагностики.
                try
                {
                    var tmpDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    if (string.IsNullOrEmpty(tmpDir)) tmpDir = Path.GetTempPath();
                    var folder = Path.Combine(tmpDir, "RoomBroomChainPlugin");
                    Directory.CreateDirectory(folder);
                    var file = Path.Combine(folder, "utd_last.xml");
                    File.WriteAllText(file, xml ?? "", Encoding.UTF8);
                    SafeDebugLog("GetUtdItemsAsync: saved utd_last.xml to " + file);
                }
                catch { }

                var list = new List<UtdItemRow>();
                var doc = System.Xml.Linq.XDocument.Parse(xml);
                var root = doc.Root;
                if (root == null)
                {
                    SafeDebugLog("GetUtdItemsAsync: root is null");
                    return Array.Empty<UtdItemRow>();
                }

                // Вариант 1: UniversalTransferDocument (Table/Item)
                System.Xml.Linq.XElement table = null;
                foreach (var el in root.Descendants())
                {
                    var local = el.Name.LocalName;
                    if (local == "Table" || local == "InvoiceTable")
                    {
                        table = el;
                        break;
                    }
                }

                int index = 1;

                if (table != null)
                {
                    SafeDebugLog("GetUtdItemsAsync: using UTD Table/Item format");

                    foreach (var item in table.Elements())
                    {
                        if (item.Name.LocalName != "Item")
                            continue;

                        decimal GetDecimalAttr(string name)
                        {
                            var a = (string)item.Attribute(name);
                            if (decimal.TryParse(a, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v))
                                return v;
                            return 0m;
                        }

                        var prodName = (string)item.Attribute("Product") ?? "";
                        var row = new UtdItemRow
                        {
                            LineIndex = index++,
                            Product = prodName,
                            SupplierProductName = prodName,
                            Unit = (string)item.Attribute("Unit") ?? "",
                            UnitName = (string)item.Attribute("UnitName") ?? "",
                            Quantity = GetDecimalAttr("Quantity"),
                            Price = GetDecimalAttr("Price"),
                            Subtotal = GetDecimalAttr("Subtotal"),
                            Vat = GetDecimalAttr("Vat"),
                            ItemVendorCode = (string)item.Attribute("ItemVendorCode") ?? "",
                            ItemArticle = (string)item.Attribute("ItemArticle") ?? "",
                            Gtin = (string)item.Attribute("Gtin") ?? "",
                            ItemAdditionalInfo = (string)item.Attribute("ItemAdditionalInfo") ?? ""
                        };

                        list.Add(row);
                    }
                }
                else
                {
                    // Вариант 2: ФНС 5.02/5.03 (Файл/Документ/ТаблСчФакт/СведТов)
                    System.Xml.Linq.XElement table2 = null;
                    foreach (var el in root.Descendants())
                    {
                        if (el.Name.LocalName == "ТаблСчФакт")
                        {
                            table2 = el;
                            break;
                        }
                    }

                    if (table2 == null)
                    {
                        SafeDebugLog("GetUtdItemsAsync: ТаблСчФакт не найден");
                        return Array.Empty<UtdItemRow>();
                    }

                    SafeDebugLog("GetUtdItemsAsync: using ФНС ТаблСчФакт/СведТов format");

                    foreach (var item in table2.Elements())
                    {
                        if (item.Name.LocalName != "СведТов")
                            continue;

                        decimal GetDecimalAttrFromAttr(string name)
                        {
                            var a = (string)item.Attribute(name);
                            if (decimal.TryParse(a, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v))
                                return v;
                            return 0m;
                        }

                        decimal GetDecimalFromChild(string name)
                        {
                            var child = item.Element(item.GetDefaultNamespace() + name) ??
                                        item.Element(name);
                            var text = (string)child ?? "";
                            if (decimal.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v))
                                return v;
                            return 0m;
                        }

                        var dop = item.Element(item.GetDefaultNamespace() + "ДопСведТов") ??
                                  item.Element("ДопСведТов");

                        var nameAttr = (string)item.Attribute("НаимТов") ?? "";

                        var row = new UtdItemRow
                        {
                            LineIndex = index++,
                            Product = nameAttr,
                            SupplierProductName = nameAttr,
                            Unit = (string)item.Attribute("ОКЕИ_Тов") ?? "",
                            UnitName = (string)item.Attribute("НаимЕдИзм") ?? "",
                            Quantity = GetDecimalAttrFromAttr("КолТов"),
                            Price = GetDecimalAttrFromAttr("ЦенаТов"),
                            Subtotal = GetDecimalAttrFromAttr("СтТовУчНал"),
                            Vat = GetDecimalFromChild("СумНал"),
                            ItemVendorCode = (string)(dop?.Attribute("КодТов")) ?? "",
                            ItemArticle = (string)(dop?.Attribute("КодТов")) ?? "",
                            Gtin = "",
                            ItemAdditionalInfo = ""
                        };

                        list.Add(row);
                    }
                }

                SafeDebugLog("GetUtdItemsAsync: parsed rows count=" + list.Count);
                return list.ToArray();
            }
            catch (Exception ex)
            {
                SafeDebugLog("GetUtdItemsAsync: exception " + ex.GetType().Name + " " + ex.Message);
                // В случае неизвестного формата просто вернуть пустой список, чтобы не ломать UI.
                return Array.Empty<UtdItemRow>();
            }
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
        public string BoxId { get; set; }
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
        /// <summary>Найден ли поставщик с таким ИНН в iiko.</summary>
        public bool SupplierFound { get; set; }
        /// <summary>Поставщик (для входящих = отправитель).</summary>
        public string Supplier { get; set; }
        /// <summary>Накладная IIKO — при интеграции с iiko.</summary>
        public string IikoInvoice { get; set; }
        /// <summary>Идентификатор поставщика в iiko (если найден по ИНН).</summary>
        public string IikoSupplierId { get; set; }
        /// <summary>Статус накладной относительно iiko (не внесена / внесена без/с проведением / не требует внесения).</summary>
        public string IikoStatus { get; set; }
    }
}
