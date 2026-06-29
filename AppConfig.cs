using System.Text.Json;
using System.Text.Json.Serialization;

namespace ADGroupBrowser;

/// <summary>A single domain-controller endpoint (host + port).</summary>
public sealed class DcEndpoint
{
    public string Host { get; }
    public int Port { get; }
    public DcEndpoint(string host, int port) { Host = host; Port = port; }
    public override string ToString() => $"{Host}:{Port}";
}

/// <summary>
/// A configured search OU plus its scope. In JSON it may be written either as a
/// plain string (= subtree) or as an object { "dn": "...", "subtree": true|false }.
/// </summary>
[JsonConverter(typeof(SearchOuConverter))]
public sealed class SearchOu
{
    public string Dn { get; set; } = "";
    public bool Subtree { get; set; } = true;   // true = this OU + child OUs; false = only this OU

    // "Organisation Groups" from "OU=Organisation Groups,OU=Groups,DC=…"
    [JsonIgnore]
    public string ShortName
    {
        get
        {
            var first = Dn.Split(',')[0];
            int eq = first.IndexOf('=');
            return (eq >= 0 ? first[(eq + 1)..] : first).Trim();
        }
    }
}

/// <summary>Reads a SearchOu from either a JSON string or an object; always writes the object form.</summary>
public sealed class SearchOuConverter : JsonConverter<SearchOu>
{
    public override SearchOu Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new SearchOu { Dn = reader.GetString() ?? "", Subtree = true };

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            string dn = root.TryGetProperty("dn", out var d) ? d.GetString() ?? "" : "";
            bool subtree = !root.TryGetProperty("subtree", out var s) || s.ValueKind != JsonValueKind.False;
            return new SearchOu { Dn = dn, Subtree = subtree };
        }

        throw new JsonException("search_ous entries must be a string or an object { \"dn\": \"...\", \"subtree\": true|false }.");
    }

    public override void Write(Utf8JsonWriter writer, SearchOu value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("dn", value.Dn);
        writer.WriteBoolean("subtree", value.Subtree);
        writer.WriteEndObject();
    }
}

public class AppConfig
{
    // Legacy single-DC field — still honored for backward compatibility.
    [JsonPropertyName("domain_controller")]
    public string DomainController { get; set; } = "";

    // Preferred: list of DCs. Entries may be "host" or "host:port".
    [JsonPropertyName("domain_controllers")]
    public List<string> DomainControllers { get; set; } = new();

    [JsonPropertyName("domain")]
    public string Domain { get; set; } = "";

    // Default port applied to any DC entry that doesn't specify its own.
    [JsonPropertyName("port")]
    public int Port { get; set; } = 636;

    [JsonPropertyName("use_ssl")]
    public bool UseSsl { get; set; } = true;

    [JsonPropertyName("timeout_seconds")]
    public int TimeoutSeconds { get; set; } = 30;

    // TCP pre-flight probe timeout per DC, before attempting the LDAP bind.
    [JsonPropertyName("connect_timeout_ms")]
    public int ConnectTimeoutMs { get; set; } = 1500;

    [JsonPropertyName("search_ous")]
    public List<SearchOu> SearchOus { get; set; } = new();

    // Access gate: user must be a (nested) member of ANY of these groups (CN or DN).
    // Empty = no restriction (everyone who can bind may use the tool).
    [JsonPropertyName("allowed_groups")]
    public List<string> AllowedGroups { get; set; } = new();

    // Audit log directory (UNC path or local path). Per-day CSV files are appended here.
    // Leave empty to disable audit logging.
    [JsonPropertyName("audit_log_path")]
    public string AuditLogPath { get; set; } = "";

    // How many days of audit log files to keep. Files older than this are deleted at startup.
    [JsonPropertyName("audit_log_retain_days")]
    public int AuditLogRetainDays { get; set; } = 31;

    // DC=contoso,DC=local  derived from "contoso.local"
    [JsonIgnore]
    public string DomainDn => string.Join(",", Domain.Split('.').Select(p => $"DC={p}"));

    // "CONTOSO" from "contoso.local" — shown in the login form username hint
    [JsonIgnore]
    public string NetBiosHint => Domain.Split('.')[0].ToUpper();

    /// <summary>
    /// All configured DC endpoints, parsed from <see cref="DomainControllers"/>
    /// (falling back to the legacy single <see cref="DomainController"/>).
    /// </summary>
    [JsonIgnore]
    public List<DcEndpoint> Endpoints
    {
        get
        {
            var raw = new List<string>(DomainControllers);
            if (raw.Count == 0 && !string.IsNullOrWhiteSpace(DomainController))
                raw.Add(DomainController);

            var list = new List<DcEndpoint>();
            foreach (var entry in raw)
            {
                var s = entry.Trim();
                if (s.Length == 0) continue;

                string host = s;
                int port = Port;

                // "host:port" — only split on the last colon and only if it's a number
                int colon = s.LastIndexOf(':');
                if (colon > 0 && int.TryParse(s.AsSpan(colon + 1), out int p))
                {
                    host = s[..colon].Trim();
                    port = p;
                }
                if (host.Length > 0)
                    list.Add(new DcEndpoint(host, port));
            }
            return list;
        }
    }

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Config file not found: {path}");

        var json = File.ReadAllText(path);
        var cfg = JsonSerializer.Deserialize<AppConfig>(json)
            ?? throw new InvalidOperationException("Failed to parse config.json");

        if (string.IsNullOrWhiteSpace(cfg.Domain))
            throw new InvalidOperationException("domain is required in config.json");
        if (cfg.Endpoints.Count == 0)
            throw new InvalidOperationException(
                "At least one domain controller is required (domain_controllers).");
        if (cfg.SearchOus.Count == 0)
            throw new InvalidOperationException("At least one entry in search_ous is required");

        return cfg;
    }
}
