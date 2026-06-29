namespace ADGroupBrowser;

public record AdGroup(
    string Name,
    string DistinguishedName,
    string Description,
    string Mail,
    string Scope   // DomainLocal / Global / Universal
);

public record AdMember(
    string DisplayName,
    string SamAccountName,
    string Type,           // User / Group / Computer / Contact
    string DistinguishedName,
    string Mail
);

// Groups found under one configured search OU (the unit shown as a tree section).
public sealed record OuGroupSection(
    string OuDn,
    string OuName,         // short RDN value, e.g. "Organisation Groups"
    bool Subtree,          // true = included child OUs; false = this OU only
    List<AdGroup> Groups
);
