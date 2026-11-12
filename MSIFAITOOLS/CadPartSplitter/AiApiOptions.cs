using System;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace CadPartSplitter
{
    /// <summary>
    /// AI（OpenAI 兼容接口）的连接配置，供 <see cref="TzAiExtractor"/> 调用。
    ///
    /// <para>
    /// 取值优先级（与 <c>MSIFAITOOLS</c> 的 <c>AiApiOptions</c> 一致，两边的密钥可以共用一份）：
    /// 环境变量（<c>MSIF_AI_API_KEY</c> / <c>MSIF_AI_BASE_URL</c> / <c>MSIF_AI_MODEL</c>）
    /// → <c>%APPDATA%\MSIFAITOOLS\ai-settings.json</c>（MSIFAITOOLS 的【AI 设置】画面写的那份，
    /// 密钥用 DPAPI 加密）
    /// → <c>App.config</c> 的 <c>appSettings</c>（<c>AiApiKey</c> / <c>AiBaseUrl</c> / <c>AiModel</c> / <c>AiTimeoutSeconds</c>）
    /// → 内置默认值。
    /// </para>
    ///
    /// <para>
    /// 只依赖 .NET Framework 自带的部件（<c>System.Net.Http</c> / <c>System.Web.Extensions</c> /
    /// <c>System.Security</c>），不额外引 NuGet。
    /// </para>
    /// </summary>
    internal sealed class AiApiOptions
    {
        /// <summary>默认接口地址（OpenAI Compatible）。</summary>
        public const string DefaultBaseUrl = "https://api-ap-southeast-1.modelarts-maas.com/v1";

        /// <summary>默认模型（与 MSIFAITOOLS 相同）。</summary>
        public const string DefaultModel = "deepseek-v4-flash";

        /// <summary>默认请求超时（秒）。</summary>
        public const int DefaultTimeoutSeconds = 180;

        /// <summary>App.config 中的接口地址键名。</summary>
        public const string BaseUrlSettingKey = "AiBaseUrl";

        /// <summary>App.config 中的密钥键名。</summary>
        public const string ApiKeySettingKey = "AiApiKey";

        /// <summary>App.config 中的模型键名。</summary>
        public const string ModelSettingKey = "AiModel";

        /// <summary>App.config 中的超时键名。</summary>
        public const string TimeoutSettingKey = "AiTimeoutSeconds";

        /// <summary>覆盖接口地址的环境变量。</summary>
        public const string BaseUrlEnvironmentKey = "MSIF_AI_BASE_URL";

        /// <summary>覆盖密钥的环境变量（优先级最高，便于临时替换密钥）。</summary>
        public const string ApiKeyEnvironmentKey = "MSIF_AI_API_KEY";

        /// <summary>覆盖模型的环境变量。</summary>
        public const string ModelEnvironmentKey = "MSIF_AI_MODEL";

        /// <summary>MSIFAITOOLS 的用户配置文件（与它共用同一份 AI 设置）。</summary>
        private const string SharedSettingsFolderName = "MSIFAITOOLS";

        /// <summary>MSIFAITOOLS 的用户配置文件名。</summary>
        private const string SharedSettingsFileName = "ai-settings.json";

        /// <summary>加密值前缀（与 MSIFAITOOLS 的写法一致）。</summary>
        private const string EncryptedPrefix = "dpapi:";

        /// <summary>DPAPI 附加熵（与 MSIFAITOOLS 完全一致，否则解不开它加密的密钥）。</summary>
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MSIFAITOOLS.AiSettings.v1");

        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public AiApiOptions(string baseUrl, string apiKey, string model, int timeoutSeconds, string source)
        {
            BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim();
            ApiKey = string.IsNullOrWhiteSpace(apiKey) ? string.Empty : apiKey.Trim();
            Model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
            TimeoutSeconds = timeoutSeconds <= 0 ? DefaultTimeoutSeconds : timeoutSeconds;
            ConfigurationSource = string.IsNullOrWhiteSpace(source) ? "内置默认值" : source;
        }

        /// <summary>接口基础地址（不含 <c>/chat/completions</c>）。</summary>
        public string BaseUrl { get; private set; }

        /// <summary>API 密钥。</summary>
        public string ApiKey { get; private set; }

        /// <summary>模型名称。</summary>
        public string Model { get; private set; }

        /// <summary>单次请求超时（秒）。</summary>
        public int TimeoutSeconds { get; private set; }

        /// <summary>本次配置的实际来源（环境变量 / AI 设置画面 / App.config / 内置默认值）。</summary>
        public string ConfigurationSource { get; private set; }

        /// <summary>聊天补全接口的完整地址。</summary>
        public string Endpoint
        {
            get { return BaseUrl.TrimEnd('/') + "/chat/completions"; }
        }

        /// <summary>是否已配置密钥（未配置时不允许发起 AI 判定）。</summary>
        public bool IsConfigured
        {
            get { return ApiKey.Length > 0; }
        }

        /// <summary>最后一次读取配置失败的原因（为空表示读取正常）。</summary>
        public static string LastConfigurationError { get; private set; }

        /// <summary>MSIFAITOOLS 用户配置文件的全路径（<c>%APPDATA%\MSIFAITOOLS\ai-settings.json</c>）。</summary>
        public static string SharedSettingsFilePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    SharedSettingsFolderName, SharedSettingsFileName);
            }
        }

        /// <summary>
        /// 从环境变量、MSIFAITOOLS 用户配置与 App.config 读取配置。
        /// 读不到任何密钥时返回的对象 <see cref="IsConfigured"/> 为 false（不抛异常）。
        /// </summary>
        public static AiApiOptions FromConfiguration()
        {
            LastConfigurationError = string.Empty;

            SharedAiSettings shared = SharedAiSettings.Load();

            string baseUrlSource;
            string baseUrl = ReadSetting(BaseUrlEnvironmentKey, BaseUrlSettingKey, DefaultBaseUrl,
                shared == null ? null : shared.BaseUrl, out baseUrlSource);
            // 以密钥的来源代表整体配置来源（密钥是判定「是否已配置」的关键）
            string apiKeySource;
            string apiKey = ReadSetting(ApiKeyEnvironmentKey, ApiKeySettingKey, string.Empty,
                shared == null ? null : shared.ApiKey, out apiKeySource);
            string modelSource;
            string model = ReadSetting(ModelEnvironmentKey, ModelSettingKey, DefaultModel,
                shared == null ? null : shared.Model, out modelSource);

            int sharedTimeout = shared == null ? 0 : shared.TimeoutSeconds;
            int timeout;
            string timeoutSource;

            if (!int.TryParse(
                    ReadSetting(string.Empty, TimeoutSettingKey, string.Empty,
                        sharedTimeout > 0 ? sharedTimeout.ToString(CultureInfo.InvariantCulture) : null,
                        out timeoutSource),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out timeout))
            {
                timeout = DefaultTimeoutSeconds;
            }

            return new AiApiOptions(baseUrl, apiKey, model, timeout, apiKeySource);
        }

        /// <summary>
        /// 依次取「环境变量 → 用户配置 → App.config → 默认值」里第一个非空值，
        /// 并把实际来源写进 <paramref name="source"/>。
        /// </summary>
        private static string ReadSetting(string environmentKey, string settingKey, string defaultValue,
            string userValue, out string source)
        {
            source = "内置默认值";

            if (!string.IsNullOrWhiteSpace(environmentKey))
            {
                string environmentValue = TryReadEnvironment(environmentKey);

                if (!string.IsNullOrWhiteSpace(environmentValue))
                {
                    source = "环境变量（" + environmentKey + "）";
                    return environmentValue.Trim();
                }
            }

            if (!string.IsNullOrWhiteSpace(userValue))
            {
                source = "MSIFAITOOLS 的【AI 设置】（" + SharedSettingsFilePath + "）";
                return userValue.Trim();
            }

            string configValue = TryReadAppSetting(settingKey);

            if (!string.IsNullOrWhiteSpace(configValue))
            {
                source = "App.config（" + settingKey + "）";
                return configValue.Trim();
            }

            return defaultValue;
        }

        /// <summary>读取环境变量（读不到时返回 null，不抛异常）。</summary>
        private static string TryReadEnvironment(string key)
        {
            try
            {
                return Environment.GetEnvironmentVariable(key);
            }
            catch (Exception ex)
            {
                LastConfigurationError = "读取环境变量 " + key + " 失败：" + ex.Message;
                return null;
            }
        }

        /// <summary>读取 App.config 的 appSettings（配置系统异常时记下原因并返回 null）。</summary>
        private static string TryReadAppSetting(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            try
            {
                return ConfigurationManager.AppSettings[key];
            }
            catch (Exception ex)
            {
                LastConfigurationError = "读取 App.config 失败：" + ex.Message;
                return null;
            }
        }

        /// <summary>
        /// MSIFAITOOLS（<c>%APPDATA%\MSIFAITOOLS\ai-settings.json</c>）里保存的 AI 设置，
        /// 密钥可能是 DPAPI 加密的（<c>dpapi:</c> 前缀）或手工编辑的明文。
        /// </summary>
        private sealed class SharedAiSettings
        {
            public string BaseUrl;
            public string ApiKey;
            public string Model;
            public int TimeoutSeconds;

            /// <summary>读取文件；文件不存在或解析失败时返回 null（原因记进 LastConfigurationError）。</summary>
            public static SharedAiSettings Load()
            {
                string path = SharedSettingsFilePath;

                try
                {
                    if (!File.Exists(path))
                    {
                        return null;
                    }

                    System.Collections.Generic.Dictionary<string, object> data =
                        Serializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(
                            File.ReadAllText(path, Encoding.UTF8));

                    if (data == null)
                    {
                        return null;
                    }

                    SharedAiSettings settings = new SharedAiSettings();
                    settings.BaseUrl = GetText(data, "BaseUrl");
                    settings.ApiKey = Unprotect(GetText(data, "ApiKey"));
                    settings.Model = GetText(data, "Model");
                    settings.TimeoutSeconds = GetInt(data, "TimeoutSeconds");

                    return settings;
                }
                catch (Exception ex)
                {
                    LastConfigurationError = "读取 " + path + " 失败：" + ex.Message;
                    return null;
                }
            }

            /// <summary>按 key 取字符串（忽略大小写）。</summary>
            private static string GetText(System.Collections.Generic.Dictionary<string, object> data,
                string key)
            {
                foreach (System.Collections.Generic.KeyValuePair<string, object> pair in data)
                {
                    if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase) && pair.Value != null)
                    {
                        return Convert.ToString(pair.Value, CultureInfo.InvariantCulture);
                    }
                }

                return string.Empty;
            }

            /// <summary>按 key 取整数（取不到或不是数字时返回 0）。</summary>
            private static int GetInt(System.Collections.Generic.Dictionary<string, object> data, string key)
            {
                int value;

                if (int.TryParse(GetText(data, key), NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out value))
                {
                    return value;
                }

                return 0;
            }

            /// <summary>解开 <c>dpapi:</c> 前缀的值；明文原样返回，解不开时退回原值。</summary>
            private static string Unprotect(string value)
            {
                if (string.IsNullOrEmpty(value) ||
                    !value.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
                {
                    return value;
                }

                try
                {
                    byte[] encrypted = Convert.FromBase64String(value.Substring(EncryptedPrefix.Length));
                    byte[] plain = ProtectedData.Unprotect(encrypted, Entropy,
                        DataProtectionScope.CurrentUser);

                    return Encoding.UTF8.GetString(plain);
                }
                catch (Exception ex)
                {
                    LastConfigurationError = "解密 ai-settings.json 里的密钥失败（将按明文使用）：" + ex.Message;
                    return value.Substring(EncryptedPrefix.Length);
                }
            }
        }
    }
}
