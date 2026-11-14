// Windows Audio Keys - 새 버전 확인(알림 전용).
// GitHub의 최신 릴리스 메타데이터만 읽는다. 실행 파일을 내려받거나 교체하지 않으며,
// 서버 응답에 담긴 주소도 쓰지 않는다. 앱이 여는 주소는 검증된 버전으로 직접 만든다.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace GameAudioKeys
{
    // 릴리스 버전 "주.부.수정". 태그 형식 "v1.2.3" 또는 "1.2.3"만 받는다.
    internal sealed class ReleaseVersion : IComparable<ReleaseVersion>
    {
        // 각 자리는 숫자 1~5자(0~99999)다. 앞자리 0, 공백, 접미사(-beta 등)는 거부한다.
        private const int MaxPartLength = 5;

        public readonly int Major;
        public readonly int Minor;
        public readonly int Patch;

        private ReleaseVersion(int major, int minor, int patch)
        {
            this.Major = major;
            this.Minor = minor;
            this.Patch = patch;
        }

        public static bool TryParse(string text, out ReleaseVersion version)
        {
            version = null;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string body = text[0] == 'v' ? text.Substring(1) : text;
            string[] parts = body.Split('.');
            if (parts.Length != 3)
            {
                return false;
            }

            int[] values = new int[3];
            for (int i = 0; i < 3; i++)
            {
                if (!TryParsePart(parts[i], out values[i]))
                {
                    return false;
                }
            }

            version = new ReleaseVersion(values[0], values[1], values[2]);
            return true;
        }

        private static bool TryParsePart(string part, out int value)
        {
            value = 0;
            if (part.Length == 0 || part.Length > MaxPartLength)
            {
                return false;
            }

            if (part.Length > 1 && part[0] == '0')
            {
                return false;
            }

            for (int i = 0; i < part.Length; i++)
            {
                char ch = part[i];
                if (ch < '0' || ch > '9')
                {
                    return false;
                }

                value = (value * 10) + (ch - '0');
            }

            return true;
        }

        public int CompareTo(ReleaseVersion other)
        {
            if (other == null)
            {
                return 1;
            }

            if (this.Major != other.Major)
            {
                return this.Major.CompareTo(other.Major);
            }

            if (this.Minor != other.Minor)
            {
                return this.Minor.CompareTo(other.Minor);
            }

            return this.Patch.CompareTo(other.Patch);
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}", this.Major, this.Minor, this.Patch);
        }
    }

    internal enum UpdateStatus
    {
        UpToDate,
        UpdateAvailable,
        LocalNewer,
        Invalid
    }

    // GitHub 응답에서 판단에 필요한 값만 옮겨 둔다.
    internal sealed class LatestRelease
    {
        public string TagName;
        public bool Draft;
        public bool Prerelease;
    }

    internal sealed class UpdateCheckResult
    {
        public bool Success;
        public UpdateStatus Status;
        public ReleaseVersion Latest;
        public string MessageKo;

        public static UpdateCheckResult Failure(string messageKo)
        {
            UpdateCheckResult result = new UpdateCheckResult();
            result.Success = false;
            result.Status = UpdateStatus.Invalid;
            result.MessageKo = messageKo;
            return result;
        }
    }

    internal static class UpdateCheckLogic
    {
        public const string LatestReleaseApiUrl =
            "https://api.github.com/repos/devbattery/windows-audio-keys/releases/latest";

        // 앱이 여는 유일한 주소 형식: 이 접두사 + "v<주.부.수정>".
        public const string ReleaseTagPagePrefix =
            "https://github.com/devbattery/windows-audio-keys/releases/tag/";

        // 시작 직후에는 로그인 처리와 네트워크 준비가 겹치므로 잠시 기다린다.
        public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
        public static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);

        // 최신 릴리스 응답은 보통 수 KB다. 비정상적으로 큰 응답은 읽지 않는다.
        public const int MaxResponseBytes = 256 * 1024;

        public static ReleaseVersion CurrentVersion(string productVersion)
        {
            ReleaseVersion version;
            return ReleaseVersion.TryParse(productVersion, out version) ? version : null;
        }

        // JSON 해석은 예외를 던지지 않는다. 형식이 다르면 false와 이유를 돌려준다.
        public static bool TryParseLatestRelease(string json, out LatestRelease release, out string errorKo)
        {
            release = null;
            errorKo = null;
            if (string.IsNullOrEmpty(json))
            {
                errorKo = "업데이트 서버 응답이 비어 있습니다.";
                return false;
            }

            object root;
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = MaxResponseBytes;
                root = serializer.DeserializeObject(json);
            }
            catch (Exception)
            {
                errorKo = "업데이트 서버 응답을 해석하지 못했습니다.";
                return false;
            }

            IDictionary<string, object> map = root as IDictionary<string, object>;
            if (map == null)
            {
                errorKo = "업데이트 서버 응답 형식이 예상과 다릅니다.";
                return false;
            }

            object tag;
            if (!map.TryGetValue("tag_name", out tag) || !(tag is string))
            {
                errorKo = "업데이트 서버 응답에 릴리스 태그가 없습니다.";
                return false;
            }

            bool draft;
            bool prerelease;
            if (!TryReadOptionalBool(map, "draft", out draft) || !TryReadOptionalBool(map, "prerelease", out prerelease))
            {
                errorKo = "업데이트 서버 응답 형식이 예상과 다릅니다.";
                return false;
            }

            release = new LatestRelease();
            release.TagName = (string)tag;
            release.Draft = draft;
            release.Prerelease = prerelease;
            return true;
        }

        private static bool TryReadOptionalBool(IDictionary<string, object> map, string key, out bool value)
        {
            value = false;
            object raw;
            if (!map.TryGetValue(key, out raw) || raw == null)
            {
                return true;
            }

            if (!(raw is bool))
            {
                return false;
            }

            value = (bool)raw;
            return true;
        }

        // 초안·사전 릴리스·형식이 잘못된 태그는 알림 대상이 아니다.
        public static UpdateStatus Evaluate(ReleaseVersion current, LatestRelease latest, out ReleaseVersion latestVersion)
        {
            latestVersion = null;
            if (current == null || latest == null || latest.Draft || latest.Prerelease)
            {
                return UpdateStatus.Invalid;
            }

            if (!ReleaseVersion.TryParse(latest.TagName, out latestVersion))
            {
                latestVersion = null;
                return UpdateStatus.Invalid;
            }

            int order = latestVersion.CompareTo(current);
            if (order > 0)
            {
                return UpdateStatus.UpdateAvailable;
            }

            return order == 0 ? UpdateStatus.UpToDate : UpdateStatus.LocalNewer;
        }

        public static string ReleasePageUrl(ReleaseVersion version)
        {
            if (version == null)
            {
                throw new ArgumentNullException("version");
            }

            return ReleaseTagPagePrefix + "v" + version.ToString();
        }

        // 여는 주소를 한 번 더 검사한다. 정확히 "접두사 + v<버전>" 형식만 허용한다.
        public static bool IsAllowedReleaseUrl(string url)
        {
            if (url == null || !url.StartsWith(ReleaseTagPagePrefix, StringComparison.Ordinal))
            {
                return false;
            }

            string tag = url.Substring(ReleaseTagPagePrefix.Length);
            ReleaseVersion version;
            return tag.Length > 1
                && tag[0] == 'v'
                && ReleaseVersion.TryParse(tag, out version)
                && string.Equals(tag, "v" + version.ToString(), StringComparison.Ordinal);
        }

        // 네트워크 결과와 테스트용 가상 결과가 같은 판정을 거치도록 한곳에서 만든다.
        public static UpdateCheckResult BuildResult(ReleaseVersion current, LatestRelease latest)
        {
            ReleaseVersion latestVersion;
            UpdateStatus status = Evaluate(current, latest, out latestVersion);
            if (status == UpdateStatus.Invalid)
            {
                return UpdateCheckResult.Failure("업데이트 서버가 알 수 없는 릴리스 정보를 보냈습니다.");
            }

            UpdateCheckResult result = new UpdateCheckResult();
            result.Success = true;
            result.Status = status;
            result.Latest = latestVersion;
            return result;
        }
    }

    // GitHub REST API 호출. UI 스레드가 아닌 작업 스레드에서만 부른다. 예외를 던지지 않는다.
    internal static class UpdateClient
    {
        private const int TimeoutMilliseconds = 15000;

        public static UpdateCheckResult CheckLatest(ReleaseVersion current)
        {
            string body;
            string errorKo;
            if (!TryDownloadLatestJson(current, out body, out errorKo))
            {
                return UpdateCheckResult.Failure(errorKo);
            }

            LatestRelease release;
            if (!UpdateCheckLogic.TryParseLatestRelease(body, out release, out errorKo))
            {
                return UpdateCheckResult.Failure(errorKo);
            }

            return UpdateCheckLogic.BuildResult(current, release);
        }

        private static bool TryDownloadLatestJson(ReleaseVersion current, out string body, out string errorKo)
        {
            body = null;
            errorKo = null;
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(UpdateCheckLogic.LatestReleaseApiUrl);
                request.Method = "GET";
                request.Timeout = TimeoutMilliseconds;
                request.ReadWriteTimeout = TimeoutMilliseconds;
                // GitHub API는 User-Agent를 요구한다. 앱 이름과 버전만 보낸다.
                request.UserAgent = "WindowsAudioKeys/" + (current == null ? "unknown" : current.ToString());
                request.Accept = "application/vnd.github+json";
                request.Headers["X-GitHub-Api-Version"] = "2022-11-28";
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                request.AllowAutoRedirect = true;
                request.MaximumAutomaticRedirections = 3;

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                {
                    byte[] bytes;
                    if (!TryReadLimited(stream, UpdateCheckLogic.MaxResponseBytes, out bytes))
                    {
                        errorKo = "업데이트 서버 응답이 너무 커서 읽지 않았습니다.";
                        return false;
                    }

                    body = Encoding.UTF8.GetString(bytes);
                    return true;
                }
            }
            catch (WebException ex)
            {
                errorKo = DescribeWebError(ex);
                return false;
            }
            catch (Exception ex)
            {
                errorKo = "업데이트를 확인하지 못했습니다: " + ex.Message;
                return false;
            }
        }

        // 최대 크기보다 1바이트라도 더 오면 거부한다.
        private static bool TryReadLimited(Stream stream, int maxBytes, out byte[] bytes)
        {
            bytes = null;
            using (MemoryStream buffer = new MemoryStream())
            {
                byte[] chunk = new byte[8192];
                int read;
                while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    if (buffer.Length + read > maxBytes)
                    {
                        return false;
                    }

                    buffer.Write(chunk, 0, read);
                }

                bytes = buffer.ToArray();
                return true;
            }
        }

        private static string DescribeWebError(WebException ex)
        {
            HttpWebResponse response = ex.Response as HttpWebResponse;
            if (response != null)
            {
                int code = (int)response.StatusCode;
                response.Close();
                if (code == 404)
                {
                    return "아직 게시된 릴리스가 없습니다.";
                }

                if (code == 403 || code == 429)
                {
                    return "GitHub 요청 한도에 걸렸습니다. 잠시 후 다시 확인하세요.";
                }

                return "업데이트 서버 응답 오류(HTTP " + code.ToString(CultureInfo.InvariantCulture) + ")";
            }

            switch (ex.Status)
            {
                case WebExceptionStatus.Timeout:
                    return "업데이트 서버 응답 시간이 초과되었습니다.";
                case WebExceptionStatus.NameResolutionFailure:
                case WebExceptionStatus.ProxyNameResolutionFailure:
                    return "업데이트 서버 주소를 찾지 못했습니다. 인터넷 연결을 확인하세요.";
                case WebExceptionStatus.ConnectFailure:
                    return "업데이트 서버에 연결하지 못했습니다. 인터넷 연결을 확인하세요.";
                case WebExceptionStatus.SecureChannelFailure:
                case WebExceptionStatus.TrustFailure:
                    return "업데이트 서버와 보안 연결(TLS)을 만들지 못했습니다.";
                default:
                    return "업데이트 서버에 연결하지 못했습니다: " + ex.Message;
            }
        }
    }
}
