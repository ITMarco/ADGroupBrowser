using System.DirectoryServices.Protocols;
using System.Net;
using System.Net.Sockets;

namespace ADGroupBrowser;

/// <summary>Thrown when no configured DC could be reached (TCP probe / bind all failed).</summary>
public sealed class NoReachableDomainControllerException : Exception
{
    public NoReachableDomainControllerException(string message) : base(message) { }
}

public class LdapService : IDisposable
{
    // LDAP_MATCHING_RULE_IN_CHAIN — server-side recursive memberOf expansion
    private const string InChainOid = "1.2.840.113556.1.4.1941";
    private const int LDAP_INVALID_CREDENTIALS = 49;

    private LdapConnection? _conn;
    private AppConfig _config = null!;

    /// <summary>The DC we actually bound to (null until a successful Connect).</summary>
    public DcEndpoint? ActiveEndpoint { get; private set; }

    /// <summary>
    /// Bind using the current Windows identity — no explicit credentials.
    /// Works on AADJ machines with cloud Kerberos trust or Hybrid AADJ.
    /// Throws on auth rejection (no failover to other DCs on credential failure).
    /// </summary>
    public void ConnectIntegrated(AppConfig config)
    {
        _config = config;
        var who       = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
        var endpoints = config.Endpoints.ToList();
        Shuffle(endpoints);

        Logger.Info("──────── LDAP Connect (integrated / SSO) ────────");
        Logger.Info($"  Candidates  : {string.Join(", ", endpoints.Select(e => e.ToString()))}");
        Logger.Info($"  SSL         : {config.UseSsl}");
        Logger.Info($"  Identity    : {who}");

        var failures = new List<string>();

        foreach (var ep in endpoints)
        {
            Logger.Info($"  Probing {ep} (TCP, {config.ConnectTimeoutMs} ms)…");
            if (!TcpAlive(ep.Host, ep.Port, config.ConnectTimeoutMs))
            {
                Logger.Warn($"  {ep} not reachable (TCP) — skipping.");
                failures.Add($"{ep}: not reachable (TCP)");
                continue;
            }

            Logger.Info($"  {ep} reachable; binding with current identity…");
            try
            {
                _conn = BindIntegrated(ep, config);
                ActiveEndpoint = ep;
                Logger.Info($"  Bind SUCCEEDED on {ep} as {who}.");
                return;
            }
            catch (LdapException lex) when (lex.ErrorCode == LDAP_INVALID_CREDENTIALS)
            {
                // The DC rejected our identity — same result on every DC, no point continuing.
                Logger.Exception($"  Bind on {ep}: integrated auth rejected", lex);
                throw;
            }
            catch (Exception ex)
            {
                Logger.Exception($"  Bind on {ep} failed — trying next DC", ex);
                failures.Add($"{ep}: {ex.Message}");
            }
        }

        var detail = failures.Count > 0 ? string.Join("\r\n", failures) : "(no domain controllers configured)";
        Logger.Error("  No domain controller could be reached (integrated).\r\n" + detail);
        throw new NoReachableDomainControllerException(
            "No domain controller could be reached.\r\n\r\n" + detail);
    }

    private static LdapConnection BindIntegrated(DcEndpoint ep, AppConfig config)
    {
        var identifier = new LdapDirectoryIdentifier(ep.Host, ep.Port, false, false);
        var connection = new LdapConnection(identifier);

        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
        connection.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);

        if (config.UseSsl)
        {
            connection.SessionOptions.SecureSocketLayer = true;
            connection.SessionOptions.VerifyServerCertificate = (_, cert) =>
            {
                Logger.Debug($"    cert from {ep.Host}: subject='{cert?.Subject}' — accepted.");
                return true;
            };
        }

        // AuthType.Negotiate without an explicit Credential uses the current Windows
        // identity token (Kerberos via cloud trust / hybrid join, with NTLM as fallback).
        connection.AuthType = AuthType.Negotiate;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        connection.Bind();
        sw.Stop();
        Logger.Info($"    bind() returned in {sw.ElapsedMilliseconds} ms.");
        return connection;
    }

    public void Connect(AppConfig config, string usernameInput, string password)
    {
        _config = config;
        var (user, dom) = ParseUser(usernameInput, config.Domain);

        // Round-robin: shuffle the candidate order each launch to spread load across DCs.
        var endpoints = config.Endpoints.ToList();
        Shuffle(endpoints);

        Logger.Info("──────── LDAP Connect ────────");
        Logger.Info($"  Candidates  : {string.Join(", ", endpoints.Select(e => e.ToString()))}");
        Logger.Info($"  SSL         : {config.UseSsl}");
        Logger.Info($"  Raw input   : '{usernameInput}'");
        Logger.Info($"  Parsed user : '{user}'  domain '{dom}'");
        Logger.Info($"  Password len: {password.Length}");   // length only, never the value

        var failures = new List<string>();

        foreach (var ep in endpoints)
        {
            Logger.Info($"  Probing {ep} (TCP, {config.ConnectTimeoutMs} ms)…");
            if (!TcpAlive(ep.Host, ep.Port, config.ConnectTimeoutMs))
            {
                Logger.Warn($"  {ep} not reachable (TCP) — skipping.");
                failures.Add($"{ep}: not reachable (TCP)");
                continue;
            }

            Logger.Info($"  {ep} reachable; binding…");
            try
            {
                _conn = Bind(ep, config, user, dom, password);
                ActiveEndpoint = ep;
                Logger.Info($"  Bind SUCCEEDED on {ep}.");
                return;
            }
            catch (LdapException lex) when (lex.ErrorCode == LDAP_INVALID_CREDENTIALS)
            {
                // Same credentials would fail on every DC — stop and report to the user.
                Logger.Exception($"  Bind on {ep}: invalid credentials (stop, no failover)", lex);
                throw;
            }
            catch (Exception ex)
            {
                Logger.Exception($"  Bind on {ep} failed — failing over to next DC", ex);
                failures.Add($"{ep}: {ex.Message}");
            }
        }

        var detail = failures.Count > 0 ? string.Join("\r\n", failures) : "(no domain controllers configured)";
        Logger.Error("  No domain controller could be reached.\r\n" + detail);
        throw new NoReachableDomainControllerException(
            "No domain controller could be reached.\r\n\r\n" + detail);
    }

    // Parse "jdoe" / "CONTOSO\jdoe" / "jdoe@contoso.local" into (user, domain).
    private static (string user, string dom) ParseUser(string usernameInput, string configDomain)
    {
        string user = usernameInput.Trim();
        string dom  = configDomain;

        if (user.Contains('\\'))
        {
            var idx = user.IndexOf('\\');
            dom  = user[..idx];
            user = user[(idx + 1)..];
        }
        else if (user.Contains('@'))
        {
            dom = "";   // UPN — let Negotiate resolve the domain
        }
        return (user, dom);
    }

    private static LdapConnection Bind(DcEndpoint ep, AppConfig config, string user, string dom, string password)
    {
        var identifier = new LdapDirectoryIdentifier(ep.Host, ep.Port, false, false);
        var connection = new LdapConnection(identifier);

        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
        connection.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);

        if (config.UseSsl)
        {
            connection.SessionOptions.SecureSocketLayer = true;
            connection.SessionOptions.VerifyServerCertificate = (_, cert) =>
            {
                Logger.Debug($"    cert from {ep.Host}: subject='{cert?.Subject}' — accepted.");
                return true;
            };
        }

        // Negotiate → Kerberos if possible, NTLM fallback; works from non-domain-joined machines
        connection.AuthType = AuthType.Negotiate;
        connection.Credential = new NetworkCredential(user, password, dom);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        connection.Bind();
        sw.Stop();
        Logger.Info($"    bind() returned in {sw.ElapsedMilliseconds} ms.");
        return connection;
    }

    // Quick TCP reachability check so a dead DC is skipped instantly instead of
    // hanging on the much longer LDAP bind timeout.
    private static bool TcpAlive(string host, int port, int timeoutMs)
    {
        try
        {
            using var tcp = new TcpClient();
            var task = tcp.ConnectAsync(host, port);
            return task.Wait(timeoutMs) && tcp.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static void Shuffle<T>(IList<T> list)
    {
        var rng = Random.Shared;
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // ── access gate (M2) ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns whether <paramref name="usernameInput"/> is a (nested) member of ANY of
    /// the allowed groups. Never cached. Empty allowedGroups = open access (granted).
    /// Throws on directory errors — callers treat any throw as a DENY (fail-closed).
    /// </summary>
    public (bool granted, string detail) CheckAccess(IReadOnlyList<string> allowedGroups, string usernameInput)
    {
        EnsureConnected();

        if (allowedGroups is null || allowedGroups.Count == 0)
        {
            Logger.Info("Access check: no allowed_groups configured — access open.");
            return (true, "No access restriction configured.");
        }

        Logger.Info("──────── Access check ────────");

        // 1. Resolve allowed groups to DNs (CN entries looked up in the directory).
        var dns = ResolveGroupDns(allowedGroups);
        if (dns.Count == 0)
        {
            Logger.Warn("  None of the configured allowed groups were found.");
            return (false, "None of the configured allowed groups could be found in the directory.");
        }

        // 2. Build "this user AND member-of-any-allowed-group (nested)" — a single, cheap, targeted query.
        var (sam, upn) = SplitForMatch(usernameInput);
        var userClause = !string.IsNullOrEmpty(upn)
            ? $"(|(sAMAccountName={EscapeDn(sam)})(userPrincipalName={EscapeDn(upn)}))"
            : $"(sAMAccountName={EscapeDn(sam)})";
        var orChain = string.Concat(dns.Select(d => $"(memberOf:{InChainOid}:={EscapeDn(d)})"));
        var filter  = $"(&{userClause}(|{orChain}))";

        Logger.Info($"  Match user  : sam='{sam}' upn='{upn}'");
        foreach (var d in dns) Logger.Info($"  Allowed DN  : {d}");
        Logger.Info($"  Filter      : {filter}");

        var request = new SearchRequest(_config.DomainDn, filter, SearchScope.Subtree, "distinguishedName");
        request.TimeLimit = TimeSpan.FromSeconds(_config.TimeoutSeconds);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = (SearchResponse)_conn!.SendRequest(request);
        sw.Stop();

        bool granted = response.Entries.Count > 0;
        Logger.Info($"  Result      : {(granted ? "GRANTED" : "DENIED")} ({response.Entries.Count} match, {sw.ElapsedMilliseconds} ms).");

        return granted
            ? (true, "Member of an allowed group.")
            : (false, "Your account is not a member of any group permitted to use this tool.");
    }

    // Resolve a mix of DNs and CNs to DNs (CNs looked up via one combined query).
    private List<string> ResolveGroupDns(IReadOnlyList<string> entries)
    {
        var dns = new List<string>();
        var cns = new List<string>();
        foreach (var e in entries)
        {
            var s = e.Trim();
            if (s.Length == 0) continue;
            if (LooksLikeDn(s)) dns.Add(s);
            else cns.Add(s);
        }

        if (cns.Count > 0)
        {
            var orCn  = string.Concat(cns.Select(c => $"(cn={EscapeDn(c)})"));
            var filter = $"(&(objectClass=group)(|{orCn}))";
            var req = new SearchRequest(_config.DomainDn, filter, SearchScope.Subtree, "distinguishedName", "cn");
            req.TimeLimit = TimeSpan.FromSeconds(_config.TimeoutSeconds);

            var resp = (SearchResponse)_conn!.SendRequest(req);
            var foundCns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SearchResultEntry en in resp.Entries)
            {
                dns.Add(GetAttr(en, "distinguishedName"));
                foundCns.Add(GetAttr(en, "cn"));
            }
            foreach (var c in cns)
                if (!foundCns.Contains(c)) Logger.Warn($"  Allowed group CN not found in directory: {c}");
        }

        return dns;
    }

    private static bool LooksLikeDn(string s) =>
        s.Contains('=') && s.Contains(',') &&
        (s.StartsWith("CN=", StringComparison.OrdinalIgnoreCase) ||
         s.Contains("OU=", StringComparison.OrdinalIgnoreCase) ||
         s.Contains("DC=", StringComparison.OrdinalIgnoreCase));

    // Derive a sAMAccountName + (optional) UPN to match against from the typed username.
    private static (string sam, string upn) SplitForMatch(string usernameInput)
    {
        var u = usernameInput.Trim();
        if (u.Contains('\\')) return (u[(u.IndexOf('\\') + 1)..], "");
        if (u.Contains('@'))  return (u[..u.IndexOf('@')], u);
        return (u, "");
    }

    /// <summary>
    /// Returns the groups under each configured OU, grouped by OU and honoring each
    /// OU's subtree flag (Subtree = OU + child OUs; otherwise OneLevel = only this OU).
    /// </summary>
    public List<OuGroupSection> GetGroupSections()
    {
        EnsureConnected();
        var sections = new List<OuGroupSection>();

        Logger.Info("──────── GetGroups ────────");
        foreach (var ou in _config.SearchOus)
        {
            var scope = ou.Subtree ? SearchScope.Subtree : SearchScope.OneLevel;
            Logger.Info($"  Search base : {ou.Dn}");
            Logger.Info($"  Scope       : {scope}  (subtree={ou.Subtree})");

            var groups = new List<AdGroup>();
            try
            {
                var request = new SearchRequest(
                    ou.Dn,
                    "(objectClass=group)",
                    scope,
                    "cn", "distinguishedName", "description", "mail", "groupType"
                );
                request.TimeLimit = TimeSpan.FromSeconds(_config.TimeoutSeconds);

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var response = (SearchResponse)_conn!.SendRequest(request);
                sw.Stop();
                Logger.Info($"  Response    : ResultCode={response.ResultCode}, " +
                            $"{response.Entries.Count} entries in {sw.ElapsedMilliseconds} ms.");

                foreach (SearchResultEntry entry in response.Entries)
                {
                    groups.Add(new AdGroup(
                        Name:              GetAttr(entry, "cn"),
                        DistinguishedName: GetAttr(entry, "distinguishedName"),
                        Description:       GetAttr(entry, "description"),
                        Mail:              GetAttr(entry, "mail"),
                        Scope:             ResolveGroupScope(entry)
                    ));
                }
            }
            catch (Exception ex)
            {
                Logger.Exception($"  GetGroups failed for OU '{ou.Dn}'", ex);
                throw;
            }

            groups = groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
            sections.Add(new OuGroupSection(ou.Dn, ou.ShortName, ou.Subtree, groups));
        }

        Logger.Info($"  Total groups: {sections.Sum(s => s.Groups.Count)} across {sections.Count} OU(s).");
        return sections;
    }

    /// <summary>Direct members only — one indexed back-link query, fast and light on the DC.</summary>
    public List<AdMember> GetMembersDirect(string groupDn)
        => SearchMembers(groupDn, $"(memberOf={EscapeDn(groupDn)})", "direct");

    /// <summary>Effective members — server-side recursive expansion via LDAP_MATCHING_RULE_IN_CHAIN. Heavier on the DC.</summary>
    public List<AdMember> GetMembersRecursive(string groupDn)
        => SearchMembers(groupDn, $"(memberOf:{InChainOid}:={EscapeDn(groupDn)})", "recursive");

    private List<AdMember> SearchMembers(string groupDn, string filter, string mode)
    {
        EnsureConnected();

        Logger.Info($"──────── GetMembers ({mode}) ────────");
        Logger.Info($"  Group DN    : {groupDn}");
        Logger.Info($"  Search base : {_config.DomainDn}");
        Logger.Info($"  Filter      : {filter}");

        try
        {
            var request = new SearchRequest(
                _config.DomainDn,
                filter,
                SearchScope.Subtree,
                "displayName", "cn", "sAMAccountName", "objectClass", "mail", "distinguishedName"
            );
            request.TimeLimit = TimeSpan.FromSeconds(_config.TimeoutSeconds);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var response = (SearchResponse)_conn!.SendRequest(request);
            sw.Stop();
            Logger.Info($"  Response    : ResultCode={response.ResultCode}, " +
                        $"{response.Entries.Count} entries in {sw.ElapsedMilliseconds} ms.");

            var members = new List<AdMember>();
            foreach (SearchResultEntry entry in response.Entries)
            {
                var classes = GetAttrMulti(entry, "objectClass");
                members.Add(new AdMember(
                    DisplayName:       GetDisplayName(entry),
                    SamAccountName:    GetAttr(entry, "sAMAccountName"),
                    Type:              ResolveObjectType(classes),
                    DistinguishedName: GetAttr(entry, "distinguishedName"),
                    Mail:              GetAttr(entry, "mail")
                ));
            }

            return members.OrderBy(m => m.Type).ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex)
        {
            Logger.Exception($"  GetMembers ({mode}) failed", ex);
            throw;
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string GetAttr(SearchResultEntry entry, string attr)
    {
        if (!entry.Attributes.Contains(attr)) return "";
        var vals = entry.Attributes[attr];
        return vals.Count > 0 ? vals[0]?.ToString() ?? "" : "";
    }

    private static string[] GetAttrMulti(SearchResultEntry entry, string attr)
    {
        if (!entry.Attributes.Contains(attr)) return Array.Empty<string>();
        var vals = entry.Attributes[attr];
        var result = new string[vals.Count];
        for (int i = 0; i < vals.Count; i++)
            result[i] = vals[i]?.ToString() ?? "";
        return result;
    }

    private static string GetDisplayName(SearchResultEntry entry)
    {
        var dn = GetAttr(entry, "displayName");
        if (!string.IsNullOrWhiteSpace(dn)) return dn;
        var cn = GetAttr(entry, "cn");
        return !string.IsNullOrWhiteSpace(cn) ? cn : GetAttr(entry, "sAMAccountName");
    }

    private static string ResolveObjectType(string[] classes)
    {
        var set = new HashSet<string>(classes, StringComparer.OrdinalIgnoreCase);
        if (set.Contains("computer")) return "Computer";
        if (set.Contains("group"))    return "Group";
        if (set.Contains("user"))     return "User";
        if (set.Contains("contact"))  return "Contact";
        return "Other";
    }

    private static string ResolveGroupScope(SearchResultEntry entry)
    {
        var raw = GetAttr(entry, "groupType");
        if (!int.TryParse(raw, out int gt)) return "";
        return (gt & 0xF) switch
        {
            2 => "Global",
            4 => "Domain Local",
            8 => "Universal",
            _ => ""
        };
    }

    private static string EscapeDn(string dn) =>
        dn.Replace("\\", "\\5c").Replace("(", "\\28").Replace(")", "\\29").Replace("*", "\\2a");

    private void EnsureConnected()
    {
        if (_conn is null) throw new InvalidOperationException("Not connected. Call Connect() first.");
    }

    public void Dispose()
    {
        _conn?.Dispose();
        _conn = null;
    }
}
